using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Cleanup;

public sealed partial class DisconnectedPlayerCleanupSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    private readonly Dictionary<NetUserId, PendingDisconnect> _disconnects = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _disconnects.Clear();
            _nextCheck = TimeSpan.Zero;
        });
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (!_configuration.GetCVar(CCVars.Wh40kDisconnectedPlayerCleanupEnabled) || now < _nextCheck)
            return;

        _nextCheck = now + TimeSpan.FromSeconds(1);
        var delay = TimeSpan.FromSeconds(Math.Max(
            0, _configuration.GetCVar(CCVars.Wh40kDisconnectedPlayerCleanupDelaySeconds)));

        foreach (var (userId, pending) in _disconnects.ToArray())
        {
            if (now - pending.DisconnectedAt < delay)
                continue;

            _disconnects.Remove(userId);
            if (IsConnected(userId) || TerminatingOrDeleted(pending.Body) ||
                !_minds.TryGetMind(userId, out _, out var mind) || mind.OwnedEntity != pending.Body)
                continue;

            var ev = new DisconnectedPlayerCleanupEvent(userId);
            RaiseLocalEvent(pending.Body, ref ev);
            if (!ev.Handled)
                DeleteBody(pending.Body, userId);
        }
    }

    /// <summary>
    /// Tracks an offline player's current body, including one bound after the disconnect event.
    /// Repeated notifications must not restart the grace period.
    /// </summary>
    public void TrackDisconnect(NetUserId userId)
    {
        if (IsConnected(userId) || !_minds.TryGetMind(userId, out _, out var mind) ||
            mind.OwnedEntity is not { } body || TerminatingOrDeleted(body) ||
            !HasComp<MobStateComponent>(body) || HasComp<GhostComponent>(body))
            return;

        if (!_disconnects.TryGetValue(userId, out var pending) || pending.Body != body)
            _disconnects[userId] = new PendingDisconnect(body, _timing.CurTime);
    }

    /// <summary>
    /// Ends the mind before deleting the body to avoid leaving an offline observer behind.
    /// Persistent inventory calls this only after its loss has been committed.
    /// </summary>
    public void DeleteBody(EntityUid body, NetUserId userId)
    {
        if (TerminatingOrDeleted(body))
            return;

        if (_minds.TryGetMind(body, out var mindId, out var mind) && mind.UserId == userId)
        {
            _minds.UnVisit(mindId, mind);
            _minds.WipeMind(mindId, mind);
        }

        _adminLog.Add(LogType.Mind, LogImpact.High,
            $"Disconnected player cleanup removed {ToPrettyString(body)} for {userId}.");
        QueueDel(body);
    }

    private bool IsConnected(NetUserId userId)
    {
        return _players.TryGetSessionById(userId, out var session) && session.Status != SessionStatus.Disconnected;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            TrackDisconnect(args.Session.UserId);
        else
            _disconnects.Remove(args.Session.UserId);
    }

    private readonly record struct PendingDisconnect(EntityUid Body, TimeSpan DisconnectedAt);
}
