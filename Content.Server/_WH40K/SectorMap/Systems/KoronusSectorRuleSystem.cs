using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Systems;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.BUI;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared._Mono;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Maps;
using Content.Shared.Tiles;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Replaces the round's main map with Footfall and preloads every configured remote system on a paused map.
/// </summary>
public sealed class KoronusSectorRuleSystem : GameRuleSystem<KoronusSectorRuleComponent>
{
    private const int AngularPlacementAttempts = 360;
    private const float ObjectPlacementPadding = 300f;

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private KoronusPlanetarySystem _planetary = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IResourceManager _resources = default!;

    private readonly Dictionary<string, HashSet<string>> _warpAdjacency = new();
    private readonly List<KoronusRoutePrototype> _sectorRoutes = new();
    private ResPath _coldSnapshotRoot = CreateColdSnapshotRoot();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LoadingMapsEvent>(OnLoadingMaps);
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Retrieves a loaded system map for future FTL, boundary and residency systems.
    /// </summary>
    public bool TryGetSystemMap(string systemId, out MapId mapId)
    {
        mapId = MapId.Nullspace;
        return TryGetSectorRule(out var rule) && rule.Comp.SystemMaps.TryGetValue(systemId, out mapId);
    }

    /// <summary>
    /// Ensures that a cold-unloaded system has a live map again before a shuttle or another
    /// authoritative server operation attempts to use its retained <see cref="MapId"/>.
    /// </summary>
    public bool TryEnsureSystemMapLoaded(string systemId, out MapId mapId)
    {
        mapId = MapId.Nullspace;
        if (!TryGetSectorRule(out var rule) ||
            !rule.Comp.SystemMaps.TryGetValue(systemId, out mapId))
        {
            return false;
        }

        if (_maps.TryGetMap(mapId, out _))
            return true;

        if (!rule.Comp.ColdUnloadedSystems.Contains(systemId) ||
            !_prototypes.TryIndex<KoronusSystemPrototype>(systemId, out var system))
        {
            return false;
        }

        var snapshotPath = GetColdSnapshotPath(systemId);
        var options = DeserializationOptions.Default with
        {
            InitializeMaps = true,
            PauseMaps = true,
        };

        try
        {
            if (!_mapLoader.TryLoadMapWithId(mapId, snapshotPath, out var loadedMap, out _, options))
            {
                Log.Error($"Failed to restore cold Koronus system {systemId} from {snapshotPath}.");
                return false;
            }

            _metaData.SetEntityName(loadedMap.Value.Owner, system.DisplayName);
            ConfigureSystemMap(loadedMap.Value.Owner, system);
            rule.Comp.ColdUnloadedSystems.Remove(systemId);
            _resources.UserData.Delete(snapshotPath);
            Log.Info($"Restored cold Koronus system {systemId} on map {mapId}.");
            return true;
        }
        catch (Exception exception)
        {
            Log.Error($"Exception while restoring cold Koronus system {systemId}: {exception}");
            return false;
        }
    }

    /// <summary>
    /// Serializes a paused, empty remote system and removes its live map. The same map id is
    /// retained in the sector index and will be reused by <see cref="TryEnsureSystemMapLoaded"/>.
    /// A failed snapshot never deletes the live map.
    /// </summary>
    public bool TryColdUnloadSystem(string systemId)
    {
        if (!TryGetSectorRule(out var rule) ||
            !rule.Comp.SystemMaps.TryGetValue(systemId, out var mapId) ||
            !_prototypes.TryIndex<KoronusSystemPrototype>(systemId, out var system) ||
            !system.AllowColdUnload ||
            system.ColdUnloadDelay <= 0f ||
            _prototypes.Index(rule.Comp.Sector).StartSystem.Id == systemId ||
            !_maps.TryGetMap(mapId, out _) ||
            !_maps.IsPaused(mapId))
        {
            return false;
        }

        var snapshotPath = GetColdSnapshotPath(systemId);
        if (!_mapLoader.TrySaveMap(mapId, snapshotPath))
        {
            Log.Error($"Cold snapshot failed for Koronus system {systemId}; the live map was kept paused.");
            return false;
        }

        rule.Comp.ColdUnloadedSystems.Add(systemId);
        _maps.DeleteMap(mapId);
        Log.Info($"Cold-unloaded Koronus system {systemId} from map {mapId} into {snapshotPath}.");
        return true;
    }

