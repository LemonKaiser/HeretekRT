using Content.Shared._Mono;
using Content.Shared._Mono.NoHack;
using Content.Shared._Mono.NoDeconstruct;
using Content.Server.Construction.Components;
using Content.Shared.GameTicking;
using Content.Shared.Doors.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Server._Mono;

/// <summary>
/// Keeps authored facility entities protected as they are created, rebuilt or moved onto a grid.
/// The component is never attached to procedural terrain, so asteroid mining remains available.
/// </summary>
public sealed partial class GridRaiderSystem : EntitySystem
{
    private const float RefreshInterval = 1f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    private float _refreshAccumulator;
    private readonly Queue<EntityUid> _refreshQueue = new();
    private readonly HashSet<EntityUid> _queuedGrids = new();
    private readonly HashSet<EntityUid> _intersectingEntities = new();
    private readonly List<EntityUid> _staleProtectedEntities = new();

    // Counts only components created by this system. This lets protection transfer safely
    // between two protected grids without removing a component still owned by the new grid.
    private readonly Dictionary<EntityUid, int> _noHackOwners = new();
    private readonly Dictionary<EntityUid, int> _noDeconstructOwners = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridRaiderComponent, MapInitEvent>(OnGridRaiderMapInit);
        SubscribeLocalEvent<GridRaiderComponent, ComponentStartup>(OnGridRaiderStartup);
        SubscribeLocalEvent<GridRaiderComponent, ComponentShutdown>(OnGridRaiderShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ClearTransientState());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _refreshAccumulator += frameTime;
        if (_refreshAccumulator >= RefreshInterval)
        {
            _refreshAccumulator -= RefreshInterval;
            QueueGridRefreshes();
        }

        // A full authored station lookup can be expensive. Spread the protected grids over
        // separate ticks instead of combining all lookups into a periodic frame spike.
        if (!_refreshQueue.TryDequeue(out var grid))
            return;

