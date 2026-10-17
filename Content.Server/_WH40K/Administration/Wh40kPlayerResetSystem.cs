using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Mono.MonoCoins;
using Content.Server._WH40K.Act1;
using Content.Server._WH40K.CharacterCreation;
using Content.Server._WH40K.ClassProgression;
using Content.Server._WH40K.DeathTransition;
using Content.Server._WH40K.Dialogue;
using Content.Server._WH40K.PersistentInventory;
using Content.Server._WH40K.Progression;
using Content.Server._WH40K.Quests;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Preferences;
using Content.Shared._WF.SafetyDepositBox.Components;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Administration;

public sealed partial class Wh40kPlayerResetSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private IAdminAuthorizationManager _authorization = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private UserDbDataManager _userDb = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private Wh40kPlayerProgressManager _onboarding = default!;
    [Dependency] private Wh40kAccountRpgManager _foundation = default!;
    [Dependency] private Wh40kProgressManager _progress = default!;
    [Dependency] private Wh40kClassProgressManager _classes = default!;
    [Dependency] private Wh40kPartyManager _parties = default!;
    [Dependency] private PersistentInventoryManager _inventory = default!;
    [Dependency] private MonoCoinsManager _coins = default!;

    private readonly ConcurrentDictionary<NetUserId, byte> _resetting = new();
    private readonly Dictionary<NetUserId, HashSet<Task>> _operations = new();
    private bool _busy;

    public bool IsResetting(NetUserId userId) => _resetting.ContainsKey(userId);

    public override void Initialize()
    {
        base.Initialize();
        _net.Connecting += OnConnecting;
    }

    public override void Shutdown()
    {
        _net.Connecting -= OnConnecting;
        base.Shutdown();
    }

    private Task OnConnecting(NetConnectingArgs args)
    {
        if (IsResetting(args.UserId))
            args.Deny(new NetDenyReason(Loc.GetString("wh40k-player-reset-disconnect")));
        return Task.CompletedTask;
    }

    public async Task<T> RunPlayerOperationAsync<T>(NetUserId userId, Func<Task<T>> operation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_operations)
        {
            if (IsResetting(userId))
                throw new InvalidOperationException("The player's progress is being reset.");
            if (!_operations.TryGetValue(userId, out var pending))
                _operations[userId] = pending = new HashSet<Task>();
            pending.Add(completion.Task);
        }
        try
        {
            return await operation();
        }
        finally
        {
            lock (_operations)
            {
                _operations[userId].Remove(completion.Task);
                if (_operations[userId].Count == 0)
                    _operations.Remove(userId);
            }
            completion.TrySetResult();
        }
    }

    public Task RunPlayerOperationAsync(NetUserId userId, Func<Task> operation)
        => RunPlayerOperationAsync(userId, async () => { await operation(); return true; });

    private Task WaitForOperationsAsync(NetUserId userId)
    {
        lock (_operations)
            return Task.WhenAll(_operations.TryGetValue(userId, out var pending) ? pending.ToArray() : []);
    }

    public async Task<bool> ResetAsync(NetUserId userId, string username, ICommonSession? admin, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
            return false;
        if (await _authorization.TryDenyTargetAsync(admin, userId, AdminOperation.Wh40kPlayerReset, username))
            return false;
        var quests = EntityManager.System<Wh40kQuestSessionSystem>();
        if (_busy || quests.IsStopping)
            return false;
        if (_players.TryGetSessionById(userId, out var session) && !session.Channel.AuthType.HasStaticUserId())
            return false;

        _busy = true;
        lock (_operations)
            _resetting[userId] = 0;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _userDb.SetLoadBarrier(userId, barrier.Task);
        var actor = admin?.UserId.UserId ?? new Guid("00000000-0000-0000-0000-000000000001");
        var actorName = admin?.Name ?? "server-console";
        Wh40kPartyRecord? party = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var load = session?.Status == SessionStatus.InGame ? _userDb.GetLoadTask(session) : Task.CompletedTask;
            // A reconnect that has already passed the network approval still cannot load preferences or spawn.
            session?.Channel.Disconnect(Loc.GetString("wh40k-player-reset-disconnect"));
            try { await load.WaitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
            await quests.DrainAsync(false);
            await EntityManager.System<DialogueSystem>().PreparePlayerResetAsync(userId).WaitAsync(timeout.Token);
            await WaitForOperationsAsync(userId).WaitAsync(timeout.Token);

            var completions = new List<Task>();
            var prologues = EntityQueryEnumerator<Act1PrologueInstanceComponent>();
            while (prologues.MoveNext(out _, out var prologue))
                if (prologue.UserId == userId && prologue.CompletionTask != null)
                    completions.Add(prologue.CompletionTask);
            await Task.WhenAll(completions).WaitAsync(timeout.Token);
            await _db.WaitForPendingCommandsAsync(timeout.Token);

            var lifecycle = EntityManager.System<PersistentInventoryLifecycleSystem>();
            await lifecycle.EnsureReadyAsync().WaitAsync(timeout.Token);
            await EntityManager.System<PersistentInventorySaveSystem>().StartReconciliation().WaitAsync(timeout.Token);
            await _inventory.LoadAsync(userId, timeout.Token);
            var preparation = await lifecycle.PrepareRespawnAsync(userId,
                PersistentInventoryInvalidationReason.StaffAction, "wh40kwipe", actor, reason, timeout.Token);
            if (!preparation.Success)
                throw new InvalidOperationException(preparation.Message);
            if ((await _db.GetPersistentInventoryHeaderAsync(userId, timeout.Token))?.State == PersistentInventorySnapshotState.Bound)
            {
                var invalidation = await lifecycle.AdminInvalidateAsync(userId, "wh40kwipe", actor, reason, timeout.Token);
                if (!invalidation.Success)
                    throw new InvalidOperationException(invalidation.Message);
            }

            party = await _db.GetWh40kPartyAsync(userId, timeout.Token);
            var temporary = HumanoidCharacterProfile.Random().WithBankBalance(0)
                .WithWh40kCharacterBuild(new());
            await _db.ResetWh40kPlayerAsync(userId, temporary, actor, reason, timeout.Token);

            DeletePlayerEntities(userId);
            if (session != null)
                _preferences.OnClientDisconnected(session);
            _onboarding.Remove(userId);
            _foundation.Remove(userId);
            _progress.Remove(userId);
            _classes.Remove(userId);
            _inventory.Remove(userId);
            _coins.InvalidateCachedBalance(userId);
            EntityManager.System<GhostPermissionSystem>().ForgetPlayer(userId);
            _parties.InvalidatePlayer(userId);
            if (party != null)
                foreach (var member in party.Members.Where(member => member.UserId != userId))
                {
                    try { await _parties.LoadAsync(member.UserId, timeout.Token); }
                    catch (Exception error) { Log.Error($"Failed to refresh party after player reset: {error}"); }
                }

            _adminLog.Add(LogType.Action, LogImpact.High,
                $"WH40K admin {actorName} ({actor}) fully reset {username} ({userId}). Reason: {reason}");
            return true;
        }
        catch (Exception error)
        {
            Log.Error($"WH40K player reset failed for {username} ({userId}), actor {actor}: {error}");
            return false;
        }
        finally
        {
            if (_players.TryGetSessionById(userId, out var reconnect) && reconnect.Status != SessionStatus.Disconnected)
                reconnect.Channel.Disconnect(Loc.GetString("wh40k-player-reset-disconnect"));
            _userDb.RemoveLoadBarrier(userId);
            barrier.TrySetResult();
            _resetting.TryRemove(userId, out _);
            _busy = false;
            await quests.ResumeAfterAbortedDrainAsync();
            if (party != null)
                foreach (var member in party.Members.Where(member => member.UserId != userId &&
                             _players.TryGetSessionById(member.UserId, out var current) && current.Status == SessionStatus.InGame))
                {
                    try { await quests.LoadAccountAsync(member.UserId); }
                    catch (Exception error) { Log.Error($"Failed to refresh party quests after player reset: {error}"); }
                }
        }
    }

    private void DeletePlayerEntities(NetUserId userId)
    {
        var bodies = new HashSet<EntityUid>();
        var minds = EntityManager.System<MindSystem>();
        if (minds.TryGetMind(userId, out var mindId, out var mind))
        {
            if (mind.OwnedEntity is {} owned)
                bodies.Add(owned);
            if (mind.VisitingEntity is {} visiting && HasComp<GhostComponent>(visiting))
                bodies.Add(visiting);
            minds.UnVisit(mindId.Value, mind);
            minds.WipeMind(mindId, mind);
        }
        var bound = EntityQueryEnumerator<PersistentInventoryBoundLifeComponent>();
        while (bound.MoveNext(out var body, out var life))
            if (life.UserId == userId)
                bodies.Add(body);
        var prologues = EntityQueryEnumerator<Act1PrologueInstanceComponent>();
        while (prologues.MoveNext(out var body, out var prologue))
            if (prologue.UserId == userId)
                bodies.Add(body);
        var boxes = EntityQueryEnumerator<SafetyDepositBoxComponent>();
        while (boxes.MoveNext(out var box, out var contents))
            if (contents.OwnerId == userId.UserId)
                bodies.Add(box);
        foreach (var body in bodies)
            if (!TerminatingOrDeleted(body))
                Del(body);
    }
}
