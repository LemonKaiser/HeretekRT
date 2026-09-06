using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Chat.V2.Repository;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server._WH40K.Administration;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Emoting;
using Content.Shared.GameTicking;
using Content.Shared.Speech;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Administration.Mute;

/// <summary>
/// Authoritative persistent administrative mutes. Chat and ahelp scopes are loaded before the player enters the
/// lobby and every client-facing snapshot is informational only; all message blocking happens on the server.
/// </summary>
public sealed partial class WH40KMuteSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogs = default!;
    [Dependency] private IAdminHierarchyManager _adminHierarchy = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private UserDbDataManager _userDb = default!;

    private static readonly WH40KMuteSnapshot EmptySnapshot =
        new(WH40KMuteType.None, null, null);

    private readonly Dictionary<NetUserId, WH40KMuteSnapshot> _snapshots = new();
    private readonly HashSet<NetUserId> _refreshQueued = new();
    private readonly object _operationLockSync = new();
    private readonly Dictionary<NetUserId, MuteOperationLockEntry> _operationLocks = new();
    private TimeSpan _nextExpirySweep;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpeakAttemptEvent>(OnSpeakAttempt);
        SubscribeLocalEvent<EmoteAttemptEvent>(OnEmoteAttempt);
        SubscribeLocalEvent<InGameOocMessageAttemptEvent>(OnInGameOocAttempt);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeNetworkEvent<WH40KMuteRequestStateEvent>(OnRequestState);

        _userDb.AddOnLoadPlayer(LoadPlayerDataAsync);
        _userDb.AddOnFinishLoad(FinishPlayerLoad);
        _userDb.AddOnPlayerDisconnect(OnPlayerDisconnected);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextExpirySweep)
            return;

        _nextExpirySweep = _timing.CurTime + TimeSpan.FromSeconds(1);
        var now = DateTime.UtcNow;
        foreach (var (userId, snapshot) in _snapshots.ToArray())
        {
            if (SnapshotMayNeedRefresh(snapshot, now))
                QueueRefresh(userId);
        }
    }

    public bool IsChatMuted(ICommonSession session, out WH40KActiveMuteInfo? info)
    {
        return IsMuted(session, WH40KMuteType.Chat, out info);
    }

    public bool IsAHelpMuted(ICommonSession session, out WH40KActiveMuteInfo? info)
    {
        return IsMuted(session, WH40KMuteType.AHelp, out info);
    }

    public string GetApplyFailureMessage(WH40KMuteApplyResult result)
    {
        return result switch
        {
            WH40KMuteApplyResult.InvalidScope => Loc.GetString("wh40k-mute-command-invalid-scope-mask"),
            WH40KMuteApplyResult.InvalidReason => Loc.GetString("wh40k-mute-panel-no-reason"),
            WH40KMuteApplyResult.InvalidDuration => Loc.GetString("wh40k-mute-command-invalid-duration"),
            WH40KMuteApplyResult.TargetHostProtected => Loc.GetString("wh40k-mute-target-host-protected"),
            _ => string.Empty,
        };
    }

    public Task<WH40KMuteApplyResult> ApplyMuteAsync(
        NetUserId targetUserId,
        string targetName,
        WH40KMuteType typeMask,
        string reason,
        TimeSpan? duration,
        NetUserId? adminUserId,
        bool eraseMessages)
    {
        return WithTargetLockAsync(targetUserId, async () =>
        {
            if (!WH40KMutePolicy.IsValidScopeMask(typeMask))
                return WH40KMuteApplyResult.InvalidScope;

            if (!WH40KMutePolicy.TryNormalizeReason(reason, out var sanitizedReason))
                return WH40KMuteApplyResult.InvalidReason;

            if (!WH40KMutePolicy.IsValidTemporaryDuration(duration))
                return WH40KMuteApplyResult.InvalidDuration;

            if (await HasHostBypassAsync(targetUserId))
                return WH40KMuteApplyResult.TargetHostProtected;

            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? expiresAt = duration == null ? null : now + duration.Value;
            var replacement = await _db.ReplaceMutesAsync(
                targetUserId,
                WH40KMutePolicy.EnumerateScopes(typeMask),
                sanitizedReason,
                adminUserId,
                now,
                expiresAt);

            if (eraseMessages)
                ErasePlayerMessages(targetUserId);

            _adminLogs.Add(
                LogType.Action,
                LogImpact.Medium,
                $"{adminUserId} muted {targetName} ({targetUserId}) for {FormatScopes(typeMask)}; " +
                $"replaced {replacement.SupersededCount} active mute(s). Reason: {sanitizedReason}");

            await RefreshSnapshotCoreAsync(targetUserId);
            NotifyMutedPlayer(targetUserId);
            return WH40KMuteApplyResult.Applied;
        });
    }

    public async Task<bool> CanRemoveMuteAsync(
        NetUserId targetUserId,
        WH40KMuteType typeMask,
        ICommonSession? actor,
        Action<string>? notify = null,
        CancellationToken cancel = default)
    {
        if (actor == null)
            return true;

        typeMask = WH40KMutePolicy.NormalizeRemovalScopeMask(typeMask);
        if (!WH40KMutePolicy.IsValidScopeMask(typeMask))
            return false;

        var actorHierarchy = _adminHierarchy.GetAdminHierarchy(actor, includeDeAdmin: true);
        var activeMutes = await _db.GetActiveMutesAsync(targetUserId);
        foreach (var activeMute in activeMutes.Where(m => (m.Type & typeMask) != 0))
        {
            if (activeMute.MutingAdmin is not { } mutingAdminId || mutingAdminId == actor.UserId)
                continue;

            if ((await _adminHierarchy.CanManageAdminAsync(actor, mutingAdminId, cancel)).Allowed)
                continue;

            if (!WH40KStaffProtection.CanOverrideStaffAction(
                    actorHierarchy,
                    await TryGetHierarchyAsync(mutingAdminId, cancel)))
            {
                notify?.Invoke(Loc.GetString("wh40k-mute-unmute-denied-protected"));
                return false;
            }
        }

        return true;
    }

    public Task<WH40KMuteRemovalResult> RemoveMuteAsync(
        NetUserId targetUserId,
        WH40KMuteType typeMask,
        ICommonSession? actor,
        Action<string>? notify = null)
    {
        return WithTargetLockAsync(targetUserId, async () =>
        {
            typeMask = WH40KMutePolicy.NormalizeRemovalScopeMask(typeMask);
            if (!WH40KMutePolicy.IsValidScopeMask(typeMask) ||
                !await CanRemoveMuteAsync(targetUserId, typeMask, actor, notify))
            {
                return new WH40KMuteRemovalResult(false, 0);
            }

            var adminUserId = actor?.UserId;
            var removed = await _db.RemoveActiveMutesAsync(
                targetUserId,
                WH40KMutePolicy.EnumerateScopes(typeMask),
                adminUserId,
                DateTimeOffset.UtcNow);

            if (removed > 0)
            {
                _adminLogs.Add(
                    LogType.Action,
                    LogImpact.Medium,
                    $"{adminUserId} removed {removed} mute(s) from {targetUserId} for {FormatScopes(typeMask)}");
            }

            await RefreshSnapshotCoreAsync(targetUserId);
            return new WH40KMuteRemovalResult(true, removed);
        });
    }

    private bool IsMuted(ICommonSession session, WH40KMuteType type, out WH40KActiveMuteInfo? info)
    {
        if (ShouldIgnoreMute(session))
        {
            info = null;
            return false;
        }

        if (!_snapshots.TryGetValue(session.UserId, out var snapshot))
        {
            if (!_userDb.IsLoadComplete(session))
            {
                info = null;
                return true;
            }

            snapshot = EmptySnapshot;
            _snapshots[session.UserId] = snapshot;
        }

        if (SnapshotMayNeedRefresh(snapshot, DateTime.UtcNow))
        {
            QueueRefresh(session.UserId);
            snapshot = PruneExpired(snapshot, DateTime.UtcNow);
            _snapshots[session.UserId] = snapshot;
        }

        info = type switch
        {
            WH40KMuteType.Chat => snapshot.ChatMute,
            WH40KMuteType.AHelp => snapshot.AHelpMute,
            _ => null
        };
        return info != null;
    }

    private async Task LoadPlayerDataAsync(ICommonSession session, CancellationToken cancel)
    {
        await WithTargetLockAsync(session.UserId, async () =>
        {
            if (ShouldIgnoreMute(session))
            {
                _snapshots[session.UserId] = EmptySnapshot;
                return true;
            }

            _snapshots[session.UserId] = await LoadSnapshotAsync(session.UserId);
            cancel.ThrowIfCancellationRequested();
            return true;
        });
    }

    private void FinishPlayerLoad(ICommonSession session)
    {
        if (_players.TryGetSessionById(session.UserId, out var current))
            PushSnapshot(current);
    }

    private void OnPlayerDisconnected(ICommonSession session)
    {
        _snapshots.Remove(session.UserId);
        _refreshQueued.Remove(session.UserId);
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        if (_userDb.IsLoadComplete(ev.PlayerSession))
            PushSnapshot(ev.PlayerSession);
    }

    private void OnRequestState(WH40KMuteRequestStateEvent ev, EntitySessionEventArgs args)
    {
        PushSnapshot(args.SenderSession);
    }

    private void OnSpeakAttempt(SpeakAttemptEvent args)
    {
        if (_players.TryGetSessionByEntity(args.Uid, out var session) && IsChatMuted(session, out _))
            args.Cancel();
    }

    private void OnEmoteAttempt(EmoteAttemptEvent args)
    {
        if (_players.TryGetSessionByEntity(args.Uid, out var session) && IsChatMuted(session, out _))
            args.Cancel();
    }

    private void OnInGameOocAttempt(ref InGameOocMessageAttemptEvent args)
    {
        if (IsChatMuted(args.Session, out _))
            args.Cancelled = true;
    }

    private void ErasePlayerMessages(NetUserId userId)
    {
        _chat.DeleteMessagesBy(userId);
        EntityManager.System<ChatRepositorySystem>().NukeForUserId(userId, out _);
    }

    private void NotifyMutedPlayer(NetUserId userId)
    {
        if (_players.TryGetSessionById(userId, out var session))
            _chat.DispatchServerMessage(session, Loc.GetString("wh40k-mute-player-notification"));
    }

    private async Task<WH40KMuteSnapshot> LoadSnapshotAsync(NetUserId userId)
    {
        if (await HasHostBypassAsync(userId))
            return EmptySnapshot;

        var activeMutes = await _db.GetActiveMutesAsync(userId);
        var chatMute = activeMutes.Where(m => m.Type == WH40KMuteType.Chat)
            .OrderByDescending(m => m.MuteTime).FirstOrDefault();
        var ahelpMute = activeMutes.Where(m => m.Type == WH40KMuteType.AHelp)
            .OrderByDescending(m => m.MuteTime).FirstOrDefault();

        return new WH40KMuteSnapshot(
            (chatMute == null ? WH40KMuteType.None : WH40KMuteType.Chat) |
            (ahelpMute == null ? WH40KMuteType.None : WH40KMuteType.AHelp),
            ToActiveInfo(chatMute),
            ToActiveInfo(ahelpMute));
    }

    private Task<WH40KMuteSnapshot> RefreshSnapshotIfOnlineAsync(NetUserId userId)
    {
        return WithTargetLockAsync(userId, () => RefreshSnapshotCoreAsync(userId));
    }

    private async Task<WH40KMuteSnapshot> RefreshSnapshotCoreAsync(NetUserId userId)
    {
        try
        {
            if (!_players.TryGetSessionById(userId, out var session))
            {
                _snapshots.Remove(userId);
                return EmptySnapshot;
            }

            var snapshot = await LoadSnapshotAsync(userId);

            // The player may disconnect while the database query is in flight.
            if (!_players.TryGetSessionById(userId, out session))
            {
                _snapshots.Remove(userId);
                return EmptySnapshot;
            }

            _snapshots[userId] = snapshot;
            PushSnapshot(session, snapshot);
            return snapshot;
        }
        finally
        {
            _refreshQueued.Remove(userId);
        }
    }

    private void QueueRefresh(NetUserId userId)
    {
        if (_refreshQueued.Add(userId))
            _ = RefreshSnapshotIfOnlineAsync(userId);
    }

    private void PushSnapshot(ICommonSession session)
    {
        PushSnapshot(session, _snapshots.GetValueOrDefault(session.UserId, EmptySnapshot));
    }

    private void PushSnapshot(ICommonSession session, WH40KMuteSnapshot snapshot)
    {
        RaiseNetworkEvent(new WH40KMuteStateEvent(snapshot), session.Channel);
    }

    private static WH40KActiveMuteInfo? ToActiveInfo(WH40KMuteDef? mute)
    {
        return mute == null
            ? null
            : new WH40KActiveMuteInfo(mute.Type, mute.Reason, mute.ExpirationTime?.UtcDateTime);
    }

    private static bool SnapshotMayNeedRefresh(WH40KMuteSnapshot snapshot, DateTime now)
    {
        return IsExpired(snapshot.ChatMute, now) || IsExpired(snapshot.AHelpMute, now);
    }

    private static WH40KMuteSnapshot PruneExpired(WH40KMuteSnapshot snapshot, DateTime now)
    {
        var chat = IsExpired(snapshot.ChatMute, now) ? null : snapshot.ChatMute;
        var ahelp = IsExpired(snapshot.AHelpMute, now) ? null : snapshot.AHelpMute;
        var scopes = (chat == null ? WH40KMuteType.None : WH40KMuteType.Chat) |
                     (ahelp == null ? WH40KMuteType.None : WH40KMuteType.AHelp);
        return new WH40KMuteSnapshot(scopes, chat, ahelp);
    }

    private static bool IsExpired(WH40KActiveMuteInfo? mute, DateTime now)
    {
        return mute?.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= now;
    }

    private static string FormatScopes(WH40KMuteType typeMask)
    {
        return typeMask switch
        {
            WH40KMuteType.Chat => "chat",
            WH40KMuteType.AHelp => "ahelp",
            WH40KMuteType.Chat | WH40KMuteType.AHelp => "chat+ahelp",
            _ => typeMask.ToString()
        };
    }

    private bool ShouldIgnoreMute(ICommonSession session)
    {
        return WH40KStaffProtection.HasHostBypass(
            _adminManager.GetAdminData(session, includeDeAdmin: true),
            _adminManager.IsPromotedHost(session.UserId));
    }

    private async ValueTask<bool> HasHostBypassAsync(NetUserId userId, CancellationToken cancel = default)
    {
        if (_adminManager.IsPromotedHost(userId))
            return true;

        if (_players.TryGetSessionById(userId, out var session))
        {
            return WH40KStaffProtection.HasHostBypass(
                _adminManager.GetAdminData(session, includeDeAdmin: true),
                isPromotedHost: false);
        }

        var admin = await _db.GetAdminDataForAsync(userId, cancel);
        return admin != null && _adminHierarchy.GetAdminHierarchy(admin).IsHost;
    }

    private async ValueTask<AdminHierarchyInfo> TryGetHierarchyAsync(NetUserId userId, CancellationToken cancel)
    {
        if (_adminManager.IsPromotedHost(userId))
            return new AdminHierarchyInfo(true, true, 0, 0);

        if (_players.TryGetSessionById(userId, out var session))
            return _adminHierarchy.GetAdminHierarchy(session, includeDeAdmin: true);

        var admin = await _db.GetAdminDataForAsync(userId, cancel);
        return admin == null ? AdminHierarchyInfo.Missing : _adminHierarchy.GetAdminHierarchy(admin);
    }

    private async Task<T> WithTargetLockAsync<T>(NetUserId userId, Func<Task<T>> operation)
    {
        MuteOperationLockEntry entry;
        lock (_operationLockSync)
        {
            if (!_operationLocks.TryGetValue(userId, out var existing))
            {
                entry = new MuteOperationLockEntry();
                _operationLocks.Add(userId, entry);
            }
            else
            {
                entry = existing;
            }

            entry.ReferenceCount++;
        }

        await entry.Gate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            entry.Gate.Release();
            lock (_operationLockSync)
            {
                entry.ReferenceCount--;
                if (entry.ReferenceCount == 0)
                {
                    _operationLocks.Remove(userId);
                    entry.Gate.Dispose();
                }
            }
        }
    }

    private sealed class MuteOperationLockEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }
}
