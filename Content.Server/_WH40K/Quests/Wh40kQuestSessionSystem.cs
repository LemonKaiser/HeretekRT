using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Npcs;
using Content.Server.Database;
using Content.Shared._WH40K.Quests;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

/// <summary>Authenticated account loading, bounded recovery and the server-only operation gate.</summary>
public sealed partial class Wh40kQuestSessionSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private Wh40kQuestContentSystem _content = default!;
    [Dependency] private Wh40kNpcAnchorSystem _anchors = default!;
    [Dependency] private Content.Server._WH40K.Progression.Wh40kPartyManager _parties = default!;
    private readonly Dictionary<NetUserId, Guid> _partyOwners = new();
    private readonly Dictionary<NetUserId, IReadOnlyList<Wh40kQuestAccountState>> _states = new();
    public IReadOnlyList<Wh40kQuestAccountState> AccountState(NetUserId user) => _states.GetValueOrDefault(user) ?? [];
    public Wh40kQuestOwnerKey? PartyOwner(NetUserId user) => _partyOwners.TryGetValue(user, out var id) ? Wh40kQuestOwnerKey.Party(id) : null;
    private readonly Dictionary<NetUserId, IReadOnlyList<Wh40kQuestStoredStatus>> _cache = new();
    private readonly Dictionary<NetUserId, IReadOnlySet<string>> _completed = new();
    private readonly Dictionary<NetUserId, IReadOnlyList<Wh40kQuestInstance>> _history = new();
    public bool HasCompleted(NetUserId user, string definition) => IsReady(user) && _completed.TryGetValue(user, out var rows) && rows.Contains(definition);
    public IReadOnlyList<Wh40kQuestInstance> History(NetUserId user) => _history.GetValueOrDefault(user) ?? [];
    private readonly Dictionary<NetUserId, Guid> _loads = new();
    private readonly Dictionary<NetUserId, DateTime> _deadlines = new();
    private readonly HashSet<Task> _pending = new();
    private Guid _generation = Guid.NewGuid();
    private bool _stopping;
    private float _retryTimer;
    private Task _pump = Task.CompletedTask;
    private int _simulationThread;
    public event Action<NetUserId>? AccountLoaded;
    public Guid Generation => _generation;
    public bool IsStopping => _stopping;
    public T TrackWork<T>(T task) where T : Task => Track(task);
    public Task WaitForOperationsAsync()
    {
        lock (_pending) return Task.WhenAll(_pending.ToArray());
    }

    public override void Initialize()
    {
        base.Initialize();
        _simulationThread = Environment.CurrentManagedThreadId;
        _players.PlayerStatusChanged += OnStatusChanged;
        _parties.PartyChanged += OnPartyChanged;
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttached);
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnStatusChanged;
        _parties.PartyChanged -= OnPartyChanged;
        _tasks.BlockWaitOnTask(DrainAsync(false));
        base.Shutdown();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _generation = Guid.NewGuid();
        _partyOwners.Clear(); _states.Clear();
        _loads.Clear(); _completed.Clear(); _history.Clear(); _cache.Clear(); _deadlines.Clear(); _stopping = false;
        foreach (var session in _players.Sessions)
            if (session.Status == SessionStatus.InGame && session.Channel.AuthType != LoginType.Guest)
                Observe(LoadAccountAsync(session.UserId));
    }

    private void OnStatusChanged(object? sender, SessionStatusEventArgs ev)
    {
        if (ev.NewStatus is SessionStatus.Disconnected or SessionStatus.Zombie)
        { _loads.Remove(ev.Session.UserId); _cache.Remove(ev.Session.UserId); _completed.Remove(ev.Session.UserId); _history.Remove(ev.Session.UserId); _deadlines.Remove(ev.Session.UserId); _states.Remove(ev.Session.UserId); _partyOwners.Remove(ev.Session.UserId); }
        else if (ev.NewStatus == SessionStatus.InGame && ev.Session.Channel.AuthType != LoginType.Guest)
            Observe(LoadAccountAsync(ev.Session.UserId));
    }

    private void OnAttached(PlayerAttachedEvent ev)
    {
        if (ev.Player.Channel.AuthType.HasStaticUserId()) Observe(LoadAccountAsync(ev.Player.UserId));
    }

    private void OnPartyChanged(Content.Server._WH40K.Progression.Wh40kPartyRecord? party)
        => Observe(OnSimulationThread(() =>
        {
            foreach (var user in _cache.Keys.ToArray()) Observe(LoadAccountAsync(user));
        }));

    public bool IsReady(NetUserId user) => !_stopping && _cache.ContainsKey(user) &&
        _players.TryGetSessionById(user, out var session) && session.Status == SessionStatus.InGame && session.Channel.AuthType.HasStaticUserId();

    public bool TryGetStatus(NetUserId user, out IReadOnlyList<Wh40kQuestStoredStatus> status)
        => _cache.TryGetValue(user, out status!);

    public Task LoadAccountAsync(NetUserId user)
    {
        if (_stopping || !_players.TryGetSessionById(user, out var session) || session.Channel.AuthType == LoginType.Guest)
            return Task.CompletedTask;
        var token = Guid.NewGuid(); var generation = _generation;
        _loads[user] = token; _cache.Remove(user);
        return Track(LoadCoreAsync(user, token, generation));
    }

    private async Task LoadCoreAsync(NetUserId user, Guid token, Guid generation)
    {
        var owner = Wh40kQuestOwnerKey.Account(user);
        var definitions = _prototypes.EnumeratePrototypes<Wh40kQuestPrototype>().Select(x => x.ID).ToArray();
        try
        {
            var party = await _db.GetWh40kPartyAsync(user);
            var accountState = await _db.GetWh40kQuestAccountStateAsync(user.UserId);
            await _db.RecoverWh40kQuestsAsync(owner, DateTime.UtcNow, generation);
            var rows = (await _db.GetWh40kQuestsAsync(owner)).ToList();
            var history = (await _db.GetWh40kQuestsAsync(owner, history: true, limit: 32)).ToList();
            if (party != null)
            {
                var partyOwner = Wh40kQuestOwnerKey.Party(party.Id);
                await _db.RecoverWh40kQuestsAsync(partyOwner, DateTime.UtcNow, generation);
                rows.AddRange(await _db.GetWh40kQuestsAsync(partyOwner));
                history.AddRange(await _db.GetWh40kQuestsAsync(partyOwner, history: true, limit: 32));
            }
            var completed = new HashSet<string>();
            foreach (var batch in definitions.Chunk(128))
            {
                completed.UnionWith(await _db.GetCompletedWh40kQuestDefinitionsAsync(owner, batch));
                if (party != null) completed.UnionWith(await _db.GetCompletedWh40kQuestDefinitionsAsync(Wh40kQuestOwnerKey.Party(party.Id), batch));
            }
            var data = new List<(Wh40kQuestInstance Row, IReadOnlyList<Wh40kQuestObjective> Objectives)>();
            foreach (var row in rows)
            {
                var rowOwner = Wh40kQuestSnapshotCodec.TryDeserialize(row.SnapshotJson, out var terms) ? terms!.Owner : owner;
                data.Add((row, await _db.GetWh40kQuestObjectivesAsync(rowOwner, row.Id)));
            }
            await OnSimulationThread(() =>
            {
                if (_stopping || _generation != generation || !_loads.TryGetValue(user, out var current) || current != token ||
                    !_players.TryGetSessionById(user, out var session) || !session.Channel.AuthType.HasStaticUserId()) return;
                if (party != null) _partyOwners[user] = party.Id; else _partyOwners.Remove(user);
                _states[user] = accountState;
                _history[user] = history.OrderByDescending(x => x.ClosedAt).Take(32).ToArray();
                _completed[user] = completed;
                _cache[user] = data.Select(item => BindStatus(Wh40kQuestSnapshotCodec.TryDeserialize(item.Row.SnapshotJson, out var terms) ? terms!.Owner : owner,
                    item.Row, item.Objectives)).ToArray();
                AccountLoaded?.Invoke(user);
                var deadlines = rows.Where(row => row.Deadline != null).Select(row => row.Deadline!.Value).ToArray();
                if (deadlines.Length > 0) _deadlines[user] = deadlines.Min(); else _deadlines.Remove(user);
            });
        }
        finally
        {
            await OnSimulationThread(() =>
            {
                if (_loads.TryGetValue(user, out var current) && current == token) _loads.Remove(user);
            });
        }
    }

    private Wh40kQuestStoredStatus BindStatus(Wh40kQuestOwnerKey owner, Wh40kQuestInstance row,
        IReadOnlyList<Wh40kQuestObjective> objectives)
    {
        var compatible = Wh40kQuestSnapshotCodec.TryDeserialize(row.SnapshotJson, out var snapshot) &&
            snapshot!.Owner == owner && snapshot.DefinitionId == row.DefinitionId && _content.IsCompatibleContract(snapshot);
        var binding = compatible ? Wh40kQuestBindingState.WaitingForTarget : Wh40kQuestBindingState.WaitingForContent;
        if (compatible && snapshot!.Objectives.All(o => EntityManager.System<Wh40kQuestProgressSystem>().CanBind(o)) &&
            (snapshot.Issuer == null || _anchors.TryGetNpc(snapshot.Issuer, out _))) binding = Wh40kQuestBindingState.Bound;
        return new(row, objectives, snapshot, binding);
    }

    public Task<Wh40kQuestResult> MutateAsync(NetUserId user, Wh40kQuestRequest request, Func<bool>? authorize = null)
    {
        if (!IsReady(user)) return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.Pending, null, 0));
        return Track(MutateCoreAsync(user, request, _generation, authorize));
    }

    public Task<Wh40kQuestResult> ResolveForAdminAsync(Wh40kQuestOwnerKey owner, Guid actor, Wh40kQuestRequest request)
    {
        if (_stopping) return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.Pending, null, 0));
        return Track(ResolveCoreAsync(owner, actor, request));
    }

    private async Task<Wh40kQuestResult> ResolveCoreAsync(Wh40kQuestOwnerKey owner, Guid actor, Wh40kQuestRequest request)
    {
        var result = await _db.MutateWh40kQuestAsync(owner, actor, request, DateTime.UtcNow, admin: true);
        await OnSimulationThread(() =>
        {
            if (_stopping) return;
            var users = owner.Scope == Wh40kQuestOwnerScope.Party
                ? _partyOwners.Where(x => x.Value == owner.Id).Select(x => x.Key).ToArray()
                : [new NetUserId(owner.Id)];
            foreach (var user in users) if (_cache.ContainsKey(user)) Observe(LoadAccountAsync(user));
        });
        return result;
    }

    private async Task<Wh40kQuestResult> MutateCoreAsync(NetUserId user, Wh40kQuestRequest request, Guid generation, Func<bool>? authorize)
    {
        var owner = await _db.ResolveWh40kQuestOwnerAsync(user.UserId, request.TargetId);
        var committed = await _db.GetWh40kQuestOperationAsync(owner, request.OperationId);
        if (committed != null)
            return await _db.MutateWh40kQuestAsync(owner, user.UserId, request, DateTime.UtcNow);
        if (request.Kind == Wh40kQuestMutation.Accept)
        {
            var offers = await _db.GetWh40kQuestOffersAsync(owner, DateTime.UtcNow);
            var offer = offers.SingleOrDefault(offer => offer.Id == request.TargetId);
            var valid = false;
            await OnSimulationThread(() =>
            {
                valid = offer != null && _generation == generation && IsReady(user) &&
                    _content.IsValidQuest(offer.DefinitionId) && _prototypes.Index<Wh40kQuestPrototype>(offer.DefinitionId).Enabled;
            });
            if (!valid)
            {
                return new Wh40kQuestResult(Wh40kQuestOperationResult.InvalidContent, null, 0);
            }
        }
        var allowed = false;
        var rejection = Wh40kQuestOperationResult.UnavailableTarget;
        await OnSimulationThread(() =>
        {
            allowed = generation == _generation && IsReady(user) && (authorize?.Invoke() ?? true);
            if (allowed && request.Kind == Wh40kQuestMutation.Complete)
            {
                var status = _cache[user].SingleOrDefault(state => state.Contract.Id == request.TargetId);
                allowed = status != null && status.Binding != Wh40kQuestBindingState.WaitingForContent &&
                    _content.IsCompatibleContract(status.Snapshot!) &&
                    (status.Snapshot!.Issuer == null || _anchors.TryGetNpc(status.Snapshot.Issuer, out var issuer) &&
                        _players.TryGetSessionById(user, out var actorSession) && actorSession.AttachedEntity is {} body &&
                        _anchors.IsInRange(body, issuer));
                if (allowed && !EntityManager.System<Wh40kQuestProgressSystem>().HasRequiredPossessions(user, request.TargetId))
                { allowed = false; rejection = Wh40kQuestOperationResult.NotReady; }
            }
        });
        if (!allowed) return new(rejection, null, 0);
        var result = await _db.MutateWh40kQuestAsync(owner, user.UserId, request, DateTime.UtcNow);
        Task reload = Task.CompletedTask;
        await OnSimulationThread(() =>
        {
            if (generation != _generation || _stopping) return;
            reload = owner.Scope == Wh40kQuestOwnerScope.Party
                ? Task.WhenAll(_partyOwners.Where(x => x.Value == owner.Id).Select(x => x.Key).Append(user).Distinct().ToArray().Select(LoadAccountAsync))
                : LoadAccountAsync(user);
        });
        await reload;
        return result;
    }

    public override void Update(float frameTime)
    {
        _retryTimer += frameTime;
        if (_stopping || _retryTimer < 5f || !_pump.IsCompleted) return;
        _retryTimer = 0;
        foreach (var user in _cache.Keys.ToArray())
        {
            _cache[user] = _cache[user].Select(state => BindStatus(state.Snapshot?.Owner ?? Wh40kQuestOwnerKey.Account(user), state.Contract, state.Objectives)).ToArray();
            AccountLoaded?.Invoke(user);
        }
        foreach (var session in _players.Sessions.Where(session => session.Status == SessionStatus.InGame &&
                     session.Channel.AuthType != LoginType.Guest && !_cache.ContainsKey(session.UserId) && !_loads.ContainsKey(session.UserId)).Take(32))
            Observe(LoadAccountAsync(session.UserId));
        foreach (var user in _deadlines.Where(pair => pair.Value <= DateTime.UtcNow && !_loads.ContainsKey(pair.Key)).Select(pair => pair.Key).ToArray())
            Observe(LoadAccountAsync(user));
        var available = _prototypes.EnumeratePrototypes<Wh40kQuestPrototype>().Where(quest => _content.IsValidQuest(quest.ID))
            .Select(quest => quest.ID).ToHashSet();
        _pump = Track(PumpAsync(available, _generation)); Observe(_pump);
    }

    private async Task PumpAsync(IReadOnlySet<string> available, Guid generation)
    {
        async Task<bool> ContentAvailable(Wh40kQuestContractSnapshot snapshot)
        {
            var valid = false;
            await OnSimulationThread(() => valid = !_stopping && generation == _generation && _content.IsCompatibleContract(snapshot));
            return valid;
        }
        var changed = await _db.ProcessWh40kQuestInboxAsync(DateTime.UtcNow,
            availableDefinitions: available, contentAvailable: ContentAvailable);
        if (changed > 0) await OnSimulationThread(() =>
        {
            if (_generation == generation && !_stopping)
                foreach (var user in _cache.Keys.ToArray()) Observe(LoadAccountAsync(user));
        });
    }

    public async Task DrainAsync(bool roundRestart)
    {
        _stopping = true;
        async Task Drain()
        {
            await EntityManager.System<Wh40kQuestProgressSystem>().DrainFactsAsync();
            Task[] pending; lock (_pending) pending = _pending.ToArray();
            await Task.WhenAll(pending);
        }
        await Drain().WaitAsync(TimeSpan.FromSeconds(10));
        if (roundRestart) await OnSimulationThread(() => _stopping = false);
    }

    internal Task ResumeAfterAbortedDrainAsync() => OnSimulationThread(() => _stopping = false);

    private T Track<T>(T task) where T : Task
    {
        lock (_pending) _pending.Add(task);
        _ = task.ContinueWith(completed => { lock (_pending) _pending.Remove(completed); }, TaskScheduler.Default);
        return task;
    }

    private async void Observe(Task task)
    {
        try { await task; }
        catch (Exception error) { Log.Error($"Quest account operation failed; durable state retained: {error}"); }
    }

    internal Task OnSimulationThread(Action action)
    {
        // SQLite's synchronous test mode must finish ECS callbacks before returning from the DB command.
        // Production DB workers still marshal through the engine task queue.
        if (Environment.CurrentManagedThreadId == _simulationThread)
        {
            try { action(); return Task.CompletedTask; }
            catch (Exception error) { return Task.FromException(error); }
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tasks.RunOnMainThread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }
}
