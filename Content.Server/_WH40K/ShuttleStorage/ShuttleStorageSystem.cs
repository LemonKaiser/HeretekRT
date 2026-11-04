using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WH40K.OperationalDomain;
using Content.Server.Database;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.ShuttleStorage;
using Content.Shared.ActionBlocker;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private ShipOwnershipSystem _ownership = default!;
    [Dependency] private OperationalDomainSystem _domains = default!;
    [Dependency] private ShuttleStorageSerializer _serializer = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<NetUserId, View> _views = new();
    private readonly Dictionary<NetUserId, TimeSpan> _cooldowns = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Guid _epoch = Guid.NewGuid();
    private int _thread;
    private long _sequence;
    private TimeSpan _nextPublish;
    private Task? _startup;
    private Task? _work;
    private Transfer? _transfer;
    private bool _stopping;
    private (uint Tick, TaskCompletionSource<bool> Completion)? _frameWait;

    private sealed class View
    {
        public Wh40kShuttleSlot? Row;
        public Task<Wh40kShuttleSlot?>? Read;
        public ShuttleStorageResult Result = ShuttleStorageResult.Busy;
        public TimeSpan ViewedAt;
        public long PreviewRevision = -1;
        public Task<ShuttleStoragePreview?>? PreviewRead;
        public ShuttleStoragePreview? Preview;
    }

    public bool Busy => _transfer != null;
    public bool IsTransferring(EntityUid grid) => _transfer?.Grid == grid ||
        TryComp<ShuttleStorageTransferComponent>(grid, out var marker) && marker.Active;

    public bool HasActiveTransfer(MapId map) => _transfer is { } operation &&
        (TryComp(operation.Grid, out TransformComponent? xform) && xform.MapID == map ||
         TryComp(operation.Console, out TransformComponent? console) && console.MapID == map ||
         TryComp(operation.Facility, out TransformComponent? facility) && facility.MapID == map);

    public override void Initialize()
    {
        base.Initialize();
        _thread = Environment.CurrentManagedThreadId;
        SubscribeLocalEvent<ShuttleStorageComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<ShuttleStorageComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<ShuttleStorageComponent, ShuttleStorageRefresh>(OnRefresh);
        SubscribeLocalEvent<ShuttleStorageComponent, ShuttleStorageRequest>(OnRequest);
        SubscribeLocalEvent<ShuttleStorageTransferComponent, ConsoleFTLAttemptEvent>(OnFtlAttempt);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _epoch = Guid.NewGuid();
            _frameWait?.Completion.TrySetCanceled();
            _frameWait = null;
            _views.Clear();
            _cooldowns.Clear();
            _startup = null;
        });
    }

    public override void Shutdown()
    {
        DrainForShutdown();
        base.Shutdown();
    }

    public void DrainForShutdown()
    {
        _stopping = true;
        _frameWait?.Completion.TrySetResult(true);
        _frameWait = null;
        try
        {
            if (_work is { IsCompleted: false })
                _tasks.BlockWaitOnTask(_work.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        catch (Exception error) { Log.Warning($"Shuttle transfer left for startup recovery: {error.Message}"); }
        _shutdown.Cancel();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_frameWait is { } wait && _timing.CurTick.Value >= wait.Tick)
        {
            _frameWait = null;
            wait.Completion.TrySetResult(true);
        }
        // Startup recovery also runs when the console feature is disabled.
        if (_startup == null && _transfer == null)
            _startup = RecoverPendingAsync();
        if (_nextPublish > _timing.CurTime)
            return;
        _nextPublish = _timing.CurTime + TimeSpan.FromSeconds(1);
        foreach (var (owner, view) in _views.ToArray())
        {
            if (view.Read is { IsCompleted: true } read)
            {
                try
                {
                    view.Row = read.GetAwaiter().GetResult();
                    view.Result = ShuttleStorageResult.Ready;
                }
                catch (Exception error)
                {
                    view.Result = ShuttleStorageResult.Unavailable;
                    Log.Warning($"Shuttle metadata could not be read for {owner}: {error.Message}");
                }
                view.Read = null;
            }
            if (view.Read == null && view.Result == ShuttleStorageResult.Unavailable && _transfer?.Owner != owner)
                view.Read = _db.GetWh40kShuttleAsync(owner.UserId);
            if (view.Read == null && _transfer?.Owner != owner &&
                (view.Result == ShuttleStorageResult.Cooldown &&
                 (!_cooldowns.TryGetValue(owner, out var until) || until <= _timing.CurTime) ||
                 (view.Result is ShuttleStorageResult.Busy or ShuttleStorageResult.ServiceBusy) &&
                 _transfer == null && _startup is { IsCompletedSuccessfully: true }))
                view.Result = ShuttleStorageResult.Ready;
            if (_timing.CurTime - view.ViewedAt > TimeSpan.FromMinutes(2) && _transfer?.Owner != owner)
                _views.Remove(owner);
        }
        Publish();
    }

    private bool Facility(EntityUid console, out EntityUid station, out EntityUid grid)
    {
        station = default;
        grid = default;
        if (TerminatingOrDeleted(console) || !HasComp<ShuttleStorageComponent>(console) ||
            Transform(console).GridUid is not { } facility || _domains.IsVesselGrid(facility) ||
            _stations.GetOwningStation(console) is not { } owner)
            return false;
        station = owner;
        grid = facility;
        return true;
    }

    private bool CanUse(EntityUid console, EntityUid actor)
    {
        return !TerminatingOrDeleted(actor) && !_stopping && Facility(console, out _, out _) &&
               TryComp(actor, out ActorComponent? user) && user.PlayerSession.AttachedEntity == actor &&
               user.PlayerSession.Channel.AuthType.HasStaticUserId() &&
               _access.IsAllowed(actor, console) && _blocker.CanInteract(actor, console) && _interaction.InRangeUnobstructed(actor, console, popup: false) &&
               (!TryComp(console, out ApcPowerReceiverComponent? power) || power.Powered);
    }

    private void OnFtlAttempt(EntityUid grid, ShuttleStorageTransferComponent marker, ref ConsoleFTLAttemptEvent args)
    {
        if (!IsTransferring(grid))
            return;
        args.Cancelled = true;
        args.Reason = Loc.GetString("wh40k-shuttle-storage-result-busy");
    }

    private void OnOpenAttempt(Entity<ShuttleStorageComponent> console, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!CanUse(console, args.User))
            args.Cancel();
    }

    private void OnOpened(Entity<ShuttleStorageComponent> console, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey.Equals(ShuttleStorageUiKey.Key) && CanUse(console, args.Actor))
        {
            Refresh(Comp<ActorComponent>(args.Actor).PlayerSession.UserId);
            Publish();
        }
    }

    private void OnRefresh(EntityUid console, ShuttleStorageComponent component, ShuttleStorageRefresh args)
    {
        if (CanUse(console, args.Actor) && _ui.GetActors(console, ShuttleStorageUiKey.Key).Contains(args.Actor))
            Refresh(Comp<ActorComponent>(args.Actor).PlayerSession.UserId);
    }

    private void OnRequest(EntityUid console, ShuttleStorageComponent component, ShuttleStorageRequest args)
    {
        if (!CanUse(console, args.Actor) || !_ui.GetActors(console, ShuttleStorageUiKey.Key).Contains(args.Actor))
            return;
        var owner = Comp<ActorComponent>(args.Actor).PlayerSession.UserId;
        EntityUid? grid = args.Ship is { } net ? GetEntity(net) : null;
        _ = TransferAsync(owner, console, grid, args.Revision, args.RequestId);
    }

    private void Refresh(NetUserId owner)
    {
        if (!_views.TryGetValue(owner, out var view))
            _views[owner] = view = new View();
        view.ViewedAt = _timing.CurTime;
        if (view.Read == null && _transfer?.Owner != owner)
            view.Read = _db.GetWh40kShuttleAsync(owner.UserId);
    }

    private void Publish()
    {
        var consoles = EntityManager.AllEntityQueryEnumerator<ShuttleStorageComponent>();
        while (consoles.MoveNext(out var console, out _))
        {
            foreach (var actor in _ui.GetActors(console, ShuttleStorageUiKey.Key).ToArray())
            {
                if (!CanUse(console, actor))
                {
                    _ui.CloseUi(console, ShuttleStorageUiKey.Key, actor);
                    continue;
                }
                var owner = Comp<ActorComponent>(actor).PlayerSession.UserId;
                _ui.ServerSendUiMessage(console, ShuttleStorageUiKey.Key, Snapshot(console, owner), actor);
            }
        }
    }

    public ShuttleStorageSnapshot Snapshot(EntityUid console, NetUserId owner)
    {
        if (!_views.TryGetValue(owner, out var view))
        {
            Refresh(owner);
            view = _views[owner];
        }
        view.ViewedAt = _timing.CurTime;
        var row = view.Row;
        var ships = new List<ShuttleStorageShip>();
        var stationName = "";
        if (Facility(console, out var station, out _))
        {
            stationName = Name(station);
            var owned = EntityManager.AllEntityQueryEnumerator<ShipOwnershipComponent>();
            while (owned.MoveNext(out var grid, out var ownership))
            {
                if (ownership.OwnerUserId != owner || !_shipyard.TryGetStationBerth(grid, out var berthStation, out _) || berthStation != station)
                    continue;
                var result = Inspect(console, owner, grid, out var info);
                ships.Add(new(GetNetEntity(grid), Name(grid), info.Width, info.Height, info.Entities.Count, result,
                    GridPreview(grid, info)));
            }
        }
        var status = !_config.GetCVar(ShuttleStorageCVars.Enabled) ? ShuttleStorageResult.Disabled :
            _transfer?.Owner == owner ? ShuttleStorageResult.Busy :
            _transfer != null || _startup is not { IsCompletedSuccessfully: true } ? ShuttleStorageResult.ServiceBusy : view.Result;
        UpdatePreview(owner, view);
        return new(++_sequence, row?.Revision ?? 0, (ShuttleStorageState) (row?.State ?? Wh40kShuttleState.Empty),
            stationName, row?.Name, row?.Width ?? 0, row?.Height ?? 0, row?.Bytes ?? 0, row?.SavedAt,
            ships.OrderBy(ship => ship.Name).ToArray(), status,
            _transfer?.Owner == owner ? _transfer.Request : row?.Operation ?? Guid.Empty,
            view.Preview, view.PreviewRead != null);
    }

    public ShuttleStorageResult Inspect(EntityUid console, NetUserId owner, EntityUid grid, out ShuttleStorageInspection info)
    {
        info = new(0, 0, []);
        if (TerminatingOrDeleted(grid) || !HasComp<ShuttleComponent>(grid) || !Facility(console, out var station, out _) ||
            grid == Transform(console).GridUid)
            return ShuttleStorageResult.InvalidShip;
        if (!TryComp(grid, out ShipOwnershipComponent? ownership) || ownership.OwnerUserId != owner)
            return ShuttleStorageResult.Ownership;
        if (!_shipyard.TryGetStationBerth(grid, out var dockedStation, out _) || dockedStation != station)
            return ShuttleStorageResult.NotDocked;
        if (IsTransferring(grid) && _transfer?.Grid != grid || HasComp<FTLComponent>(grid))
            return ShuttleStorageResult.Busy;
        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith is { } other && (_stations.GetOwningStation(other) != station ||
                Transform(other).GridUid is { } otherGrid && _domains.IsVesselGrid(otherGrid)))
                return ShuttleStorageResult.NotDocked;
        }
        return _serializer.Inspect(grid, out info);
    }

    private Task<T> OnMain<T>(Func<T> action)
    {
        if (_shutdown.IsCancellationRequested)
            return Task.FromCanceled<T>(_shutdown.Token);
        if (Environment.CurrentManagedThreadId == _thread)
            return Task.FromResult(action());
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tasks.RunOnMainThread(() =>
        {
            try
            {
                _shutdown.Token.ThrowIfCancellationRequested();
                completion.TrySetResult(action());
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
    }
}