    /// <summary>
    /// Registers a map created by the Koronus bootstrap. Keeping the mutation here makes the runtime
    /// map index usable by other server systems without exposing the rule component for writes.
    /// </summary>
    public void RegisterSystemMap(Entity<KoronusSectorRuleComponent> rule, string systemId, MapId mapId)
    {
        rule.Comp.SystemMaps[systemId] = mapId;
    }

    /// <summary>
    /// Resolves the authored system that owns a loaded runtime map.
    /// </summary>
    public bool TryGetSystemId(MapId mapId, out string systemId)
    {
        if (TryGetSectorRule(out var rule))
        {
            foreach (var (id, loadedMap) in rule.Comp.SystemMaps)
            {
                if (loadedMap == mapId)
                {
                    systemId = id;
                    return true;
                }
            }
        }

        systemId = string.Empty;
        return false;
    }

    /// <summary>
    /// Resolves a preloaded planetary surface map. Surface maps are intentionally kept separate
    /// from orbital system maps so their safety profile can cover the whole landing world.
    /// </summary>
    public bool TryGetSurfaceId(MapId mapId, out string surfaceId)
    {
        if (TryGetSectorRule(out var rule))
        {
            foreach (var (id, loadedMap) in rule.Comp.SurfaceMaps)
            {
                if (loadedMap == mapId)
                {
                    surfaceId = id;
                    return true;
                }
            }
        }

        surfaceId = string.Empty;
        return false;
    }

    /// <summary>
    /// Checks the fixed authored warp graph. Routes are directional unless explicitly marked bidirectional.
    /// </summary>
    public bool HasWarpRoute(string fromSystem, string toSystem)
    {
        if (!TryGetSectorRule(out var rule))
            return false;

        EnsureTopology(rule.Comp.Sector);
        return _warpAdjacency.TryGetValue(fromSystem, out var destinations) && destinations.Contains(toSystem);
    }

    /// <summary>
    /// Produces only presentation data. Navigation requests are always validated again on the server.
    /// </summary>
    public KoronusSectorInterfaceState GetInterfaceState(MapId currentMap, bool canJump)
    {
        if (!TryGetSectorRule(out var rule) || !TryGetSystemId(currentMap, out var currentSystem))
            return KoronusSectorInterfaceState.Unavailable();

        return GetInterfaceState(currentSystem, canJump);
    }

    /// <summary>
    /// Produces presentation data while the shuttle is on the technical FTL map.
    /// <paramref name="currentSystem"/> remains the departure system until arrival completes.
    /// </summary>
    public KoronusSectorInterfaceState GetInterfaceState(
        string currentSystem,
        bool canJump,
        KoronusSectorTravelState? warpTravel = null)
    {
        if (!TryGetSectorRule(out var rule))
            return KoronusSectorInterfaceState.Unavailable();

        EnsureTopology(rule.Comp.Sector);

        var systems = new List<KoronusSectorNodeState>();
        foreach (var system in _prototypes.EnumeratePrototypes<KoronusSystemPrototype>())
        {
            if (system.Sector != rule.Comp.Sector)
                continue;

            var isCurrent = system.ID == currentSystem;
            var available = IsSystemAvailable(rule.Comp, system.ID);
            var reachable = !isCurrent && available && system.Enabled && HasWarpRoute(currentSystem, system.ID);
            systems.Add(new KoronusSectorNodeState(
                system.ID,
                system.DisplayName,
                system.UiPosition,
                system.Enabled && available,
                isCurrent,
                reachable));
        }

        systems.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        var routes = new List<KoronusSectorRouteState>();
        foreach (var route in _sectorRoutes)
        {
            var available = IsSystemAvailable(rule.Comp, route.From) &&
                            IsSystemAvailable(rule.Comp, route.To);
            routes.Add(new KoronusSectorRouteState(route.From, route.To, route.RouteClass, route.Enabled && available));
        }

        return new KoronusSectorInterfaceState(true, canJump, currentSystem, systems, routes, warpTravel);
    }

