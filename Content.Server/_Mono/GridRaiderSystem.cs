using System.Linq;
using Content.Shared._Mono;
using Content.Shared._Mono.NoHack;
using Content.Shared._Mono.NoDeconstruct;
using Content.Server.Construction.Components;
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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridRaiderComponent, MapInitEvent>(OnGridRaiderMapInit);
        SubscribeLocalEvent<GridRaiderComponent, ComponentStartup>(OnGridRaiderStartup);
        SubscribeLocalEvent<GridRaiderComponent, ComponentShutdown>(OnGridRaiderShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _refreshAccumulator += frameTime;
        if (_refreshAccumulator < RefreshInterval)
            return;

        _refreshAccumulator = 0f;
        var grids = EntityQueryEnumerator<GridRaiderComponent>();
        while (grids.MoveNext(out var grid, out var component))
            RefreshGridProtection(grid, component);
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
        foreach (var entity in component.ProtectedEntities.ToList())
        {
            if (EntityManager.EntityExists(entity))
                RemoveProtection(entity, component);
        }

        component.ProtectedEntities.Clear();
        component.AddedNoHackEntities.Clear();
        component.AddedNoDeconstructEntities.Clear();
    }

    private void RefreshGridProtection(EntityUid grid, GridRaiderComponent component)
    {
        ApplyInitialProtection(grid, component);

        foreach (var entity in component.ProtectedEntities.ToList())
        {
            if (!Exists(entity))
            {
                component.ProtectedEntities.Remove(entity);
                component.AddedNoHackEntities.Remove(entity);
                component.AddedNoDeconstructEntities.Remove(entity);
                continue;
            }

            if (TryComp<TransformComponent>(entity, out var transform) && transform.GridUid == grid)
                continue;

            component.ProtectedEntities.Remove(entity);
            RemoveProtection(entity, component);
        }
    }

    private void ApplyInitialProtection(EntityUid gridUid, GridRaiderComponent component)
    {
        foreach (var entity in _lookup.GetEntitiesIntersecting(gridUid).ToHashSet())
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

        if (hackProtect && !HasComp<NoHackComponent>(entityUid))
        {
            EnsureComp<NoHackComponent>(entityUid);
            component.AddedNoHackEntities.Add(entityUid);
        }

        if (deconProtect && !HasComp<NoDeconstructComponent>(entityUid))
        {
            EnsureComp<NoDeconstructComponent>(entityUid);
            component.AddedNoDeconstructEntities.Add(entityUid);
        }

        component.ProtectedEntities.Add(entityUid);
    }

    private void RemoveProtection(EntityUid entityUid, GridRaiderComponent component)
    {
        if (component.AddedNoHackEntities.Remove(entityUid) && HasComp<NoHackComponent>(entityUid))
            RemComp<NoHackComponent>(entityUid);

        if (component.AddedNoDeconstructEntities.Remove(entityUid) && HasComp<NoDeconstructComponent>(entityUid))
            RemComp<NoDeconstructComponent>(entityUid);
    }
}
