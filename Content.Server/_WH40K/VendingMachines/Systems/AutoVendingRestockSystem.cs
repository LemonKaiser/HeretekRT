using Content.Server.VendingMachines;
using Content.Server._WH40K.VendingMachines.Components;
using Content.Server.GameTicking;
using Content.Shared.Station.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.VendingMachines.Systems;

/// <summary>
/// Restocks selected station grids once every three hours.
/// The actual machine updates are spread over several ticks so a large station does not
/// receive one burst of inventory and network updates.
/// </summary>
public sealed class AutoVendingRestockSystem : EntitySystem
{
    private static readonly TimeSpan RestockInterval = TimeSpan.FromHours(3);
    private const int MachinesPerTick = 4;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly VendingMachineSystem _vendingMachines = default!;

    private readonly Queue<EntityUid> _pending = new();
    private readonly HashSet<EntityUid> _pendingSet = new();
    private TimeSpan? _nextRestockAt;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<AutoVendingRestockComponent, ComponentStartup>(OnGridMarked);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_nextRestockAt is { } next && _timing.CurTime >= next)
        {
            QueueConfiguredMachines(recordRestock: true);
            _nextRestockAt = _timing.CurTime + RestockInterval;
        }

        for (var i = 0; i < MachinesPerTick && _pending.TryDequeue(out var machine); i++)
        {
            _pendingSet.Remove(machine);

            if (TerminatingOrDeleted(machine) ||
                !TryComp<VendingMachineComponent>(machine, out var vending) ||
                !TryComp<TransformComponent>(machine, out var transform) ||
                transform.GridUid is not { } grid ||
                !HasComp<AutoVendingRestockComponent>(grid))
            {
                continue;
            }

            _vendingMachines.ResetInventoryFromPrototype(machine, vending);
        }
    }

    /// <summary>
    /// Queues every vending machine on grids marked for automatic restocking.
    /// </summary>
    public int QueueConfiguredMachines(bool recordRestock = false)
    {
        var grids = new HashSet<EntityUid>();
        var gridQuery = EntityQueryEnumerator<AutoVendingRestockComponent, MapGridComponent>();
        while (gridQuery.MoveNext(out var grid, out var restock, out _))
        {
            if (recordRestock)
                restock.LastRestockAt = _timing.CurTime;

            grids.Add(grid);
        }

        return QueueMachinesOnGrids(grids);
    }

    /// <summary>
    /// Queues every vending machine on a particular grid, regardless of its automatic marker.
    /// </summary>
    public int QueueMachinesOnGrid(EntityUid grid)
    {
        return QueueMachinesOnGrids(new HashSet<EntityUid> { grid });
    }

    /// <summary>
    /// Marks or unmarks a grid for the scheduled restock.
    /// </summary>
    public bool SetGridEnabled(EntityUid grid, bool enabled)
    {
        if (!HasComp<MapGridComponent>(grid))
            return false;

        if (enabled)
            EnsureComp<AutoVendingRestockComponent>(grid);
        else
            RemComp<AutoVendingRestockComponent>(grid);

        return true;
    }

    public TimeSpan? GetNextRestockAt() => _nextRestockAt;

    private int QueueMachinesOnGrids(HashSet<EntityUid> grids)
    {
        if (grids.Count == 0)
            return 0;

        var queued = 0;
        var query = EntityQueryEnumerator<VendingMachineComponent, TransformComponent>();
        while (query.MoveNext(out var machine, out _, out var transform))
        {
            if (transform.GridUid is not { } grid || !grids.Contains(grid) || !_pendingSet.Add(machine))
                continue;

            _pending.Enqueue(machine);
            queued++;
        }

        return queued;
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        _pending.Clear();
        _pendingSet.Clear();

        _nextRestockAt = args.New == GameRunLevel.InRound
            ? _timing.CurTime + RestockInterval
            : null;
    }

    private void OnGridMarked(EntityUid grid, AutoVendingRestockComponent component, ComponentStartup args)
    {
        if (_nextRestockAt is not { } ||
            _timing.CurTime - component.LastRestockAt < RestockInterval)
        {
            return;
        }

        component.LastRestockAt = _timing.CurTime;
        QueueMachinesOnGrid(grid);
    }
}
