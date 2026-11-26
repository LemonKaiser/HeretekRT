using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.Localizations;
using Content.Shared._WH40K.Chat;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Chat;

/// <summary>Queues optional translations of already-authorized visible chat deliveries.</summary>
public sealed class Wh40kChatTranslationSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private WH40KPlayerCultureManager _cultures = default!;

    private const int MaxPending = 32;
    private const int MaxActive = 2;
    private const int MaxDeliveriesPerWork = 512;
    private static readonly TimeSpan MaxQueueAge = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ProviderCooldown = TimeSpan.FromMinutes(1);

    private readonly Dictionary<(string Text, string Culture), Work> _pending = new();
    private readonly Queue<Work> _queue = new();
    private readonly HashSet<Work> _active = new();
    private readonly ConcurrentQueue<(Work Work, string? Text, bool Unavailable)> _completed = new();
    private CancellationTokenSource _lifetime = new();
    private string? _key;
    private ulong _nextId;
    private TimeSpan _resumeAt;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        _config.OnValueChanged(Wh40kChatTranslationCVars.Enabled, OnEnabledChanged);
        _config.OnValueChanged(Wh40kChatTranslationCVars.GeminiApiKey, OnApiKeyChanged, invokeImmediately: true);
    }

    public override void Shutdown()
    {
        _config.UnsubValueChanged(Wh40kChatTranslationCVars.Enabled, OnEnabledChanged);
        _config.UnsubValueChanged(Wh40kChatTranslationCVars.GeminiApiKey, OnApiKeyChanged);
        Reset(restart: false);
    }

    internal MsgChatMessage CreatePacket(ChatMessage message, INetChannel recipient, EntityUid source, NetUserId? author = null)
    {
        var packet = new MsgChatMessage { Message = message };
        if (!TryGetSenderCulture(message, source, author, out var senderCulture) ||
            !TryGetTargetCulture(senderCulture, recipient, out var targetCulture))
            return packet;

        packet.TranslationId = QueueDelivery(message.Message, targetCulture, [recipient]);
        return packet;
    }

    internal bool TrySendGroup(ChatMessage message, List<INetChannel> recipients, EntityUid source, NetUserId? author)
    {
        if (!TryGetSenderCulture(message, source, author, out var senderCulture))
            return false;

        List<INetChannel> translated = new();
        List<INetChannel> originals = new();
        string? targetCulture = null;
        foreach (var recipient in recipients)
        {
            if (TryGetTargetCulture(senderCulture, recipient, out var target))
            {
                targetCulture = target;
                translated.Add(recipient);
            }
            else
            {
                originals.Add(recipient);
            }
        }

        if (targetCulture == null)
            return false;

        var id = QueueDelivery(message.Message, targetCulture, translated);
        if (id == 0)
            return false;

        if (originals.Count > 0)
            _net.ServerSendToMany(new MsgChatMessage { Message = message }, originals);

        _net.ServerSendToMany(new MsgChatMessage { Message = message, TranslationId = id }, translated);
        return true;
    }

    private bool TryGetSenderCulture(ChatMessage message, EntityUid source, NetUserId? author, out string culture)
    {
        culture = string.Empty;
        if (_key == null || _timing.RealTime < _resumeAt || !_config.GetCVar(Wh40kChatTranslationCVars.Enabled) ||
            message.HideChat || message.Channel is not (ChatChannel.Local or ChatChannel.Whisper or ChatChannel.Radio
                or ChatChannel.LOOC or ChatChannel.OOC or ChatChannel.Dead or ChatChannel.Emotes or ChatChannel.CollectiveMind) ||
            message.Message.Length is 0 or > 1200 || !message.Message.Any(char.IsLetter))
            return false;

        if (author == null && TryComp(source, out ActorComponent? actor))
            author = actor.PlayerSession.UserId;

        if (!_players.TryGetSessionById(author, out var sender) || !_cultures.IsCultureReady(sender))
            return false;

        culture = _cultures.GetCulture(sender);
        return true;
    }

    internal static bool ShouldTranslate(string senderCulture, string recipientCulture)
        => !string.Equals(senderCulture, recipientCulture, StringComparison.Ordinal);

    private bool TryGetTargetCulture(string senderCulture, INetChannel channel, out string culture)
    {
        culture = string.Empty;
        if (!_players.TryGetSessionByChannel(channel, out var session) || !_cultures.IsCultureReady(session))
            return false;

        culture = _cultures.GetCulture(session);
        return ShouldTranslate(senderCulture, culture);
    }

    private ulong QueueDelivery(string text, string culture, List<INetChannel> recipients)
    {
        var key = (text, culture);
        if (!_pending.TryGetValue(key, out var work))
        {
            if (_pending.Count >= MaxPending || recipients.Count > MaxDeliveriesPerWork)
                return 0;

            work = new Work(key, _timing.RealTime);
            _pending.Add(key, work);
            _queue.Enqueue(work);
        }

        if (work.Deliveries.Count + recipients.Count > MaxDeliveriesPerWork)
            return 0;

        var id = ++_nextId;
        foreach (var channel in recipients)
            work.Deliveries.Add((channel, id));

        return id;
    }

    public override void Update(float frameTime)
    {
        if (_key == null)
            return;

        var now = _timing.RealTime;
        while (_completed.TryDequeue(out var completed))
        {
            var (work, text, unavailable) = completed;
            if (!_active.Remove(work))
                continue;

            _pending.Remove(work.Key);
            if (unavailable)
            {
                if (now >= _resumeAt)
                    Log.Warning("Gemini Live translation is unavailable. Chat originals continue during the retry delay.");

                _resumeAt = now + ProviderCooldown;
                foreach (var queued in _queue)
                    _pending.Remove(queued.Key);
                _queue.Clear();
                continue;
            }

            if (text == null)
                continue;

            foreach (var (channel, id) in work.Deliveries)
            {
                if (!channel.IsConnected || !_players.TryGetSessionByChannel(channel, out var session) ||
                    _cultures.GetCulture(session) != work.Key.Culture)
                    continue;

                _net.ServerSendMessage(new MsgChatTranslation { Id = id, Culture = work.Key.Culture, Text = text }, channel);
            }
        }

        while (now >= _resumeAt && _active.Count < MaxActive && _queue.TryDequeue(out var work))
        {
            if (now - work.QueuedAt > MaxQueueAge)
            {
                _pending.Remove(work.Key);
                continue;
            }

            _active.Add(work);
            _ = RunWorkAsync(work, _key, _lifetime.Token);
        }
    }

    private async Task RunWorkAsync(Work work, string key, CancellationToken cancellation)
    {
        var (text, unavailable) = await GeminiLiveTranslator.TranslateAsync(key, work.Key.Text, work.Key.Culture, cancellation)
            .ConfigureAwait(false);
        if (!cancellation.IsCancellationRequested)
            _completed.Enqueue((work, text, unavailable));
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (!enabled)
            Reset();
    }

    private void OnApiKeyChanged(string value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (_key == key)
            return;

        _key = key;
        _resumeAt = TimeSpan.Zero;
        Reset();
    }

    private void Reset(bool restart = true)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (restart)
            _lifetime = new CancellationTokenSource();

        _pending.Clear();
        _queue.Clear();
        _active.Clear();
        _completed.Clear();
    }

    private sealed class Work((string Text, string Culture) key, TimeSpan queuedAt)
    {
        public readonly (string Text, string Culture) Key = key;
        public readonly TimeSpan QueuedAt = queuedAt;
        public readonly List<(INetChannel Channel, ulong Id)> Deliveries = new();
    }
}
