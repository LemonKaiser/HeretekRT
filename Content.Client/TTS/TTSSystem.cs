using Content.Shared.CCVar;
using Content.Shared.TTS;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client.TTS;

/// <summary>
/// Receives generated OGG data and plays it from the speaking entity or globally for previews.
/// </summary>
public sealed class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    private readonly MemoryContentRoot _contentRoot = new();
    private readonly Queue<PlayTTSEvent> _radioQueue = new();
    private static readonly ResPath Prefix = ResPath.Root / "TTS";
    private EntityUid? _activeRadioPlayback;
    private int _fileIndex;

    private const float WhisperFade = 4f;
    private const float MinimalVolume = -10f;
    private const float VoiceRange = 10f;
    private const float WhisperMuffledRange = 5f;

    private float _volume;
    private float _radioVolume;
    private int _radioQueueLimit;

    public override void Initialize()
    {
        _resources.AddRoot(Prefix, _contentRoot);
        _cfg.OnValueChanged(CCVars.TTSVolume, OnTTSVolumeChanged, true);
        _cfg.OnValueChanged(CCVars.TTSRadioVolume, OnTTSRadioVolumeChanged, true);
        _cfg.OnValueChanged(CCVars.TTSRadioQueueLimit, value => _radioQueueLimit = Math.Max(0, value), true);
        SubscribeNetworkEvent<PlayTTSEvent>(OnPlayTTS);
    }

    public override void Shutdown()
    {
        _cfg.UnsubValueChanged(CCVars.TTSVolume, OnTTSVolumeChanged);
        _cfg.UnsubValueChanged(CCVars.TTSRadioVolume, OnTTSRadioVolumeChanged);
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        TryPlayNextRadio();
    }

    public void RequestPreviewTTS(string voiceId)
    {
        RaiseNetworkEvent(new RequestPreviewTTSEvent(voiceId));
    }

    private void OnPlayTTS(PlayTTSEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.LocalTTSEnabled) ||
            ev.IsRadio && !_cfg.GetCVar(CCVars.LocalRadioTTSEnabled))
        {
            return;
        }

        if (ev.IsRadio)
        {
            if (_radioQueueLimit == 0 || _radioQueue.Count >= _radioQueueLimit)
                return;

            _radioQueue.Enqueue(ev);
            TryPlayNextRadio();
            return;
        }

        PlayTTS(ev);
    }

    private void TryPlayNextRadio()
    {
        if (!_cfg.GetCVar(CCVars.LocalTTSEnabled) || !_cfg.GetCVar(CCVars.LocalRadioTTSEnabled))
        {
            _radioQueue.Clear();
            return;
        }

        if (_activeRadioPlayback is { } active && !Deleted(active))
            return;

        _activeRadioPlayback = null;
        while (_radioQueue.TryDequeue(out var queued))
        {
            _activeRadioPlayback = PlayTTS(queued);
            if (_activeRadioPlayback != null)
                return;
        }
    }

    private EntityUid? PlayTTS(PlayTTSEvent ev)
    {
        var filePath = new ResPath($"{_fileIndex++}.ogg");
        _contentRoot.AddOrUpdateFile(filePath, ev.Data);

        try
        {
            var audioResource = new AudioResource();
            audioResource.Load(IoCManager.Instance!, Prefix / filePath);

            var parameters = AudioParams.Default
                .WithVolume(AdjustVolume(ev.IsWhisper, ev.IsRadio))
                .WithMaxDistance(ev.IsWhisper ? WhisperMuffledRange : VoiceRange);
            var specifier = new ResolvedPathSpecifier(Prefix / filePath);

            if (!ev.IsRadio && ev.SourceUid is { } netEntity)
            {
                if (!TryGetEntity(netEntity, out var source))
                    return null;

                return _audio.PlayEntity(audioResource.AudioStream, source.Value, specifier, parameters)?.Entity;
            }

            return _audio.PlayGlobal(audioResource.AudioStream, specifier, parameters)?.Entity;
        }
        catch (Exception exception)
        {
            Log.Warning($"Failed to play TTS audio: {exception.Message}");
            return null;
        }
        finally
        {
            _contentRoot.RemoveFile(filePath);
        }
    }

    private float AdjustVolume(bool isWhisper, bool isRadio)
    {
        var volume = MinimalVolume + SharedAudioSystem.GainToVolume(isRadio ? _radioVolume : _volume);
        return isWhisper ? volume - SharedAudioSystem.GainToVolume(WhisperFade) : volume;
    }

    private void OnTTSVolumeChanged(float volume)
    {
        _volume = volume;
    }

    private void OnTTSRadioVolumeChanged(float volume)
    {
        _radioVolume = volume;
    }
}