        _queuedGrids.Remove(grid);
        if (TryComp<GridRaiderComponent>(grid, out var component) && HasComp<MapGridComponent>(grid))
            RefreshGridProtection(grid, component);
    }

    private void QueueGridRefreshes()
    {
        var grids = EntityQueryEnumerator<GridRaiderComponent>();
        while (grids.MoveNext(out var grid, out _))
        {
            if (_queuedGrids.Add(grid))
                _refreshQueue.Enqueue(grid);
        }
    }

    private void OnGridRaiderMapInit(EntityUid uid, GridRaiderComponent component, MapInitEvent args)
    {
        if (!HasComp<MapGridComponent>(uid))
        {
            Log.Warning($"GridRaiderComponent applied to non-grid entity {ToPrettyString(uid)}");
            return;
        }

        ApplyInitialProtection(uid, component);
    }

    private void OnGridRaiderStartup(EntityUid uid, GridRaiderComponent component, ComponentStartup args)
    {
        if (HasComp<MapGridComponent>(uid))
            ApplyInitialProtection(uid, component);
    }

    private void OnGridRaiderShutdown(EntityUid uid, GridRaiderComponent component, ComponentShutdown args)
    {
        foreach (var entity in component.ProtectedEntities)
        {
            RemoveProtection(entity, component, Exists(entity));
        }

        component.ProtectedEntities.Clear();
        component.AddedNoHackEntities.Clear();
        component.AddedNoDeconstructEntities.Clear();
    }

    private void RefreshGridProtection(EntityUid grid, GridRaiderComponent component)
    {
        ApplyInitialProtection(grid, component);

        _staleProtectedEntities.Clear();
        foreach (var entity in component.ProtectedEntities)
        {
            if (!Exists(entity))
            {
                _staleProtectedEntities.Add(entity);
                continue;
            }

            if (TryComp<TransformComponent>(entity, out var transform) && transform.GridUid == grid)
                continue;

            _staleProtectedEntities.Add(entity);
        }

        foreach (var entity in _staleProtectedEntities)
        {
            component.ProtectedEntities.Remove(entity);
            RemoveProtection(entity, component, Exists(entity));
        }
    }

    private void ApplyInitialProtection(EntityUid gridUid, GridRaiderComponent component)
    {
        _intersectingEntities.Clear();
        _lookup.GetEntitiesIntersecting(gridUid, _intersectingEntities);

        foreach (var entity in _intersectingEntities)
            TryProtectEntity(gridUid, entity, component);
    }

    private void TryProtectEntity(EntityUid gridUid, EntityUid entity, GridRaiderComponent component)
    {
        if (entity == gridUid ||
            _container.IsEntityInContainer(entity) ||
            !TryComp<TransformComponent>(entity, out var transform) ||
            transform.GridUid != gridUid)
            return;

        var shouldProtect = false;
        var hackProtect = true;

        if (component.ProtectDoors && HasComp<DoorComponent>(entity))
            shouldProtect = true;

        if (component.ProtectVendingMachines && HasComp<VendingMachineComponent>(entity))
        {
            shouldProtect = true;
            hackProtect = false; // vendors remain hackable, but cannot be deconstructed.
        }

        if (component.ProtectConstructables && HasComp<ConstructionComponent>(entity))
            shouldProtect = true;

        if (shouldProtect)
            ApplyProtection(entity, component, hackProtect);
    }

    private void ApplyProtection(EntityUid entityUid, GridRaiderComponent component, bool hackProtect = true, bool deconProtect = true)
    {
        if (component.ProtectedEntities.Contains(entityUid))
            return;

        if (hackProtect)
        {
            if (_noHackOwners.TryGetValue(entityUid, out var owners))
            {
                _noHackOwners[entityUid] = owners + 1;
                component.AddedNoHackEntities.Add(entityUid);
            }
            else if (!HasComp<NoHackComponent>(entityUid))
            {
                EnsureComp<NoHackComponent>(entityUid);
                _noHackOwners[entityUid] = 1;
                component.AddedNoHackEntities.Add(entityUid);
            }
        }

        if (deconProtect)
        {
            if (_noDeconstructOwners.TryGetValue(entityUid, out var owners))
            {
                _noDeconstructOwners[entityUid] = owners + 1;
                component.AddedNoDeconstructEntities.Add(entityUid);
            }
            else if (!HasComp<NoDeconstructComponent>(entityUid))
            {
                EnsureComp<NoDeconstructComponent>(entityUid);
                _noDeconstructOwners[entityUid] = 1;
                component.AddedNoDeconstructEntities.Add(entityUid);
            }
        }

        component.ProtectedEntities.Add(entityUid);
    }

    private void RemoveProtection(EntityUid entityUid, GridRaiderComponent component, bool removeComponents)
    {
        if (component.AddedNoHackEntities.Remove(entityUid))
            ReleaseProtection<NoHackComponent>(entityUid, _noHackOwners, removeComponents);

        if (component.AddedNoDeconstructEntities.Remove(entityUid))
            ReleaseProtection<NoDeconstructComponent>(entityUid, _noDeconstructOwners, removeComponents);
    }

    private void ReleaseProtection<T>(
        EntityUid entityUid,
        Dictionary<EntityUid, int> owners,
        bool removeComponent)
        where T : Component
    {
        if (!owners.TryGetValue(entityUid, out var count))
            return;

        if (count > 1)
        {
            owners[entityUid] = count - 1;
            return;
        }

        owners.Remove(entityUid);
        if (removeComponent && HasComp<T>(entityUid))
            RemComp<T>(entityUid);
    }

    private void ClearTransientState()
    {
        _refreshAccumulator = 0f;
        _refreshQueue.Clear();
        _queuedGrids.Clear();
        _intersectingEntities.Clear();
        _staleProtectedEntities.Clear();
        _noHackOwners.Clear();
        _noDeconstructOwners.Clear();
    }
}