    /// <summary>
    /// Resolves an authored multiplier for the FTL travel phase, falling back to the distance between
    /// two systems on the normalized sector map when the route does not override it.
    /// </summary>
    public float GetWarpTravelTimeMultiplier(string originSystem, string destinationSystem)
    {
        if (TryGetSectorRule(out var rule))
        {
            EnsureTopology(rule.Comp.Sector);
            foreach (var route in _sectorRoutes)
            {
                var matchesDirection = route.From == originSystem && route.To == destinationSystem;
                var matchesReverseDirection = route.Bidirectional &&
                                             route.From == destinationSystem &&
                                             route.To == originSystem;
                if (!route.Enabled || (!matchesDirection && !matchesReverseDirection))
                    continue;

                if (route.TravelTimeMultiplier is > 0f)
                    return route.TravelTimeMultiplier.Value;
            }
        }

        if (!TryGetSystemPrototype(originSystem, out var origin) ||
            !TryGetSystemPrototype(destinationSystem, out var destination))
        {
            return 1.5f;
        }

        var distance = Vector2.Distance(origin.UiPosition, destination.UiPosition);
        return Math.Clamp(1.3f + distance * 10f, 1.5f, 4.5f);
    }

    /// <summary>
    /// Resolves the authored configuration of a loaded Koronus system.
    /// </summary>
    public bool TryGetSystemPrototype(string systemId, out KoronusSystemPrototype system)
    {
        if (_prototypes.TryIndex<KoronusSystemPrototype>(systemId, out var prototype))
        {
            system = prototype;
            return true;
        }

        system = default!;
        return false;
    }

    /// <summary>
    /// Returns the fixed authored angle or the server-selected per-round angle for a randomized
    /// static body. All authoritative position checks go through this method.
    /// </summary>
    public float GetCelestialBodyPositionAngle(
        KoronusSystemPrototype system,
        KoronusCelestialBodyPrototype body)
    {
        if (TryGetSystemMap(system.ID, out var mapId) &&
            _maps.TryGetMap(mapId, out var mapUid) &&
            TryComp<KoronusPlanetarySystemVisualComponent>(mapUid.Value, out var visual) &&
            visual.PositionAngleOverrides.TryGetValue(body.ID, out var angle))
        {
            return angle;
        }

        return body.OrbitPhase;
    }

    private void OnLoadingMaps(LoadingMapsEvent ev)
    {
        if (!TryGetSectorRule(out var rule))
            return;

        var sector = _prototypes.Index(rule.Comp.Sector);

        // DefaultMap is deliberately only Footfall. Every other system receives its own MapId below.
        ev.Maps.Clear();
        ev.Maps.Add(_prototypes.Index<GameMapPrototype>(sector.StartGameMap));

        rule.Comp.WaitingForStartMap = true;
        rule.Comp.SystemMaps.Clear();
        rule.Comp.ColdUnloadedSystems.Clear();
        RebuildTopology(rule.Comp.Sector);
    }

    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        if (!TryGetSectorRule(out var rule) || !rule.Comp.WaitingForStartMap)
            return;

        var sector = _prototypes.Index(rule.Comp.Sector);
        if (ev.GameMap.ID != sector.StartGameMap)
            return;

        var startSystem = _prototypes.Index(sector.StartSystem);
        RegisterSystemMap(rule, startSystem.ID, ev.Map);
        if (_maps.TryGetMap(ev.Map, out var startMap))
        {
            ConfigureStartSystemGrids(startMap.Value, ev.Grids, startSystem);
            _metaData.SetEntityName(startMap.Value, startSystem.DisplayName);
            ConfigureSystemMap(startMap.Value, startSystem);
        }
        else
            Log.Error($"Could not resolve Footfall map {ev.Map} for Koronus boundary setup.");

        rule.Comp.WaitingForStartMap = false;

        foreach (var system in _prototypes.EnumeratePrototypes<KoronusSystemPrototype>())
        {
            if (!system.Enabled || system.Sector != rule.Comp.Sector || system.ID == startSystem.ID)
                continue;

            LoadPausedSystem(rule, system);
        }

