using System.Numerics;
using Content.Server._WH40K.ChaosDungeon;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Content.Server._NF.Shuttles.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusPlanetarySystem
{
    private bool TryResolveDungeonLanding(
        EntityUid ship,
        ref LandingResolution landing,
        out KoronusPlanetaryTransferFailure failure)
    {
        failure = KoronusPlanetaryTransferFailure.None;
        if (landing.Body.ID != ChaosDungeonSystem.BodyId)
            return true;

        var dungeons = EntityManager.System<ChaosDungeonSystem>();
        if (!dungeons.TryPrepareLanding(ship, out var map, out failure))
            return false;

        var mapId = Comp<MapComponent>(map).MapId;
        var runtime = Comp<Components.KoronusPlanetSurfaceMapComponent>(map);
        if (!TryGetLandingSite(runtime, landing.Body, ChaosDungeonSystem.DungeonLandingSiteId,
                out var site, idIsReservationKey: false))
        {
            dungeons.CancelPreparedLanding(ship);
            failure = KoronusPlanetaryTransferFailure.InvalidTarget;
            return false;
        }

        landing = landing with { SurfaceMap = mapId, SurfaceMapUid = map, SurfaceRuntime = runtime, Site = site };
        return true;
    }

    /// <summary>Loads a private surface without replacing the planet's public landing template.</summary>
    public EntityUid? CreateDungeonSurface()
    {
        if (!TryGetSectorRule(out var rule) ||
            !_prototypes.TryIndex<KoronusPlanetSurfacePrototype>("AivoriusBurningSurface", out var surface))
            return null;

        var map = _planetMaps.LoadPlanet("AivoriusBurningWorldSurface", surface.MapPath);
        if (map == null)
            return null;

        var mapId = Comp<MapComponent>(map.Value).MapId;
        // The three northern blast doors are at (9.5..11.5, 48.5). The pad ends at y=-62;
        // keep both grids aligned to whole tiles so the approach has no strip of exposed lava.
        if (!_mapLoader.TryLoadGrid(mapId, new ResPath("/Maps/_WH40K/Chaos-1.yml"), out var dungeon,
                new DeserializationOptions { InitializeMaps = true }, new Vector2(-10f, -116f)))
        {
            _maps.DeleteMap(mapId);
            return null;
        }

        if (TryComp<PhysicsComponent>(dungeon.Value, out var physics))
        {
            _physics.SetBodyType(dungeon.Value, BodyType.Static, body: physics);
            _physics.SetFixedRotation(dungeon.Value, true, body: physics);
        }
        EnsureComp<PreventGridAnchorChangesComponent>(dungeon.Value);
        EnsureComp<PreventPilotComponent>(dungeon.Value);
        PrepareLandingClearance(map.Value, surface);
        foreach (var grid in _mapManager.GetAllGrids(mapId))
        {
            if (grid.Owner == map.Value)
                continue;
            var bounds = grid.Comp.LocalAABB.Translated(Transform(grid.Owner).LocalPosition).Enlarged(1f);
            PrepareLandingClearance(map.Value, surface, bounds);
        }
        AnchorPlanetaryInfrastructure(map.Value);
        RegisterSurfaceMap(rule, surface.ID, "Aivorius", mapId, map.Value, persistent: false);
        _maps.SetPaused(mapId, true);
        return map;
    }

    /// <summary>Finishes any atmospheric transfer and releases the normal parking locks.</summary>
    public bool ReturnDungeonShip(ChaosDungeonInstanceComponent instance)
    {
        var ship = instance.SourceShip;
        if (TerminatingOrDeleted(ship) || !_maps.TryGetMap(instance.SourceMapId, out var source))
            return false;

        if (TryComp<Components.KoronusPlanetaryTransitComponent>(ship, out var transit))
        {
            StopTransitAudio(transit);
            CompleteTransit(ship, transit);
        }

        _maps.SetPaused(instance.SourceMapId, false);
        MoveGrid(ship, source.Value, instance.SourcePosition, instance.SourceAngle);
        if (TryGetSectorRule(out var rule) && TryGetReservation(rule, ship, out var reservation))
            FinishDeparture(rule, ship, ChaosDungeonSystem.BodyId, reservation);
        return Transform(ship).MapID != instance.MapId;
    }
}

public readonly record struct PlanetaryLandingCompletedEvent(EntityUid Ship, MapId Surface);
public readonly record struct PlanetaryDepartureCompletedEvent(EntityUid Ship);
public readonly record struct PlanetaryTransferCancelledEvent(EntityUid Ship);
