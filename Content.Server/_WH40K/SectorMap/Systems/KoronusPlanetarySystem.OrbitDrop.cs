using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusPlanetarySystem
{
    [Dependency] private InventorySystem _dropInventory = default!;
    [Dependency] private RoofSystem _orbitRoof = default!;

    private static readonly EntProtoId ParachuteCanopy = "KoronusParachuteDeployed";
    private const float ParachuteFallSpeed = 0.75f;

    private void InitializeOrbitDrops()
    {
        SubscribeLocalEvent<MobStateComponent, EntParentChangedMessage>(OnOrbitMobParentChanged);
        SubscribeLocalEvent<KoronusOrbitFallComponent, CEGetZVelocityEvent>(OnOrbitFallVelocity);
        SubscribeLocalEvent<KoronusOrbitFallComponent, CEZLevelMapMoveEvent>(OnOrbitFallMapMove);
        SubscribeLocalEvent<KoronusOrbitFallComponent, ComponentShutdown>(OnOrbitFallShutdown);
    }

    private void OnOrbitMobParentChanged(Entity<MobStateComponent> mob, ref EntParentChangedMessage args)
    {
        var xform = args.Transform;
        if (xform.MapUid is not { } orbit || xform.ParentUid != orbit ||
            !TryComp<KoronusLowOrbitMapComponent>(orbit, out var orbitMap) ||
            !TryComp<MapComponent>(orbitMap.Surface, out var surface) ||
            !TryComp<MapComponent>(orbitMap.Atmosphere, out var atmosphere) ||
            !HasComp<PhysicsComponent>(mob) || HasComp<KoronusOrbitFallComponent>(mob) || TerminatingOrDeleted(mob))
        {
            return;
        }

        // Refresh the floor cache before the first CE tick. The air map has no ground at this coordinate.
        var addedZPhysics = !HasComp<CEZPhysicsComponent>(mob);
        var physics = EnsureComp<CEZPhysicsComponent>(mob);
        var fall = AddComp<KoronusOrbitFallComponent>(mob);
        fall.Orbit = orbit;
        fall.Atmosphere = orbitMap.Atmosphere;
        fall.Surface = orbitMap.Surface;
        fall.AddedZPhysics = addedZPhysics;
        fall.HadVelocityEvent = physics.VelocityRaiseEvent;
        physics.VelocityRaiseEvent = true;
        _maps.SetPaused(surface.MapId, false);
        _maps.SetPaused(atmosphere.MapId, false);

        _zLevels.RefreshBody((mob.Owner, physics));
        _zLevels.SetZPosition((mob.Owner, physics), -0.02f);
        _zLevels.SetZVelocity((mob.Owner, physics), 0f);
    }

    private void OnOrbitFallVelocity(Entity<KoronusOrbitFallComponent> entity, ref CEGetZVelocityEvent args)
    {
        if (!_dropInventory.TryGetSlotEntity(entity, "back", out var item) ||
            !HasComp<KoronusParachuteComponent>(item))
        {
            if (entity.Comp.Canopy is { } canopy)
                QueueDel(canopy);
            entity.Comp.Canopy = null;
            return;
        }

        entity.Comp.Canopy ??= SpawnAttachedTo(ParachuteCanopy, new EntityCoordinates(entity, Vector2.Zero));
        args.Target.Comp.Velocity = MathF.Max(args.Target.Comp.Velocity, -ParachuteFallSpeed);
    }

    private void OnOrbitFallMapMove(Entity<KoronusOrbitFallComponent> entity, ref CEZLevelMapMoveEvent args)
    {
        var xform = Transform(entity);
        if (xform.MapUid != entity.Comp.Surface ||
            !TryComp<KoronusPlanetSurfaceMapComponent>(entity.Comp.Surface, out var surface))
        {
            return;
        }

        var pos = _transform.GetWorldPosition(xform);
        var center = new Vector2(MathF.Floor(pos.X) + 0.5f, MathF.Floor(pos.Y) + 0.5f);
        for (var radius = 0; radius <= 5; radius++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                for (var y = -radius; y <= radius; y++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius)
                        continue;

                    var candidate = center + new Vector2(x, y);
                    if (!surface.PlayableBounds.Contains(candidate) ||
                        !_mapSystem.TryFindGridAt(xform.MapID, candidate, out var gridUid, out var grid) ||
                        (gridUid != surface.TerrainGrid && !surface.AuthoredBaseGrids.Contains(gridUid)) ||
                        !_mapSystem.TryGetTileRef(gridUid, grid, candidate, out var tile) || tile.Tile.IsEmpty ||
                        _turf.IsTileBlocked(tile, CollisionGroup.MobMask) ||
                        TryComp<RoofComponent>(gridUid, out var roof) && _orbitRoof.IsRooved((gridUid, grid, roof), tile.GridIndices))
                    {
                        continue;
                    }

                    if (radius != 0)
                        _transform.SetCoordinates(entity, xform, _turf.GetTileCenter(tile));
                    return;
                }
            }
        }

        if (TryFindAtmosphericFallCoordinates(xform.MapID, surface, out var fallback))
            _transform.SetCoordinates(entity, xform, fallback);
    }

    private void UpdateOrbitDrops()
    {
        var falling = EntityQueryEnumerator<KoronusOrbitFallComponent, CEZPhysicsComponent, TransformComponent>();
        while (falling.MoveNext(out var uid, out var fall, out var physics, out var xform))
        {
            if (xform.MapUid == fall.Surface && physics.LocalPosition > 0.02f ||
                xform.MapUid == fall.Atmosphere ||
                xform.MapUid == fall.Orbit && xform.ParentUid == fall.Orbit)
            {
                continue;
            }

            RemCompDeferred<KoronusOrbitFallComponent>(uid);
        }
    }

    private void OnOrbitFallShutdown(Entity<KoronusOrbitFallComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.Canopy is { } canopy)
            QueueDel(canopy);

        if (TerminatingOrDeleted(entity) || !TryComp<CEZPhysicsComponent>(entity, out var physics))
            return;

        if (entity.Comp.AddedZPhysics)
            RemCompDeferred<CEZPhysicsComponent>(entity);
        else
            physics.VelocityRaiseEvent = entity.Comp.HadVelocityEvent;
    }
}