        _planetary.PreloadSurfaces(rule);
    }

    private void LoadPausedSystem(Entity<KoronusSectorRuleComponent> rule, KoronusSystemPrototype system)
    {
        var mapUid = _maps.CreateMap(out var mapId);
        var options = DeserializationOptions.Default with
        {
            InitializeMaps = true,
            PauseMaps = true,
        };

        Entity<MapGridComponent>? grid = null;
        if (system.InitialGridPath != null)
        {
            if (!_mapLoader.TryLoadGrid(mapId, system.InitialGridPath.Value, out var loadedGrid, options))
            {
                QueueDel(mapUid);
                Log.Error($"Failed to load Koronus system {system.ID} from {system.InitialGridPath}.");
                return;
            }

            grid = loadedGrid;
            if (system.InitialGridSpawnDistance > 0f)
            {
                var halfDiagonal = GetGridHalfDiagonal(grid.Value.Comp);
                var position = GetSafeInitialGridSpawnPosition(system, halfDiagonal);
                _transform.SetCoordinates(grid.Value.Owner, new EntityCoordinates(mapUid, position));
            }

            ConfigureInitialGrid(grid.Value.Owner, system);
        }

        _metaData.SetEntityName(mapUid, system.DisplayName);
        if (grid != null)
            _metaData.SetEntityName(grid.Value.Owner, GetInitialGridDisplayName(system));
        ConfigureSystemMap(mapUid, system);
        _maps.SetPaused(mapId, true);
        RegisterSystemMap(rule, system.ID, mapId);
    }

    internal static Vector2 GetInitialGridSpawnPosition(KoronusSystemPrototype system, Angle angle)
    {
        var distance = Math.Max(0f, system.InitialGridSpawnDistance);
        return system.NavigationCenter + angle.ToWorldVec() * distance;
    }

    private void ConfigureStartSystemGrids(
        EntityUid mapUid,
        IReadOnlyList<EntityUid> grids,
        KoronusSystemPrototype system)
    {
        var loadedGrids = new List<(EntityUid Grid, Vector2 Position)>();
        foreach (var grid in grids)
        {
            if (TerminatingOrDeleted(grid) || Transform(grid).MapID == MapId.Nullspace)
                continue;

            loadedGrids.Add((grid, _transform.GetWorldPosition(Transform(grid))));
        }

        if (loadedGrids.Count == 0)
            return;

        if (system.InitialGridSpawnDistance > 0f)
        {
            var facilityRadius = 0f;
            foreach (var (grid, position) in loadedGrids)
            {
                if (!TryComp<MapGridComponent>(grid, out var mapGrid))
                    continue;

                facilityRadius = MathF.Max(
                    facilityRadius,
                    Vector2.Distance(loadedGrids[0].Position, position) + GetGridHalfDiagonal(mapGrid));
            }

            var destination = GetSafeInitialGridSpawnPosition(system, facilityRadius);
            var translation = destination - loadedGrids[0].Position;
            foreach (var (grid, position) in loadedGrids)
                _transform.SetCoordinates(grid, new EntityCoordinates(mapUid, position + translation));
        }

        var gridName = GetInitialGridDisplayName(system);
        var renamedStations = new HashSet<EntityUid>();
        foreach (var (grid, _) in loadedGrids)
        {
            _metaData.SetEntityName(grid, gridName);
            if (_stations.GetOwningStation(grid) is { } station && renamedStations.Add(station))
                _stations.RenameStation(station, gridName, false);

            ConfigureInitialGrid(grid, system);
        }
    }

    /// <summary>
    /// Applies protection only to authored facility grids. Procedural terrain and generated
    /// asteroid content are loaded through different paths and never enter this method.
    /// </summary>
    private void ConfigureInitialGrid(EntityUid grid, KoronusSystemPrototype system)
    {
        if (system.InitialGridSafetyProfile is { } profile && system.InitialGridSafetyRadius > 0f)
        {
            var zone = EnsureComp<KoronusSafetyZoneComponent>(grid);
            zone.Profile = profile;
            zone.Radius = system.InitialGridSafetyRadius;
            zone.ShowBoundary = true;
            Dirty(grid, zone);
        }

        if (!system.ProtectInitialGrid)
            return;

        var protectedGrid = EnsureComp<ProtectedGridComponent>(grid);
        protectedGrid.PreventFloorRemoval = true;
        protectedGrid.PreventFloorPlacement = true;
        protectedGrid.PreventRCDUse = true;
        protectedGrid.PreventEmpEvents = true;
        protectedGrid.PreventExplosions = true;

        EnsureComp<GridGodModeComponent>(grid);
        EnsureComp<GridRaiderComponent>(grid);
    }

    private static string GetInitialGridDisplayName(KoronusSystemPrototype system)
    {
        return string.IsNullOrWhiteSpace(system.InitialGridDisplayName)
            ? system.DisplayName
            : system.InitialGridDisplayName;
    }

    private Vector2 GetSafeInitialGridSpawnPosition(KoronusSystemPrototype system, float facilityRadius)
    {
        var startingAngle = _random.NextFloat(0f, 360f);
        for (var attempt = 0; attempt < AngularPlacementAttempts; attempt++)
        {
            var angle = (startingAngle + attempt) % 360f;
            var position = GetInitialGridSpawnPosition(system, Angle.FromDegrees(angle));
            if (IsInitialGridPositionClear(system, position, facilityRadius))
                return position;
        }

        Log.Warning($"No collision-free initial-grid angle was found in system {system.ID}; using the authored random fallback.");
        return GetInitialGridSpawnPosition(system, Angle.FromDegrees(startingAngle));
    }

    private bool IsInitialGridPositionClear(
        KoronusSystemPrototype system,
        Vector2 position,
        float facilityRadius)
    {
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            // Random bodies are placed after the facility and will avoid it themselves.
            if (body.System != system.ID || body.RandomizePositionAngle)
                continue;

            var bodyPosition = GetCelestialBodyPosition(system, body, body.OrbitPhase);
            var clearance = facilityRadius + GetBodyPlacementRadius(body) + ObjectPlacementPadding;
            if (Vector2.DistanceSquared(position, bodyPosition) <= clearance * clearance)
                return false;
        }

        return true;
    }

    internal void ConfigureSystemMap(EntityUid mapUid, KoronusSystemPrototype system)
    {
        var runtime = EnsureComp<KoronusSystemMapComponent>(mapUid);
        runtime.SystemId = system.ID;

        KoronusPlanetarySystemVisualComponent? visual = null;
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.System != system.ID)
                continue;

            visual ??= EnsureComp<KoronusPlanetarySystemVisualComponent>(mapUid);
            if (body.RandomizePositionAngle && !visual.PositionAngleOverrides.ContainsKey(body.ID))
                visual.PositionAngleOverrides[body.ID] = GetRandomBodyPositionAngle(mapUid, system, body, visual);
        }

        if (visual != null)
        {
            visual.SystemId = system.ID;
            Dirty(mapUid, visual);
        }

        var boundary = EnsureComp<KoronusSystemBoundaryComponent>(mapUid);
        boundary.Origin = system.NavigationCenter;
        boundary.Radius = system.BoundaryRadius;
        boundary.WarningFraction = system.WarningFraction;
        boundary.CleanupDelay = system.CleanupDelay;
        boundary.WarningAnnouncementCooldown = system.WarningAnnouncementCooldown;
        Dirty(mapUid, boundary);

    }

    private float GetRandomBodyPositionAngle(
        EntityUid mapUid,
        KoronusSystemPrototype system,
        KoronusCelestialBodyPrototype body,
        KoronusPlanetarySystemVisualComponent visual)
    {
        var mapId = Comp<MapComponent>(mapUid).MapId;
        var startingAngle = _random.NextFloat(0f, 360f);
        for (var attempt = 0; attempt < AngularPlacementAttempts; attempt++)
        {
            var angle = (startingAngle + attempt) % 360f;
            var position = GetCelestialBodyPosition(system, body, angle);
            var clear = true;

            foreach (var grid in _mapManager.GetAllGrids(mapId))
            {
                var transform = Transform(grid.Owner);
                var gridOrigin = _transform.GetWorldPosition(transform);
                var gridRotation = _transform.GetWorldRotation(transform);
                var gridCenter = gridOrigin + gridRotation.RotateVec(grid.Comp.LocalAABB.Center);
                var clearance = GetBodyPlacementRadius(body) +
                                GetGridHalfDiagonal(grid.Comp) +
                                ObjectPlacementPadding;
                if (Vector2.DistanceSquared(position, gridCenter) >= clearance * clearance)
                    continue;

                clear = false;
                break;
            }

            if (!clear)
                continue;

            foreach (var otherBody in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
            {
                if (otherBody.System != system.ID || otherBody.ID == body.ID)
                    continue;

                float otherAngle;
                if (otherBody.RandomizePositionAngle)
                {
                    if (!visual.PositionAngleOverrides.TryGetValue(otherBody.ID, out otherAngle))
                        continue;
                }
                else
                {
                    otherAngle = otherBody.OrbitPhase;
                }

                var otherPosition = GetCelestialBodyPosition(system, otherBody, otherAngle);
                var clearance = GetBodyPlacementRadius(body) +
                                GetBodyPlacementRadius(otherBody) +
                                ObjectPlacementPadding;
                if (Vector2.DistanceSquared(position, otherPosition) >= clearance * clearance)
                    continue;

                clear = false;
                break;
            }

            if (clear)
                return angle;
        }

        Log.Warning($"No collision-free random angle was found for celestial body {body.ID}; using the random fallback.");
        return startingAngle;
    }

    private static Vector2 GetCelestialBodyPosition(
        KoronusSystemPrototype system,
        KoronusCelestialBodyPrototype body,
        float positionAngle)
    {
        if (body.OrbitRadius <= 0f)
            return system.NavigationCenter;

        var radians = positionAngle * MathF.PI / 180f;
        return system.NavigationCenter +
               new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * body.OrbitRadius;
    }

    private static float GetBodyPlacementRadius(KoronusCelestialBodyPrototype body)
    {
        return MathF.Max(MathF.Max(0f, body.NavVisualRadius), MathF.Max(0f, body.LandingApproachRadius));
    }

    private static float GetGridHalfDiagonal(MapGridComponent grid)
    {
        return (grid.LocalAABB.TopRight - grid.LocalAABB.Center).Length();
    }

    private bool IsSystemAvailable(KoronusSectorRuleComponent rule, string systemId)
    {
        return rule.SystemMaps.TryGetValue(systemId, out var mapId) &&
               (_maps.TryGetMap(mapId, out _) || rule.ColdUnloadedSystems.Contains(systemId));
    }

    private ResPath GetColdSnapshotPath(string systemId)
    {
        return _coldSnapshotRoot / $"{systemId}.yml";
    }

    private static ResPath CreateColdSnapshotRoot()
    {
        return new ResPath($"/Koronus/ColdSnapshots/{Guid.NewGuid():N}");
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _resources.UserData.Delete(_coldSnapshotRoot);
        _coldSnapshotRoot = CreateColdSnapshotRoot();

        if (!TryGetSectorRule(out var rule))
            return;

        rule.Comp.WaitingForStartMap = true;
        rule.Comp.SystemMaps.Clear();
        rule.Comp.SurfaceMaps.Clear();
        rule.Comp.ColdUnloadedSystems.Clear();
        rule.Comp.LandingReservations.Clear();
        _warpAdjacency.Clear();
        _sectorRoutes.Clear();
    }

    private void EnsureTopology(ProtoId<KoronusSectorPrototype> sector)
    {
        if (_sectorRoutes.Count > 0)
            return;

        RebuildTopology(sector);
    }

    private void RebuildTopology(ProtoId<KoronusSectorPrototype> sector)
    {
        _warpAdjacency.Clear();
        _sectorRoutes.Clear();

        foreach (var route in _prototypes.EnumeratePrototypes<KoronusRoutePrototype>())
        {
            if (route.Sector != sector)
                continue;

            _sectorRoutes.Add(route);
            if (!route.Enabled)
                continue;

            AddWarpRoute(route.From, route.To);
            if (route.Bidirectional)
                AddWarpRoute(route.To, route.From);
        }
    }

    private void AddWarpRoute(string fromSystem, string toSystem)
    {
        if (!_warpAdjacency.TryGetValue(fromSystem, out var destinations))
        {
            destinations = new HashSet<string>();
            _warpAdjacency[fromSystem] = destinations;
        }

        destinations.Add(toSystem);
    }

    private bool TryGetSectorRule(out Entity<KoronusSectorRuleComponent> rule)
    {
        var query = EntityQueryEnumerator<KoronusSectorRuleComponent>();
        if (query.MoveNext(out var uid, out var component))
        {
            rule = (uid, component);
            return true;
        }

        rule = default;
        return false;
    }
}
