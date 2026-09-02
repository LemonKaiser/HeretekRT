using System.Threading.Tasks;
using Content.Server._EinsteinEngines.Language;
using Content.Server.Chat.Systems;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._EinsteinEngines.Language.Components;
using Content.Shared._EinsteinEngines.Language.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Players.RateLimiting;
using Content.Shared.TTS;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.TTS;

/// <summary>
/// Generates and routes speech audio for entities with <see cref="TTSComponent"/>.
/// </summary>
public sealed partial class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly INetConfigurationManager _netCfg = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly TTSManager _ttsManager = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly string[] PreviewPhrases =
    [
        "Съешь же ещё этих мягких французских булок, да выпей чаю.",
        "Клоун, прекрати разбрасывать банановые кожурки офицерам под ноги!",
        "Капитан, вы уверены что хотите назначить клоуна на должность главы персонала?",
        "Эс Бэ! Тут человек в сером костюме, с тулбоксом и в маске! Помогите!",
        "Учёные, тут странная аномалия в баре! Она уже съела мима!",
        "Я надеюсь, что инженеры внимательно следят за сингулярностью.",
        "Вы слышали эти странные крики в техах? Мне кажется, туда ходить небезопасно.",
        "Вы не видели Гамлета? Мне кажется, он забегал к вам на кухню.",
        "Здесь есть доктор? Человек умирает от отравленного пончика! Нужна помощь!",
        "Вам нужно согласие и печать квартирмейстера, если вы хотите сделать заказ на партию дробовиков.",
        "Возле эвакуационного шаттла разгерметизация! Инженеры, нам срочно нужна ваша помощь!",
        "Бармен, налей мне самого крепкого вина, которое есть в твоих запасах!",
    ];

    private const int MaxMessageChars = 200;
    private readonly Dictionary<EntityUid, Task> _speechQueues = new();
    private bool _isEnabled;
    private int _roundGeneration;

    public override void Initialize()
    {
        _cfg.OnValueChanged(CCVars.TTSEnabled, value => _isEnabled = value, true);

        SubscribeLocalEvent<TTSComponent, EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeNetworkEvent<RequestPreviewTTSEvent>(OnRequestPreviewTTS);

        RegisterRateLimits();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _roundGeneration++;
        _speechQueues.Clear();
        _ttsManager.ResetCache();
    }

    private async void OnRequestPreviewTTS(RequestPreviewTTSEvent ev, EntitySessionEventArgs args)
    {
        if (!_isEnabled ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(ev.VoiceId, out var voice) ||
            HandleRateLimit(args.SenderSession) != RateLimitStatus.Allowed)
        {
            return;
        }

        var roundGeneration = _roundGeneration;
        var audio = await GenerateTTS(_random.Pick(PreviewPhrases), voice.Speaker);
        if (audio != null && IsCurrentRound(roundGeneration))
            RaiseNetworkEvent(new PlayTTSEvent(audio), Filter.SinglePlayer(args.SenderSession));
    }

    private async void OnEntitySpoke(EntityUid uid, TTSComponent component, EntitySpokeEvent args)
    {
        if (!_isEnabled ||
            args.Message.Length > MaxMessageChars ||
            string.IsNullOrWhiteSpace(component.VoicePrototypeId))
        {
            return;
        }

        var previous = _speechQueues.GetValueOrDefault(uid);
        var current = ProcessSpokenMessage(previous, uid, component.VoicePrototypeId, args, _roundGeneration);
        _speechQueues[uid] = current;

        try
        {
            await current;
        }
        catch (Exception exception)
        {
            Log.Error($"TTS task failed: {exception}");
        }
        finally
        {
            if (_speechQueues.GetValueOrDefault(uid) == current)
                _speechQueues.Remove(uid);
        }
    }

    private async Task ProcessSpokenMessage(
        Task? previous,
        EntityUid uid,
        string? voiceId,
        EntitySpokeEvent args,
        int roundGeneration)
    {
        if (previous != null)
        {
            try
            {
                await previous;
            }
            catch (Exception exception)
            {
                Log.Error($"Previous TTS task failed: {exception}");
            }
        }

        if (!IsCurrentRound(roundGeneration) || string.IsNullOrWhiteSpace(voiceId) || Deleted(uid))
            return;

        var voiceEvent = new TransformSpeakerVoiceEvent(uid, voiceId);
        RaiseLocalEvent(uid, voiceEvent);

        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(voiceEvent.VoiceId, out var voice))
            return;

        await SendSpeechAudio(uid, args.Message, voice.Speaker, args.IsWhisper, args.Language, roundGeneration);
    }

    private async Task SendSpeechAudio(
        EntityUid source,
        string message,
        string speaker,
        bool isWhisper,
        LanguagePrototype language,
        int roundGeneration)
    {
        if (!IsCurrentRound(roundGeneration) || Deleted(source))
            return;

        var clearRecipients = new List<ICommonSession>();
        var obfuscatedRecipients = new List<ICommonSession>();
        var transformQuery = GetEntityQuery<TransformComponent>();
        var sourcePosition = _xforms.GetWorldPosition(transformQuery.GetComponent(source), transformQuery);

        foreach (var session in Filter.Pvs(source).Recipients)
        {
            if (!_netCfg.GetClientCVar(session.Channel, CCVars.LocalTTSEnabled) ||
                session.AttachedEntity is not { Valid: true } listener)
                continue;

            var listenerPosition = _xforms.GetWorldPosition(transformQuery.GetComponent(listener), transformQuery);
            var distance = (sourcePosition - listenerPosition).Length();
            if (distance > ChatSystem.VoiceRange)
                continue;

            var isClear = CanUnderstandLanguage(listener, language.ID)
                          && (!isWhisper || distance <= ChatSystem.WhisperClearRange);
            (isClear ? clearRecipients : obfuscatedRecipients).Add(session);
        }

        if (clearRecipients.Count == 0 && obfuscatedRecipients.Count == 0)
            return;

        byte[]? clearAudio = null;
        if (clearRecipients.Count > 0)
        {
            clearAudio = await GenerateTTS(message, speaker);
            if (clearAudio != null && IsCurrentRound(roundGeneration) && !Deleted(source))
            {
                var clearEvent = new PlayTTSEvent(clearAudio, GetNetEntity(source), isWhisper);
                foreach (var session in clearRecipients)
                {
                    RaiseNetworkEvent(clearEvent, session);
                }
            }
        }

        if (obfuscatedRecipients.Count == 0 || !IsCurrentRound(roundGeneration))
            return;

        var obfuscatedMessage = _language.ObfuscateSpeech(message, language);
        var obfuscatedAudio = string.Equals(message, obfuscatedMessage, StringComparison.Ordinal)
            ? clearAudio ?? await GenerateTTS(message, speaker)
            : await GenerateTTS(obfuscatedMessage, speaker);
        if (obfuscatedAudio == null || !IsCurrentRound(roundGeneration) || Deleted(source))
            return;

        var obfuscatedEvent = new PlayTTSEvent(obfuscatedAudio, GetNetEntity(source), isWhisper);
        foreach (var session in obfuscatedRecipients)
        {
            RaiseNetworkEvent(obfuscatedEvent, session);
        }
    }

    public void OnlyPlayerTTS(
        EntityUid source,
        string message,
        string? voiceId,
        ICommonSession session,
        bool isWhisper,
        LanguagePrototype language,
        bool isRadio = false)
    {
        _ = OnlyPlayerTTSAsync(source, message, voiceId, session, isWhisper, language, isRadio, _roundGeneration);
    }

    private async Task OnlyPlayerTTSAsync(
        EntityUid source,
        string message,
        string? voiceId,
        ICommonSession session,
        bool isWhisper,
        LanguagePrototype language,
        bool isRadio,
        int roundGeneration)
    {
        if (!_isEnabled ||
            message.Length > MaxMessageChars ||
            string.IsNullOrWhiteSpace(voiceId) ||
            !_netCfg.GetClientCVar(session.Channel, CCVars.LocalTTSEnabled) ||
            isRadio && !_netCfg.GetClientCVar(session.Channel, CCVars.LocalRadioTTSEnabled) ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(voiceId, out var voice))
        {
            return;
        }

        if (session.AttachedEntity is not { Valid: true } listener)
            return;

        var isClear = CanUnderstandLanguage(listener, language.ID);
        var text = isClear ? message : _language.ObfuscateSpeech(message, language);
        var audio = await GenerateTTS(text, voice.Speaker);
        if (audio == null || !IsCurrentRound(roundGeneration))
            return;

        RaiseNetworkEvent(new PlayTTSEvent(audio, isRadio ? null : GetNetEntity(source), isWhisper, isRadio), session);
    }

    private bool CanUnderstandLanguage(EntityUid listener, string languageId)
    {
        if (languageId == SharedLanguageSystem.UniversalPrototype ||
            languageId == SharedLanguageSystem.PsychomanticPrototype)
        {
            return true;
        }

        if (TryComp<UniversalLanguageSpeakerComponent>(listener, out var universal) && universal.Enabled)
            return true;

        return TryComp<LanguageSpeakerComponent>(listener, out var speaker)
               && speaker.UnderstoodLanguages.Contains(languageId);
    }

    private bool IsCurrentRound(int roundGeneration)
    {
        return _isEnabled && _roundGeneration == roundGeneration;
    }

    private async Task<byte[]?> GenerateTTS(string text, string speaker)
    {
        var sanitized = Sanitize(text);
        if (sanitized.Length == 0)
            return null;

        if (char.IsLetter(sanitized[^1]))
            sanitized += ".";

        return await _ttsManager.ConvertTextToSpeech(speaker, sanitized);
    }
}
