using System.Collections.Generic;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WH40K.ChaosDungeon;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WH40K.SectorMap.BUI;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._DV.Planet;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusPlanetarySystem
{
    private const float PlanetaryViewShrink = 0.72f;
    private static readonly TimeSpan LowOrbitTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LowOrbitCheckInterval = TimeSpan.FromSeconds(1);

    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private AtmosphereSystem _orbitAtmosphere = default!;

    private TimeSpan _nextLowOrbitCheck;
    private readonly HashSet<Entity<MobStateComponent>> _lowOrbitOccupants = new();

    private void RegisterLowOrbit(
        EntityUid surfaceMap,
        KoronusPlanetSurfaceMapComponent runtime,
        KoronusPlanetSurfacePrototype surface)
    {
        if (!surface.LowOrbitEnabled || runtime.LowOrbit != null || surface.Planet is not { } planetId ||
            !TryResolveBodyForSurface(surface.ID, out var body) || body.ID == ChaosDungeonSystem.BodyId)
        {
            return;
        }

        var planet = _prototypes.Index(planetId);
        var planetName = MetaData(surfaceMap).EntityName;
        var atmosphere = CreateAirMap(planet, Loc.GetString("koronus-planetary-atmosphere-map", ("planet", planetName)),
            out var atmosphereId);
        var orbit = CreateAirMap(planet, Loc.GetString("koronus-planetary-nav-orbit-map", ("planet", planetName)),
            out var orbitId);

        var identity = AddComp<KoronusLowOrbitMapComponent>(orbit);
        identity.BodyId = body.ID;
        identity.Surface = surfaceMap;
        identity.Atmosphere = atmosphere;

        var network = _zLevels.CreateMapNetwork();
        var levels = new Dictionary<EntityUid, int>
        {
            [surfaceMap] = 0,
            [atmosphere] = 1,
            [orbit] = KoronusPlanetaryInterfaceState.LowOrbitDepth,
        };
        _zLevels.TryAddMapsIntoNetwork(network, levels);

        foreach (var map in levels.Keys)
        {
            var zMap = Comp<CEZMapComponent>(map);
            zMap.ControlledGridAltitude = true;
            zMap.ViewShrink = PlanetaryViewShrink;
            Dirty(map, zMap);
        }

        var visualBoundary = EnsureComp<KoronusPlanetSurfaceBoundaryComponent>(orbit);
        visualBoundary.Minimum = runtime.PlayableBounds.BottomLeft;
        visualBoundary.Maximum = runtime.PlayableBounds.TopRight;
        _surfaceBoundaries[orbitId] = new SurfaceBoundary(orbit, runtime.PlayableBounds, orbit);
        runtime.LowOrbit = orbit;
        runtime.Atmosphere = atmosphere;
        _maps.SetPaused(atmosphereId, true);
        _maps.SetPaused(orbitId, true);
    }

    private EntityUid CreateAirMap(PlanetPrototype planet, string name, out MapId mapId)
    {
        var map = _maps.CreateMap(out mapId, runMapInit: false);
        _metaData.SetEntityName(map, name);
        AddComp<KoronusPlanetaryLayerComponent>(map);

        var gravity = EnsureComp<GravityComponent>(map);
        gravity.Enabled = true;
        gravity.Inherent = true;

        EnsureComp<MapLightComponent>(map).AmbientLightColor = planet.MapLight;
        _orbitAtmosphere.SetMapAtmosphere(map, false, planet.Atmosphere);
        return map;
    }

    private void OnLowOrbitFtlAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (!args.Cancelled && HasComp<KoronusLowOrbitMapComponent>(Transform(args.Uid).MapUid))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("koronus-planetary-orbit-ftl-denied");
        }
    }

    private void OnLowOrbitMapShutdown(Entity<KoronusLowOrbitMapComponent> entity, ref ComponentShutdown args)
    {
        if (TryComp<MapComponent>(entity, out var map))
            _surfaceBoundaries.Remove(map.MapId);
        if (TryComp<KoronusPlanetSurfaceMapComponent>(entity.Comp.Surface, out var surface))
            surface.LowOrbit = null;
    }

    private void OnOrbitShuttleParentChanged(Entity<ShuttleComponent> entity, ref EntParentChangedMessage args)
    {
        if (args.OldMapId == args.Transform.MapUid || TerminatingOrDeleted(entity))
            return;

        RemComp<KoronusLowOrbitShuttleComponent>(entity);
        if (HasComp<KoronusLowOrbitMapComponent>(args.Transform.MapUid))
            EnsureComp<KoronusLowOrbitShuttleComponent>(entity);
    }

    private void UpdateLowOrbitTimeouts()
    {
        var now = _timing.CurTime;
        if (now < _nextLowOrbitCheck)
            return;

        _nextLowOrbitCheck = now + LowOrbitCheckInterval;
        // Residency can pause a deserted orbit long before these ships are due to leave.
        var shuttles = AllEntityQuery<KoronusLowOrbitShuttleComponent, ShuttleComponent, TransformComponent>();
        while (shuttles.MoveNext(out var shuttle, out var timer, out _, out var transform))
        {
            if (!TryComp<KoronusLowOrbitMapComponent>(transform.MapUid, out var orbit) ||
                HasComp<KoronusPlanetaryTransitComponent>(shuttle) ||
                HasLowOrbitOccupants(shuttle))
            {
                timer.LastUnoccupiedCheck = null;
                continue;
            }

            if (timer.LastUnoccupiedCheck is { } lastCheck)
                timer.UnoccupiedTime += now - lastCheck;
            timer.LastUnoccupiedCheck = now;

            if (timer.UnoccupiedTime >= LowOrbitTimeout)
                TryLeaveLowOrbit(shuttle, orbit, out _, automatic: true);
        }
    }

    private bool HasLowOrbitOccupants(EntityUid shuttle)
    {
        _lowOrbitOccupants.Clear();
        _lookup.GetGridEntities(shuttle, _lowOrbitOccupants);
        foreach (var mob in _lowOrbitOccupants)
        {
            if (mob.Comp.CurrentState != MobState.Dead && !TerminatingOrDeleted(mob))
                return true;
        }

        return false;
    }

    private bool TryGetLowOrbit(MapId mapId, out KoronusLowOrbitMapComponent orbit)
    {
        if (_maps.TryGetMap(mapId, out var map) && TryComp<KoronusLowOrbitMapComponent>(map.Value, out var found))
        {
            orbit = found;
            return true;
        }

        orbit = default!;
        return false;
    }

    public bool TryEnterLowOrbit(EntityUid shuttleGrid, string bodyId, out KoronusPlanetaryTransferFailure failure)
    {
        var source = Transform(shuttleGrid).MapID;
        failure = KoronusPlanetaryTransferFailure.InvalidTarget;
        if (TryGetSurfaceRuntime(source, out _, out var landed))
        {
            return TryResolveBodyForSurface(landed.SurfaceId, out var landedBody) && landedBody.ID == bodyId &&
                   TryLaunch(shuttleGrid, out failure, toLowOrbit: true);
        }

        if (!TryGetSectorRule(out var rule) ||
            !_prototypes.TryIndex<KoronusCelestialBodyPrototype>(bodyId, out var body) ||
            body.Surface is not { } surfaceId ||
            !_prototypes.TryIndex(surfaceId, out KoronusPlanetSurfacePrototype? surface) ||
            !_sector.TryGetSystemId(source, out var currentSystem) || currentSystem != body.System.Id ||
            !_sector.TryGetSystemPrototype(currentSystem, out var system) ||
            !rule.Comp.SurfaceMaps.TryGetValue(surfaceId.Id, out var surfaceMap) ||
            !TryGetSurfaceRuntime(surfaceMap, out _, out var runtime) ||
            runtime.LowOrbit is not { } orbit || !TryComp<MapComponent>(orbit, out var orbitMap))
        {
            return false;
        }

        if (!IsWithinLandingApproach(shuttleGrid, system, body))
        {
            failure = KoronusPlanetaryTransferFailure.OutsideLandingApproach;
            return false;
        }

        if (!CanTransfer(shuttleGrid, out failure))
            return false;

        if (!TryFindLowOrbitOrigin(shuttleGrid, orbitMap.MapId, runtime, runtime.PlayableBounds.Center, out var origin))
        {
            failure = KoronusPlanetaryTransferFailure.LandingAreaBlocked;
            return false;
        }

        CancelShuttleAutopilot(shuttleGrid);
        _maps.SetPaused(orbitMap.MapId, false);
        BeginTransit(shuttleGrid, true, orbitMap.MapId, origin, string.Empty,
            surface.TransitStartupTime, surface.LandingTransitTime, surface.TransitArrivalTime,
            lowOrbitBodyId: body.ID);
        return true;
    }

    private bool TryLeaveLowOrbit(
        EntityUid shuttleGrid,
        KoronusLowOrbitMapComponent orbit,
        out KoronusPlanetaryTransferFailure failure,
        bool automatic = false)
    {
        failure = KoronusPlanetaryTransferFailure.InvalidTarget;
        if (!TryGetSectorRule(out var rule) ||
            !_prototypes.TryIndex<KoronusCelestialBodyPrototype>(orbit.BodyId, out var body) ||
            body.Surface is not { } surfaceId ||
            !_prototypes.TryIndex(surfaceId, out KoronusPlanetSurfacePrototype? surface) ||
            !_sector.TryGetSystemPrototype(body.System, out var system) ||
            !_sector.TryGetSystemMap(system.ID, out var target) ||
            !_maps.TryGetMap(target, out var targetMap))
        {
            return false;
        }

        if (!CanTransfer(shuttleGrid, out failure, allowDocked: automatic))
            return false;

        if (!TryGetOrbitalLaunchOrigin(shuttleGrid, system, body, surface, out var origin, target))
        {
            failure = KoronusPlanetaryTransferFailure.InvalidLaunchPosition;
            return false;
        }

        if (automatic)
        {
            // Include anyone standing on a shuttle tile whose transform still belongs to the map.
            AttachShuttleOccupants(shuttleGrid);
            if (HasLowOrbitOccupants(shuttleGrid))
                return false;

            _docking.UndockDocks(shuttleGrid);
            if (!CanTransfer(shuttleGrid, out failure))
                return false;

            CancelShuttleAutopilot(shuttleGrid);
            _maps.SetPaused(target, false);
            MoveGrid(shuttleGrid, targetMap.Value, origin);
            FinishDeparture(rule, shuttleGrid, body.ID, string.Empty);
            EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles(shuttleGrid);
        }
        else
        {
            _maps.SetPaused(target, false);
            BeginTransit(shuttleGrid, false, target, origin, string.Empty,
                surface.TransitStartupTime, surface.LaunchTransitTime, surface.TransitArrivalTime, body.ID);
        }

        return true;
    }

    private bool TryFindLowOrbitOrigin(
        EntityUid shuttleGrid,
        MapId orbit,
        KoronusPlanetSurfaceMapComponent surface,
        Vector2 preferredCenter,
        out Vector2 origin)
    {
        origin = default;
        if (!TryComp<MapGridComponent>(shuttleGrid, out var shuttle))
            return false;

        var bounds = surface.PlayableBounds;
        var half = shuttle.LocalAABB.Size / 2f;
        var min = bounds.BottomLeft + half;
        var max = bounds.TopRight - half;
        if (min.X > max.X || min.Y > max.Y)
            return false;

        var centerBounds = new Box2(min, max);
        return TryFindClearOrbitOrigin(orbit, shuttle.LocalAABB, Vector2.Clamp(preferredCenter, min, max),
            candidate => centerBounds.Contains(candidate), out origin);
    }

    private bool TryFindClearOrbitOrigin(
        MapId map,
        Box2 shuttleBounds,
        Vector2 center,
        Func<Vector2, bool> isCenterAllowed,
        out Vector2 origin)
    {
        var grids = new List<Entity<MapGridComponent>>();
        for (var ring = 0; ring <= 3; ring++)
        {
            var distance = (shuttleBounds.Size + new Vector2(4f)) * ring;
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (ring == 0 && (x != 0 || y != 0) || ring != 0 && x == 0 && y == 0)
                        continue;

                    var candidate = center + new Vector2(x * distance.X, y * distance.Y);
                    if (!isCenterAllowed(candidate))
                        continue;

                    var candidateOrigin = candidate - shuttleBounds.Center;
                    grids.Clear();
                    _mapSystem.FindGridsIntersecting(map, shuttleBounds.Translated(candidateOrigin).Enlarged(LandingPadding),
                        ref grids, approx: true, includeMap: false);
                    if (grids.Count == 0)
                    {
                        origin = candidateOrigin;
                        return true;
                    }
                }
            }
        }

        origin = default;
        return false;
    }
}
