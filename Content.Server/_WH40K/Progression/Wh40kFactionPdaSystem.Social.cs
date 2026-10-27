using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Administration.Mute;
using Content.Server._WH40K.Localizations;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Progression;
using Content.Shared.Database;
using Content.Shared.Chat;
using Content.Shared._Mono.Company;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players.RateLimiting;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Progression;

public sealed partial class Wh40kFactionPdaSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IAdminLogManager _adminLogs = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WH40KMuteSystem _mutes = default!;
    [Dependency] private WH40KPlayerCultureManager _culture = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    private readonly Dictionary<string, List<FactionTransmission>> _transmissions = new();
    private readonly Dictionary<(NetUserId, Wh40kFactionUiAction), TimeSpan> _socialCooldowns = new();

    private void InitializeSocial()
    {
        _factions.FactionSocialChanged += OnSocialChanged;
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _transmissions.Clear();
            _socialCooldowns.Clear();
        });
    }

    private void ShutdownSocial() => _factions.FactionSocialChanged -= OnSocialChanged;

    private bool CanTransmit(ICommonSession session) => session.Status == SessionStatus.InGame &&
        session.AttachedEntity is { } body && Exists(body) && _mobState.IsAlive(body);

    private int Cooldown(ICommonSession session, Wh40kFactionUiAction action) =>
        (int) Math.Max(0, Math.Ceiling((_socialCooldowns.GetValueOrDefault((session.UserId, action)) - _timing.RealTime).TotalSeconds));

    private FactionSocialSnapshot GetSocialSnapshot(ICommonSession session, string faction) =>
        new(_factions.GetFactionAnnouncements(faction), CanTransmit(session) ? _transmissions.GetValueOrDefault(faction)?.ToArray() ?? [] : [],
            CanTransmit(session), Cooldown(session, Wh40kFactionUiAction.SendMessage), Cooldown(session, Wh40kFactionUiAction.SendDistress));

    private async Task<Wh40kFactionUiOperationStatus> HandleSocialAsync(Wh40kFactionUiMessage message, ICommonSession session, string faction)
    {
        var request = message.Social;
        if (message.Action != Wh40kFactionUiAction.RemoveAnnouncement && _mutes.IsChatMuted(session, out _))
            return Wh40kFactionUiOperationStatus.NotAuthorized;
        if (message.Action is Wh40kFactionUiAction.SendMessage or Wh40kFactionUiAction.SendDistress && !CanTransmit(session))
            return Wh40kFactionUiOperationStatus.TransmissionUnavailable;
        if (Cooldown(session, message.Action) > 0)
            return Wh40kFactionUiOperationStatus.SocialCooldown;
        if (message.Action == Wh40kFactionUiAction.SetRelation)
            return await _factions.SetFactionRelationAsync(session.UserId, faction, request, session.Name);
        if (message.Action == Wh40kFactionUiAction.RemoveAnnouncement)
        {
            var result = await _factions.RemoveFactionAnnouncementAsync(session.UserId, faction, request?.Id ?? Guid.Empty);
            if (result == Wh40kFactionUiOperationStatus.AnnouncementRemoved)
                _socialCooldowns[(session.UserId, message.Action)] = _timing.RealTime + TimeSpan.FromSeconds(2);
            return result;
        }
        if (message.Action == Wh40kFactionUiAction.PublishAnnouncement)
        {
            var result = await _factions.PublishFactionAnnouncementAsync(session.UserId, faction, request, session.Name);
            if (result == Wh40kFactionUiOperationStatus.AnnouncementPublished)
            {
                _socialCooldowns[(session.UserId, message.Action)] = _timing.RealTime + TimeSpan.FromSeconds(15);
                NotifyFaction(faction, "wh40k-faction-comms-announcement-notify", session.Name, request!.Title.Trim());
                _adminLogs.Add(LogType.Chat, LogImpact.Low, $"Faction announcement [{faction}] {session.UserId}: {request.Title}: {request.Text}");
            }
            return result;
        }
        var distress = message.Action == Wh40kFactionUiAction.SendDistress;
        if (request == null || request.Id == Guid.Empty || !FactionSocialRules.ValidText(request.Text, FactionSocialRules.MaxMessageLength))
            return Wh40kFactionUiOperationStatus.InvalidSocialText;
        if (_chat.HandleRateLimit(session) == RateLimitStatus.Blocked || _chat.HandleRepeatedRateLimit(session, request.Text) == RateLimitStatus.Blocked)
            return Wh40kFactionUiOperationStatus.SocialCooldown;
        if (!_transmissions.TryGetValue(faction, out var history))
            _transmissions[faction] = history = new List<FactionTransmission>();
        if (history.Any(p => p.Id == request.Id))
            return Wh40kFactionUiOperationStatus.MessageSent;
        var body = session.AttachedEntity!.Value;
        var author = MetaData(body).EntityName;
        if (string.IsNullOrWhiteSpace(author))
            author = session.Name;
        var location = string.Empty;
        if (distress)
        {
            var xform = Transform(body);
            var place = xform.GridUid ?? xform.MapUid;
            var position = place is { } origin
                ? _transform.ToCoordinates(origin, _transform.GetMapCoordinates(body)).Position
                : _transform.GetWorldPosition(xform);
            location = $"{(place != null ? MetaData(place.Value).EntityName : "?")} · X {(int) position.X}, Y {(int) position.Y}";
        }
        var transmission = new FactionTransmission(request.Id, author, request.Text.Trim(), DateTime.UtcNow, distress, location);
        history.Add(transmission);
        if (history.Count > FactionSocialRules.MessageLimit)
            history.RemoveAt(0);
        _socialCooldowns[(session.UserId, message.Action)] = _timing.RealTime + TimeSpan.FromSeconds(distress ? 180 : 3);
        _adminLogs.Add(LogType.Chat, LogImpact.Low, $"Faction {(distress ? "SOS" : "message")} [{faction}] {session.UserId}: {transmission.Text} {location}");
        if (distress)
            NotifyFaction(faction, "wh40k-faction-comms-distress-notify", author, $"{transmission.Text} · {location}");
        else
            NotifyFaction(faction, "wh40k-faction-comms-message-notify", author, transmission.Text);
        OnSocialChanged(faction);
        return distress ? Wh40kFactionUiOperationStatus.DistressSent : Wh40kFactionUiOperationStatus.MessageSent;
    }

    private void NotifyFaction(string faction, string key, string author, string text)
    {
        if (!_prototypes.TryIndex<CompanyPrototype>(faction, out var prototype))
            return;
        foreach (var recipient in _players.Sessions.Where(s => CanTransmit(s) && GetFaction(s)?.Id == faction))
        {
            var message = _culture.GetPlayerString(recipient, key, ("author", author), ("text", text));
            var wrapped = _culture.GetPlayerString(recipient, "chat-manager-server-wrap-message", ("message", FormattedMessage.EscapeText(message)));
            _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, EntityUid.Invalid, false, recipient.Channel, prototype.Color);
        }
    }

    private void OnSocialChanged(string? faction)
    {
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.Disconnected && (faction == null || GetFaction(session)?.Id == faction))
                SendUpdatedSnapshot(session);
        }
    }
}
