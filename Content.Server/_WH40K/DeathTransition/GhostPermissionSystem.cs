using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared._WH40K.DeathTransition;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.DeathTransition;

/// <summary>
/// Enforces the one-character policy around observer ghosts. The server owns every decision here;
/// the client only receives a boolean for its lobby button and a visual death transition.
/// </summary>
public sealed class GhostPermissionSystem : EntitySystem
{
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromSeconds(5);

    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MindSystem _minds = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<NetUserId, GhostPermissionData> _permissions = new();
    private readonly HashSet<NetUserId> _loadedPermissions = new();
    private readonly HashSet<NetUserId> _loadingPermissions = new();
    private readonly Dictionary<NetUserId, TimeSpan> _reservedPermissionUses = new();
    private readonly Dictionary<NetUserId, TimeSpan> _staffObserverAuthorizations = new();
    private readonly Dictionary<NetUserId, PendingDeathLobbyTransition> _pendingTransitions = new();
    private readonly HashSet<NetUserId> _deathScreenEligible = new();
    private int _nextTransitionId;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeNetworkEvent<DeathSurrenderEvent>(OnDeathSurrender);
        _admin.OnPermsChanged += OnAdminPermsChanged;
    }

    public override void Shutdown()
    {
        _admin.OnPermsChanged -= OnAdminPermsChanged;
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        foreach (var userId in _reservedPermissionUses
                     .Where(pair => now - pair.Value >= AuthorizationLifetime)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _reservedPermissionUses.Remove(userId);
        }

        foreach (var userId in _staffObserverAuthorizations
                     .Where(pair => now - pair.Value >= AuthorizationLifetime)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _staffObserverAuthorizations.Remove(userId);
        }

        foreach (var (userId, transition) in _pendingTransitions.ToArray())
        {
            if (now < transition.ReturnAt)
                continue;

            _pendingTransitions.Remove(userId);

            if (_players.TryGetSessionById(userId, out var session))
            {
                _ticker.ReturnPlayerToLobby(session);
                continue;
            }

            if (_minds.TryGetMind(userId, out var mindId, out var mind))
                _minds.WipeMind(mindId, mind);
        }
    }

    /// <summary>
    /// Staff access is intentionally limited to active administrators and moderators.
    /// A deadmined account is treated exactly like an ordinary player.
    /// </summary>
    public bool IsGhostStaff(ICommonSession session)
    {
        return _admin.IsAdmin(session)
               && (_admin.HasAdminFlag(session, AdminFlags.Admin)
                   || _admin.HasAdminFlag(session, AdminFlags.Moderator));
    }

    public bool CanObserve(ICommonSession session)
    {
        return IsGhostStaff(session) || HasActivePermission(session.UserId);
    }

    public bool CanObserve(NetUserId userId)
    {
        return _players.TryGetSessionById(userId, out var session) && CanObserve(session);
    }

    /// <summary>
    /// Reserves one existing permission use for a normal observer spawn. It is consumed only after
    /// a mind is actually attached to a ghost.
    /// </summary>
    public bool TryReservePermissionUse(ICommonSession session)
    {
        if (IsGhostStaff(session))
            return true;

        if (_reservedPermissionUses.ContainsKey(session.UserId))
            return true;

        if (!HasActivePermission(session.UserId))
            return false;

        _reservedPermissionUses[session.UserId] = _timing.CurTime;
        return true;
    }

    /// <summary>
    /// Allows an active staff member to deadmin and receive exactly one ordinary observer body.
    /// It grants no further ghosting rights after that observer leaves the world.
    /// </summary>
    public bool AuthorizeStaffObserverAsPlayer(ICommonSession session)
    {
        if (!IsGhostStaff(session))
            return false;

        _staffObserverAuthorizations[session.UserId] = _timing.CurTime;
        return true;
    }

    public async System.Threading.Tasks.Task<GhostPermissionData?> GetStoredPermissionAsync(NetUserId userId)
    {
        var permission = await _db.GetGhostPermissionAsync(userId);
        return IsPermissionValid(permission) ? permission : null;
    }

    public async System.Threading.Tasks.Task SetPermissionAsync(NetUserId userId, GhostPermissionData permission)
    {
        if (!IsPermissionValid(permission))
            throw new ArgumentException("A ghost permission must have positive uses and an unexpired duration.", nameof(permission));

        _permissions[userId] = permission;
        _loadedPermissions.Add(userId);
        await _db.SetGhostPermissionAsync(userId, permission);
        SendPermissionStatus(userId);
    }

    public async System.Threading.Tasks.Task RemovePermissionAsync(NetUserId userId)
    {
        _permissions.Remove(userId);
        _loadedPermissions.Add(userId);
        _reservedPermissionUses.Remove(userId);
        await _db.RemoveGhostPermissionAsync(userId);
        SendPermissionStatus(userId);
    }

    private async void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        _pendingTransitions.Remove(ev.PlayerSession.UserId);
        _deathScreenEligible.Remove(ev.PlayerSession.UserId);
        await LoadPermissionAsync(ev.PlayerSession);
    }

    private void OnAdminPermsChanged(AdminPermsChangedEventArgs args)
    {
        SendPermissionStatus(args.Player.UserId);
    }

    private async System.Threading.Tasks.Task LoadPermissionAsync(ICommonSession session)
    {
        if (_loadingPermissions.Contains(session.UserId))
            return;

        _loadingPermissions.Add(session.UserId);

        try
        {
            var permission = await _db.GetGhostPermissionAsync(session.UserId);
            if (IsPermissionValid(permission))
            {
                _permissions[session.UserId] = permission!;
            }
            else
            {
                _permissions.Remove(session.UserId);

                if (permission != null)
                    await _db.RemoveGhostPermissionAsync(session.UserId);
            }

            _loadedPermissions.Add(session.UserId);
            SendPermissionStatus(session.UserId);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to load ghost permission for {session.UserId}: {exception}");
        }
        finally
        {
            _loadingPermissions.Remove(session.UserId);
        }
    }

    private void OnGhostAttempt(GhostAttemptHandleEvent ev)
    {
        var userId = ev.Mind.UserId;
        if (userId == null || !_players.TryGetSessionById(userId.Value, out var session))
            return;

        if (IsGhostStaff(session) || TryReservePermissionUse(session))
            return;

        // Commands such as ghost, suicide and cryosleep all reach this point before
        // spawning an observer. They are deliberate exits, not a physical death scene.
        ReturnPlayerToLobbyImmediately(session);
        ev.Handled = true;
        ev.Result = true;
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (!TryComp<ActorComponent>(ev.Target, out var actor))
            return;

        var session = actor.PlayerSession;
        if (ev.OldMobState == MobState.Dead && ev.NewMobState != MobState.Dead)
        {
            _deathScreenEligible.Remove(session.UserId);
            CancelDeathTransition(session);
        }

        if (ev.NewMobState != MobState.Dead
            || session.AttachedEntity != ev.Target
            || IsGhostStaff(session))
            return;

        _deathScreenEligible.Add(session.UserId);

        var message = Loc.GetString("heretek-death-surrender-hint");
        var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message", ("message", message));
        _chat.ChatMessageToOne(
            ChatChannel.Server,
            message,
            wrappedMessage,
            default,
            false,
            session.Channel,
            colorOverride: Color.FromHex("#800020"));
    }

    private void OnDeathSurrender(DeathSurrenderEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        // Active staff retain their normal observer workflow. Other players may explicitly
        // surrender even if they have a separate permission to become an observer.
        if (IsGhostStaff(session)
            || session.AttachedEntity is not { } entity
            || !_mobState.IsDead(entity)
            || !_minds.TryGetMind(entity, out _, out var mind)
            || mind.UserId != session.UserId)
        {
            return;
        }

        mind.TimeOfDeath ??= _timing.CurTime;
        BeginDeathTransition(session);
    }

    /// <summary>
    /// Invoked by the existing ghost-system subscription after a mind is attached to a ghost.
    /// A directed <see cref="MindAddedMessage"/> may only have one handler for a component type,
    /// so this deliberately is not an event subscription of its own.
    /// </summary>
    public void HandleGhostMindAdded(MindAddedMessage args)
    {
        var userId = args.Mind.Comp.UserId;
        if (userId == null || !_players.TryGetSessionById(userId.Value, out var session))
            return;

        if (IsGhostStaff(session))
            return;

        if (_staffObserverAuthorizations.Remove(userId.Value))
            return;

        if (_reservedPermissionUses.Remove(userId.Value))
        {
            TryConsumePermissionUse(userId.Value);
            return;
        }

        if (TryConsumePermissionUse(userId.Value))
            return;

        if (_deathScreenEligible.Remove(userId.Value))
        {
            BeginDeathTransition(session);
            _minds.WipeMind(args.Mind.Owner, args.Mind.Comp);
            return;
        }

        ReturnPlayerToLobbyImmediately(session);
    }

    private bool HasActivePermission(NetUserId userId)
    {
        if (!_loadedPermissions.Contains(userId)
            || !_permissions.TryGetValue(userId, out var permission))
        {
            return false;
        }

        if (IsPermissionValid(permission))
            return true;

        _permissions.Remove(userId);
        _reservedPermissionUses.Remove(userId);
        _ = _db.RemoveGhostPermissionAsync(userId);
        SendPermissionStatus(userId);
        return false;
    }

    private bool TryConsumePermissionUse(NetUserId userId)
    {
        if (!HasActivePermission(userId)
            || !_permissions.TryGetValue(userId, out var permission))
        {
            return false;
        }

        var remainingUses = permission.RemainingUses - 1;
        if (remainingUses <= 0)
        {
            _permissions.Remove(userId);
            _ = _db.RemoveGhostPermissionAsync(userId);
        }
        else
        {
            var updated = permission with { RemainingUses = remainingUses };
            _permissions[userId] = updated;
            _ = _db.SetGhostPermissionAsync(userId, updated);
        }

        SendPermissionStatus(userId);
        return true;
    }

    private void BeginDeathTransition(ICommonSession session)
    {
        if (_pendingTransitions.ContainsKey(session.UserId))
            return;

        var transition = new PendingDeathLobbyTransition(
            ++_nextTransitionId,
            _timing.CurTime + DeathTransitionTiming.TotalDuration);
        _pendingTransitions.Add(session.UserId, transition);
        RaiseNetworkEvent(new DeathTransitionStartEvent(transition.Id, DeathTransitionTiming.TotalDuration), session.Channel);
    }

    private void CancelDeathTransition(ICommonSession session)
    {
        if (!_pendingTransitions.Remove(session.UserId, out var transition))
            return;

        RaiseNetworkEvent(new DeathTransitionCancelledEvent(transition.Id), session.Channel);
    }

    private void ReturnPlayerToLobbyImmediately(ICommonSession session)
    {
        _pendingTransitions.Remove(session.UserId);
        _deathScreenEligible.Remove(session.UserId);
        _ticker.ReturnPlayerToLobby(session);
    }

    private void SendPermissionStatus(NetUserId userId)
    {
        if (!_players.TryGetSessionById(userId, out var session))
            return;

        // Staff rights are transported by MsgUpdateAdminStatus and evaluated separately on the client.
        // This event must only represent an ordinary observer permission; otherwise a deadmined
        // administrator keeps a stale client-side permission and sees an enabled button.
        RaiseNetworkEvent(new GhostPermissionStatusEvent(HasActivePermission(userId)), session.Channel);
    }

    private static bool IsPermissionValid(GhostPermissionData? permission)
    {
        return permission is { RemainingUses: > 0 }
               && (permission.ExpiresAt == null || permission.ExpiresAt > DateTime.UtcNow);
    }

    private readonly record struct PendingDeathLobbyTransition(int Id, TimeSpan ReturnAt);
}
