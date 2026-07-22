using System.Numerics;
using System.Linq;
using Content.Server._DV.Planet;
using Content.Server._Mono.NPC.HTN;
using Content.Server._Mono.Shuttles.Components;
using Content.Server.NPC.HTN;
using Content.Server.Parallax;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.BUI;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Owns preloaded planetary maps and performs controlled, non-docking atmospheric transfers
/// between an orbital system and a reserved surface landing site.
/// </summary>
public sealed class KoronusPlanetarySystem : EntitySystem
{
    private const float LandingPadding = 1f;
    private const float BoundaryPadding = 1f;
    private const float LandingPositionTolerance = 0.1f;
    private const float MaxGeneratedPlanetSceneryBuffer = 25f;
    private const float AtmosphericTravelSpeed = 20f;
    private const float AtmosphericTransitSpacing = 100f;
    private const float MaxAtmosphericTransitCoordinate = 20000f;
    private const int AtmosphericFallDamage = 200;
    private const float AtmosphericFallLandingSitePadding = 2f;
    private static readonly TimeSpan LandingCooldown = TimeSpan.FromMinutes(1);

    private readonly SoundSpecifier _atmosphericStartupSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_begin.ogg")
    {
        Params = AudioParams.Default.WithVolume(-5f),
    };

    private readonly SoundSpecifier _atmosphericTravelSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_progress.ogg")
    {
        Params = AudioParams.Default.WithVolume(-3f).WithLoop(true),
    };

    private readonly SoundSpecifier _atmosphericArrivalSound = new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_end.ogg")
    {
        Params = AudioParams.Default.WithVolume(-5f),
    };

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private PlanetSystem _planetMaps = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private ShipSteeringSystem _steering = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private KoronusLandingPadSystem _landingPads = default!;
    [Dependency] private ShuttleConsoleLockSystem _shipAccess = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private TurfSystem _turf = default!;

    private TimeSpan _nextBoundaryUpdate;
    private TimeSpan _nextBoundaryTrackingRefresh;
    private TimeSpan _nextParkingUpdate;
    private TimeSpan _nextReservationPrune;
    private TimeSpan _nextApproachRefresh;
    private static readonly TimeSpan BoundaryUpdateInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan BoundaryTrackingRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ParkingUpdateInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReservationPruneInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ApproachRefreshInterval = TimeSpan.FromSeconds(1);
    private EntityUid _atmosphericTransitMap = EntityUid.Invalid;
    private float _nextAtmosphericTransitCoordinate;
    private readonly Dictionary<EntityUid, LandingApproachState> _landingApproachStates = new();
    private readonly Dictionary<MapId, SurfaceBoundary> _surfaceBoundaries = new();
    private readonly HashSet<Entity<ShuttleConsoleComponent>> _shuttleConsoles = new();
    private readonly List<EntityUid> _atmosphericTransitMobRemovals = new();
    private readonly List<KoronusLandingSession> _parkingSessionSnapshot = new();
    private readonly List<string> _reservationRemovals = new();
    private readonly HashSet<EntityUid> _activeApproachShuttles = new();
    private readonly List<EntityUid> _landingApproachRemovals = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<KoronusLandedShuttleFragmentComponent, GridSplitEvent>(OnLandedGridSplit);
        SubscribeLocalEvent<KoronusLandedShuttleFragmentComponent, EntityTerminatingEvent>(OnLandedShuttleTerminating);
        SubscribeLocalEvent<PhysicsComponent, EntParentChangedMessage>(OnPhysicsParentChanged);
        SubscribeLocalEvent<KoronusSurfaceBoundaryTrackedComponent, MoveEvent>(OnTrackedEntityMoved);
        SubscribeLocalEvent<KoronusPlanetSurfaceMapComponent, ComponentShutdown>(OnSurfaceMapShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdatePlanetaryTransfers();
        if (_timing.CurTime >= _nextParkingUpdate)
        {
            _nextParkingUpdate = _timing.CurTime + ParkingUpdateInterval;
            UpdateParkingSessions();
        }

        if (_timing.CurTime < _nextBoundaryUpdate)
            return;

        _nextBoundaryUpdate = _timing.CurTime + BoundaryUpdateInterval;
        EnforceSurfaceBoundaries();
        RefreshSurfaceBoundaryTracking();

        if (_timing.CurTime >= _nextReservationPrune)
        {
            _nextReservationPrune = _timing.CurTime + ReservationPruneInterval;
            PruneStaleReservations();
        }

        if (_timing.CurTime >= _nextApproachRefresh)
        {
            _nextApproachRefresh = _timing.CurTime + ApproachRefreshInterval;
            RefreshLandingApproachStateChanges();
        }
    }

    /// <summary>
    /// Called once after the sector's orbital maps have been created. Surfaces are loaded before a
    /// player clicks land, but remain paused until a shuttle actually arrives.
    /// </summary>
    public void PreloadSurfaces(Entity<KoronusSectorRuleComponent> rule)
    {
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.Surface == null ||
                !_sector.TryGetSystemPrototype(body.System, out var system) ||
                system.Sector != rule.Comp.Sector)
            {
                continue;
            }

            var surface = _prototypes.Index(body.Surface.Value);
            if (!surface.PreloadOnRoundStart || rule.Comp.SurfaceMaps.ContainsKey(surface.ID))
                continue;

            LoadSurface(rule, system, surface);
        }
    }

    /// <summary>
    /// Validates and performs a landing from the current planetary-system map. The caller gives only
    /// prototype ids; all maps, coordinates and ownership are resolved by the server.
    /// </summary>
    public bool TryLand(
        EntityUid shuttleGrid,
        string celestialBodyId,
        string landingSiteId,
        out KoronusPlanetaryTransferFailure failure,
        EntityUid? user = null)
    {
        if (!TryResolveLanding(shuttleGrid, celestialBodyId, landingSiteId, user, out var landing, out failure))
            return false;

        var (rule, body, surface, site, surfaceMap, surfaceMapUid, surfaceRuntime) = landing;
        var reservationKey = GetReservationKey(body.ID, site.Id);
        if (rule.Comp.LandingReservations.TryGetValue(reservationKey, out var occupant) && occupant != shuttleGrid)
        {
            failure = KoronusPlanetaryTransferFailure.LandingSiteOccupied;
            return false;
        }

        if (!TryGetLandingOrigin(shuttleGrid, site, out var landingOrigin, out failure) ||
            !IsLandingAreaClear(shuttleGrid, surfaceMap, surfaceRuntime.TerrainGrid, landingOrigin))
        {
            failure = failure == KoronusPlanetaryTransferFailure.None
                ? KoronusPlanetaryTransferFailure.LandingAreaBlocked
                : failure;
            return false;
        }

        rule.Comp.LandingReservations[reservationKey] = shuttleGrid;
        _maps.SetPaused(surfaceMap, false);
        CancelShuttleAutopilot(shuttleGrid);

        if (surface.LandingTransitTime > 0f)
            BeginTransit(
                shuttleGrid,
                true,
                surfaceMap,
                landingOrigin,
                reservationKey,
                surface.TransitStartupTime,
                surface.LandingTransitTime,
                surface.TransitArrivalTime);
        else
        {
            MoveGrid(shuttleGrid, surfaceMapUid, landingOrigin);
            BeginParkingSession(
                rule,
                shuttleGrid,
                body,
                surface,
                site,
                surfaceRuntime.TerrainGrid,
                reservationKey);
        }

        failure = KoronusPlanetaryTransferFailure.None;
        return true;
    }

    /// <summary>
    /// Returns a landed shuttle to the orbital map of the celestial body that owns its surface.
    /// Only the current reservation holder may launch.
    /// </summary>
    public bool TryLaunch(EntityUid shuttleGrid, out KoronusPlanetaryTransferFailure failure)
    {
        failure = KoronusPlanetaryTransferFailure.NotLanded;
        if (!TryGetSurfaceRuntime(Transform(shuttleGrid).MapID, out var surfaceMapUid, out var surfaceRuntime) ||
            !TryGetSectorRule(out var rule) ||
            !TryGetReservation(rule, shuttleGrid, out var reservationKey) ||
            !TryResolveBodyForSurface(surfaceRuntime.SurfaceId, out var body) ||
            body.Surface == null ||
            !_prototypes.TryIndex(body.Surface.Value, out KoronusPlanetSurfacePrototype? surface) ||
            !_sector.TryGetSystemPrototype(body.System, out var system) ||
            !_sector.TryEnsureSystemMapLoaded(system.ID, out var orbitalMap) ||
            !_maps.TryGetMap(orbitalMap, out var orbitalMapUid))
        {
            return false;
        }

        if (!CanTransfer(shuttleGrid, out failure))
            return false;

        TryGetLandingSession(rule, shuttleGrid, out var activeSession);
        if (activeSession != null && activeSession.Fragments.Count > 1)
        {
            failure = KoronusPlanetaryTransferFailure.ShuttleFragmented;
            return false;
        }

        ResolvedLandingSite site;
        if (activeSession != null)
        {
            site = new ResolvedLandingSite(
                activeSession.ReservationKey,
                string.Empty,
                activeSession.LandingPosition,
                activeSession.LandingSize,
                true,
                true,
                null,
                null,
                null,
                0);
        }
        else if (!TryGetLandingSite(surfaceRuntime, body, reservationKey, out site))
        {
            return false;
        }

        if (!IsOnLandingSite(shuttleGrid, site))
        {
            failure = KoronusPlanetaryTransferFailure.NotOnLandingSite;
            return false;
        }

        if (!TryGetOrbitalLaunchOrigin(shuttleGrid, system, body, surface, out var launchOrigin))
        {
            failure = KoronusPlanetaryTransferFailure.InvalidLaunchPosition;
            return false;
        }

        // The surface can have been re-paused while an empty landed shuttle was parked there.
        // Its transit component must still tick through the startup phase before it moves away.
        CancelShuttleAutopilot(shuttleGrid);
        _maps.SetPaused(Transform(shuttleGrid).MapID, false);
        _maps.SetPaused(orbitalMap, false);
        if (surface.LaunchTransitTime > 0f)
        {
            if (activeSession != null)
                activeSession.Launching = true;
            BeginTransit(
                shuttleGrid,
                false,
                orbitalMap,
                launchOrigin,
                reservationKey,
                surface.TransitStartupTime,
                surface.LaunchTransitTime,
                surface.TransitArrivalTime,
                body.ID);
        }
        else
        {
            if (activeSession != null)
                ReleaseParkingSession(activeSession);
            MoveGrid(shuttleGrid, orbitalMapUid.Value, launchOrigin);
            FinishDeparture(rule, shuttleGrid, body.ID, reservationKey);
        }

        failure = KoronusPlanetaryTransferFailure.None;
        return true;
    }

    public bool TryGetSurfaceMap(string surfaceId, out MapId mapId)
    {
        mapId = MapId.Nullspace;
        return TryGetSectorRule(out var rule) && rule.Comp.SurfaceMaps.TryGetValue(surfaceId, out mapId);
    }

    /// <summary>
    /// Registers a successfully loaded persistent surface map. This is shared by round bootstrap and
    /// integration setup so both paths use the same server-owned runtime identity.
    /// </summary>
    public void RegisterSurfaceMap(
        Entity<KoronusSectorRuleComponent> rule,
        string surfaceId,
        string systemId,
        MapId mapId,
        EntityUid terrainGrid)
    {
        if (!_maps.TryGetMap(mapId, out var mapUid))
            return;

        var runtime = EnsureComp<KoronusPlanetSurfaceMapComponent>(mapUid.Value);
        runtime.SurfaceId = surfaceId;
        runtime.SystemId = systemId;
        runtime.TerrainGrid = terrainGrid;
        if (_prototypes.TryIndex<KoronusPlanetSurfacePrototype>(surfaceId, out var surface))
        {
            runtime.PlayableBounds = GetPlayableBounds(surface);
            runtime.GenerationBounds = GetGenerationBounds(surface);

            var visualBoundary = EnsureComp<KoronusPlanetSurfaceBoundaryComponent>(mapUid.Value);
            visualBoundary.Minimum = new Vector2(runtime.PlayableBounds.Left, runtime.PlayableBounds.Bottom);
            visualBoundary.Maximum = new Vector2(runtime.PlayableBounds.Right, runtime.PlayableBounds.Top);
            Dirty(mapUid.Value, visualBoundary);

            // PlanetSystem keeps its biome directly on the map entity. Limit the biome itself,
            // instead of relocating terrain when a shuttle reaches the gameplay perimeter.
            if (HasComp<BiomeComponent>(terrainGrid))
            {
                var generationBounds = EnsureComp<BiomeGenerationBoundsComponent>(terrainGrid);
                generationBounds.Bounds = runtime.GenerationBounds;
            }
        }

        rule.Comp.SurfaceMaps[surfaceId] = mapId;
        _surfaceBoundaries[mapId] = new SurfaceBoundary(mapUid.Value, runtime.PlayableBounds, terrainGrid);
        SeedSurfaceBoundaryTracking(mapId);
    }

    /// <summary>
    /// Builds presentation data for one shuttle console. Map identities, surface coordinates and
    /// reservations of other shuttles remain server-side.
    /// </summary>
    public KoronusPlanetaryInterfaceState GetInterfaceState(EntityUid shuttleGrid)
    {
        if (!TryGetSectorRule(out var rule))
            return KoronusPlanetaryInterfaceState.Unavailable();

        if (TryComp<KoronusPlanetaryTransitComponent>(shuttleGrid, out var transit) &&
            TryBuildTransitInterfaceState(rule, shuttleGrid, transit, out var transitState))
        {
            return transitState;
        }

        var shuttleMap = Transform(shuttleGrid).MapID;
        if (_sector.TryGetSystemId(shuttleMap, out var systemId) &&
            _sector.TryGetSystemPrototype(systemId, out var orbitalSystem) &&
            HasLandableBodies(orbitalSystem.ID))
        {
            var canLand = CanTransfer(shuttleGrid, out _);
            return BuildInterfaceState(rule, orbitalSystem, shuttleGrid, canLand, false, null);
        }

        if (!TryGetSurfaceRuntime(shuttleMap, out _, out var surfaceRuntime) ||
            !_sector.TryGetSystemPrototype(surfaceRuntime.SystemId, out var surfaceSystem) ||
            !TryResolveBodyForSurface(surfaceRuntime.SurfaceId, out var landedBody))
        {
            return KoronusPlanetaryInterfaceState.Unavailable();
        }

        var canLaunch = !HasComp<KoronusPlanetaryTransitComponent>(shuttleGrid) &&
                        TryGetReservation(rule, shuttleGrid, out _);
        return BuildInterfaceState(rule, surfaceSystem, shuttleGrid, false, canLaunch, landedBody.ID);
    }

    private void LoadSurface(
        Entity<KoronusSectorRuleComponent> rule,
        KoronusSystemPrototype system,
        KoronusPlanetSurfacePrototype surface)
    {
        EntityUid mapUid;
        MapId mapId;
        EntityUid terrainGrid;

        if (surface.Planet != null)
        {
            var planetMap = _planetMaps.LoadPlanet(surface.Planet.Value, surface.MapPath);
            if (planetMap == null)
            {
                Log.Error($"Failed to load Koronus planetary surface {surface.ID} from {surface.MapPath}.");
                return;
            }

            mapUid = planetMap.Value;
            mapId = Comp<MapComponent>(mapUid).MapId;
            terrainGrid = mapUid;
            PrepareLandingClearance(mapUid, surface);
            AnchorPlanetaryInfrastructure(mapUid);
        }
        else
        {
            mapUid = _maps.CreateMap(out mapId);
            var options = DeserializationOptions.Default with
            {
                InitializeMaps = true,
                PauseMaps = true,
            };

            if (!_mapLoader.TryLoadGrid(mapId, surface.MapPath, out var loadedGrid, options))
            {
                QueueDel(mapUid);
                Log.Error($"Failed to load Koronus planetary surface {surface.ID} from {surface.MapPath}.");
                return;
            }

            terrainGrid = loadedGrid.Value.Owner;
        }

        _metaData.SetEntityName(mapUid, surface.ID);
        if (!string.IsNullOrWhiteSpace(surface.Parallax))
        {
            var parallax = EnsureComp<ParallaxComponent>(mapUid);
            parallax.Parallax = surface.Parallax;
            Dirty(mapUid, parallax);
        }

        _maps.SetPaused(mapId, true);
        RegisterSurfaceMap(rule, surface.ID, system.ID, mapId, terrainGrid);
    }

    private bool TryResolveLanding(
        EntityUid shuttleGrid,
        string celestialBodyId,
        string landingSiteId,
        EntityUid? user,
        out LandingResolution landing,
        out KoronusPlanetaryTransferFailure failure)
    {
        landing = default;
        failure = KoronusPlanetaryTransferFailure.InvalidTarget;
        if (!TryGetSectorRule(out var rule) ||
            !_prototypes.TryIndex<KoronusCelestialBodyPrototype>(celestialBodyId, out var body) ||
            body.Surface == null ||
            !_prototypes.TryIndex(body.Surface.Value, out KoronusPlanetSurfacePrototype? surface) ||
            !_sector.TryGetSystemPrototype(body.System, out var system) ||
            !_sector.TryGetSystemId(Transform(shuttleGrid).MapID, out var currentSystem) ||
            currentSystem != system.ID ||
            !rule.Comp.SurfaceMaps.TryGetValue(surface.ID, out var surfaceMap) ||
            !_maps.TryGetMap(surfaceMap, out var surfaceMapUid) ||
            !TryComp<KoronusPlanetSurfaceMapComponent>(surfaceMapUid.Value, out var surfaceRuntime))
        {
            return false;
        }

        if (!TryGetLandingSite(surfaceRuntime, body, landingSiteId, out var site, idIsReservationKey: false) ||
            !site.Enabled)
            return false;

        if (!site.PublicAccess && (user == null || !_shipAccess.HasShipAccess(shuttleGrid, user.Value)))
        {
            failure = KoronusPlanetaryTransferFailure.AccessDenied;
            return false;
        }

        if (!IsWithinLandingApproach(shuttleGrid, system, body))
        {
            failure = KoronusPlanetaryTransferFailure.OutsideLandingApproach;
            return false;
        }

        if (TryComp<KoronusPlanetaryLandingCooldownComponent>(shuttleGrid, out var cooldown))
        {
            if (_timing.CurTime >= cooldown.Until)
                RemCompDeferred<KoronusPlanetaryLandingCooldownComponent>(shuttleGrid);
            else if (cooldown.BodyId == body.ID)
            {
                failure = KoronusPlanetaryTransferFailure.LandingCooldown;
                return false;
            }
        }

        if (!CanTransfer(shuttleGrid, out failure))
            return false;

        landing = new LandingResolution(rule, body, surface, site, surfaceMap, surfaceMapUid.Value, surfaceRuntime);
        return true;
    }

    private List<ResolvedLandingSite> GetLandingSites(KoronusPlanetSurfaceMapComponent runtime)
    {
        return _landingPads.GetPads(runtime.TerrainGrid)
            .Select(pad => new ResolvedLandingSite(
                    pad.Id,
                    pad.Name,
                    pad.Position,
                    pad.Size,
                    pad.Component.Enabled && _landingPads.IsPowered(pad.Console),
                    pad.Component.PublicAccess,
                    pad.Tiles,
                    pad.TileEntities,
                    pad.Console,
                    pad.Component.ParkingTime))
            .ToList();
    }

    private bool TryGetLandingSite(
        KoronusPlanetSurfaceMapComponent runtime,
        KoronusCelestialBodyPrototype body,
        string id,
        out ResolvedLandingSite site,
        bool idIsReservationKey = true)
    {
        foreach (var candidate in GetLandingSites(runtime))
        {
            var candidateId = idIsReservationKey
                ? GetReservationKey(body.ID, candidate.Id)
                : candidate.Id;
            if (candidateId != id)
                continue;

            site = candidate;
            return true;
        }

        site = default!;
        return false;
    }

    private bool CanTransfer(EntityUid shuttleGrid, out KoronusPlanetaryTransferFailure failure)
    {
        failure = KoronusPlanetaryTransferFailure.None;
        if (HasComp<KoronusPlanetaryTransitComponent>(shuttleGrid))
        {
            failure = KoronusPlanetaryTransferFailure.TransferInProgress;
            return false;
        }

        if (!HasComp<MapGridComponent>(shuttleGrid))
        {
            failure = KoronusPlanetaryTransferFailure.InvalidShuttle;
            return false;
        }

        if (TryComp<FTLComponent>(shuttleGrid, out var ftl) && ftl.State != FTLState.Available)
        {
            failure = KoronusPlanetaryTransferFailure.ShuttleInFtl;
            return false;
        }

        foreach (var dock in _docking.GetDocks(shuttleGrid))
        {
            if (dock.Comp.DockedWith != null)
            {
                failure = KoronusPlanetaryTransferFailure.ShuttleDocked;
                return false;
            }
        }

        return true;
    }

    private bool TryGetLandingOrigin(
        EntityUid shuttleGrid,
        ResolvedLandingSite site,
        out Vector2 origin,
        out KoronusPlanetaryTransferFailure failure)
    {
        origin = Vector2.Zero;
        failure = KoronusPlanetaryTransferFailure.InvalidShuttle;
        if (!TryComp<MapGridComponent>(shuttleGrid, out var grid))
            return false;

        var padding = site.Tiles == null ? LandingPadding : 0f;
        var available = site.Size - new Vector2(padding * 2f);
        var size = grid.LocalAABB.Size;
        if (available.X <= 0f || available.Y <= 0f || size.X > available.X || size.Y > available.Y)
        {
            failure = KoronusPlanetaryTransferFailure.ShuttleTooLarge;
            return false;
        }

        origin = site.Position - grid.LocalAABB.Center;
        if (site.Tiles != null)
        {
            var shuttleBounds = grid.LocalAABB.Translated(origin);
            var minimum = new Vector2i(
                (int) MathF.Floor(shuttleBounds.Left),
                (int) MathF.Floor(shuttleBounds.Bottom));
            var maximum = new Vector2i(
                (int) MathF.Ceiling(shuttleBounds.Right),
                (int) MathF.Ceiling(shuttleBounds.Top));
            for (var x = minimum.X; x < maximum.X; x++)
            {
                for (var y = minimum.Y; y < maximum.Y; y++)
                {
                    if (site.Tiles.Contains(new Vector2i(x, y)))
                        continue;

                    failure = KoronusPlanetaryTransferFailure.ShuttleTooLarge;
                    return false;
                }
            }
        }

        failure = KoronusPlanetaryTransferFailure.None;
        return true;
    }

    private bool IsLandingAreaClear(EntityUid shuttleGrid, MapId surfaceMap, EntityUid terrainGrid, Vector2 origin)
    {
        if (!TryComp<MapGridComponent>(shuttleGrid, out var shuttle))
            return false;

        var bounds = shuttle.LocalAABB.Translated(origin);
        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(surfaceMap, bounds, ref grids, includeMap: false);
        return grids.All(grid => grid.Owner == terrainGrid || grid.Owner == shuttleGrid);
    }

    private bool TryGetOrbitalLaunchOrigin(
        EntityUid shuttleGrid,
        KoronusSystemPrototype system,
        KoronusCelestialBodyPrototype body,
        KoronusPlanetSurfacePrototype surface,
        out Vector2 origin)
    {
        origin = Vector2.Zero;
        if (!TryComp<MapGridComponent>(shuttleGrid, out var grid))
            return false;

        var halfDiagonal = (grid.LocalAABB.TopRight - grid.LocalAABB.Center).Length();
        var safeRadius = system.BoundaryRadius - MathF.Max(BoundaryPadding, halfDiagonal);
        if (safeRadius <= 0f)
            return false;

        var bodyPosition = GetCelestialPosition(system, body);
        var outward = bodyPosition - system.NavigationCenter;
        if (outward.LengthSquared() < float.Epsilon)
            outward = Vector2.UnitY;
        else
            outward = Vector2.Normalize(outward);

        var center = bodyPosition + outward * MathF.Max(0f, surface.OrbitalLaunchDistance);
        var fromNavigationCenter = center - system.NavigationCenter;
        if (fromNavigationCenter.LengthSquared() > safeRadius * safeRadius)
            center = system.NavigationCenter + Vector2.Normalize(fromNavigationCenter) * safeRadius;

        origin = center - grid.LocalAABB.Center;
        return true;
    }

    private bool IsOnLandingSite(EntityUid shuttleGrid, ResolvedLandingSite site)
    {
        if (!HasComp<MapGridComponent>(shuttleGrid))
            return false;

        var bounds = _physics.GetWorldAABB(shuttleGrid);
        var siteHalfSize = site.Size / 2f;
        var siteBounds = new Box2(site.Position - siteHalfSize, site.Position + siteHalfSize);
        return bounds.Left >= siteBounds.Left - LandingPositionTolerance &&
               bounds.Right <= siteBounds.Right + LandingPositionTolerance &&
               bounds.Bottom >= siteBounds.Bottom - LandingPositionTolerance &&
               bounds.Top <= siteBounds.Top + LandingPositionTolerance;
    }

    private void MoveGrid(EntityUid shuttleGrid, EntityUid mapUid, Vector2 origin, Angle? angle = null)
    {
        _transform.SetCoordinates(shuttleGrid, new EntityCoordinates(mapUid, origin));
        _transform.SetWorldRotation(shuttleGrid, angle ?? Angle.Zero);

        if (TryComp<PhysicsComponent>(shuttleGrid, out var physics))
        {
            _physics.SetLinearVelocity(shuttleGrid, Vector2.Zero, body: physics);
            _physics.SetAngularVelocity(shuttleGrid, 0f, body: physics);
        }
    }

    private void PrepareLandingClearance(EntityUid mapUid, KoronusPlanetSurfacePrototype surface)
    {
        if (surface.LandingClearanceSize.X <= 0f || surface.LandingClearanceSize.Y <= 0f)
            return;

        var halfSize = surface.LandingClearanceSize / 2f;
        var bounds = new Box2(-halfSize, halfSize);
        var terrain = Comp<MapGridComponent>(mapUid);
        var biome = Comp<BiomeComponent>(mapUid);
        var tiles = new List<(Vector2i, Tile)>();

        // A just-created planet grid has no chunks yet. ReserveTiles intentionally walks existing
        // chunks, so materialise the clearing's natural terrain first, then mark every tile as
        // modified. This keeps the floor biome-authentic while permanently excluding all biome
        // entities and decals (trees, rocks, water and vegetation) from the landing field.
        var min = new Vector2i((int) MathF.Floor(bounds.Left), (int) MathF.Floor(bounds.Bottom));
        var max = new Vector2i((int) MathF.Ceiling(bounds.Right), (int) MathF.Ceiling(bounds.Top));
        for (var x = min.X; x < max.X; x++)
        {
            for (var y = min.Y; y < max.Y; y++)
            {
                var index = new Vector2i(x, y);
                if (_biome.TryGetBiomeTile(index, biome.Layers, biome.Seed, (mapUid, terrain), out var tile) &&
                    tile is { } biomeTile)
                {
                    tiles.Add((index, biomeTile));
                }
            }
        }

        _mapSystem.SetTiles(mapUid, terrain, tiles);
        _biome.ReserveTiles(mapUid, bounds, new List<(Vector2i, Tile)>());
    }

    /// <summary>
    /// Authored infrastructure is loaded before a procedural planet has materialised terrain.
    /// Its prototype requests anchoring, but the engine correctly refuses while no supporting tile
    /// exists. The landing clearance now has real tiles, so retry anchoring only our infrastructure.
    /// </summary>
    private void AnchorPlanetaryInfrastructure(EntityUid terrainGrid)
    {
        var grid = Comp<MapGridComponent>(terrainGrid);
        AnchorPlanetaryInfrastructure<KoronusLandingPadComponent>(terrainGrid, grid);
        AnchorPlanetaryInfrastructure<KoronusLandingPadConsoleComponent>(terrainGrid, grid);
        AnchorPlanetaryInfrastructure<KoronusPlanetaryTeleporterComponent>(terrainGrid, grid);
    }

    private void AnchorPlanetaryInfrastructure<T>(EntityUid terrainGrid, MapGridComponent grid)
        where T : IComponent
    {
        var query = EntityManager.AllEntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.Anchored || transform.GridUid != terrainGrid)
                continue;

            _transform.AnchorEntity((uid, transform), (terrainGrid, grid));
        }
    }

    private void BeginParkingSession(
        Entity<KoronusSectorRuleComponent> rule,
        EntityUid shuttleGrid,
        KoronusCelestialBodyPrototype body,
        KoronusPlanetSurfacePrototype surface,
        ResolvedLandingSite site,
        EntityUid terrainGrid,
        string reservationKey,
        bool takeTransitPilotLock = false)
    {
        if (TryGetLandingSession(rule, shuttleGrid, out _))
            return;

        var sessionId = rule.Comp.NextLandingSessionId++;
        var session = new KoronusLandingSession
        {
            Id = sessionId,
            BodyId = body.ID,
            SurfaceId = surface.ID,
            ReservationKey = reservationKey,
            LandingPosition = site.Position,
            LandingSize = site.Size,
            PrimaryGrid = shuttleGrid,
            LandedAt = _timing.CurTime,
            Deadline = site.ParkingTime > 0
                ? _timing.CurTime + TimeSpan.FromSeconds(site.ParkingTime)
                : null,
        };
        session.Fragments.Add(shuttleGrid);
        rule.Comp.LandingSessions[sessionId] = session;

        LockLandedFragment(shuttleGrid, sessionId, takeTransitPilotLock);
        CoverLandingPadUnderShuttle(shuttleGrid, terrainGrid, site, session);
        if (site.Console is { } console && TryComp<KoronusLandingPadConsoleComponent>(console, out var consoleComponent))
            _landingPads.UpdateUi((console, consoleComponent));
    }

    /// <summary>
    /// A parked shuttle is level geometry, not a free-flying rigid body. Preserve the exact physics
    /// state so unusual grids can still be restored correctly, and own only locks installed here.
    /// </summary>
    private void LockLandedFragment(
        EntityUid shuttleGrid,
        long sessionId,
        bool takePreventPilotOwnership = false,
        bool takeAnchorLockOwnership = false)
    {
        var fragment = EnsureComp<KoronusLandedShuttleFragmentComponent>(shuttleGrid);
        fragment.SessionId = sessionId;

        if (TryComp<PhysicsComponent>(shuttleGrid, out var physics))
        {
            if (!fragment.PhysicsLocked)
            {
                fragment.OriginalBodyType = physics.BodyType;
                fragment.OriginalBodyStatus = physics.BodyStatus;
                fragment.OriginalFixedRotation = physics.FixedRotation;
                fragment.PhysicsLocked = true;
            }

            StopPhysics(shuttleGrid);
            _physics.SetBodyType(shuttleGrid, BodyType.Static, body: physics);
            _physics.SetBodyStatus(shuttleGrid, physics, BodyStatus.OnGround);
            _physics.SetFixedRotation(shuttleGrid, true, body: physics);
        }

        if (!HasComp<PreventPilotComponent>(shuttleGrid))
        {
            EnsureComp<PreventPilotComponent>(shuttleGrid);
            fragment.AddedPreventPilot = true;
        }
        else if (takePreventPilotOwnership)
        {
            fragment.AddedPreventPilot = true;
        }

        if (!HasComp<PreventGridAnchorChangesComponent>(shuttleGrid))
        {
            EnsureComp<PreventGridAnchorChangesComponent>(shuttleGrid);
            fragment.AddedPreventAnchorChanges = true;
        }
        else if (takeAnchorLockOwnership)
        {
            fragment.AddedPreventAnchorChanges = true;
        }

        EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles(shuttleGrid);
    }

    private void UnlockLandedFragment(EntityUid shuttleGrid, bool preservePreventPilot)
    {
        if (!TryComp<KoronusLandedShuttleFragmentComponent>(shuttleGrid, out var fragment))
            return;

        if (fragment.PhysicsLocked && TryComp<PhysicsComponent>(shuttleGrid, out var physics))
        {
            StopPhysics(shuttleGrid);
            _physics.SetBodyType(shuttleGrid, fragment.OriginalBodyType, body: physics);
            _physics.SetBodyStatus(shuttleGrid, physics, fragment.OriginalBodyStatus);
            _physics.SetFixedRotation(shuttleGrid, fragment.OriginalFixedRotation, body: physics);
        }

        if (fragment.AddedPreventPilot && !preservePreventPilot)
            RemComp<PreventPilotComponent>(shuttleGrid);
        if (fragment.AddedPreventAnchorChanges)
            RemComp<PreventGridAnchorChangesComponent>(shuttleGrid);

        RemComp<KoronusLandedShuttleFragmentComponent>(shuttleGrid);
        EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles(shuttleGrid);
    }

    private void ReleaseParkingSession(KoronusLandingSession session, bool preservePreventPilot = false)
    {
        foreach (var fragment in session.Fragments.ToArray())
        {
            if (!TerminatingOrDeleted(fragment))
                UnlockLandedFragment(fragment, preservePreventPilot);
        }

        SetParkingPadCovered(session, false);
    }

    private void CoverLandingPadUnderShuttle(
        EntityUid shuttleGrid,
        EntityUid terrainGrid,
        ResolvedLandingSite site,
        KoronusLandingSession session)
    {
        if (site.TileEntities == null ||
            !TryComp<MapGridComponent>(shuttleGrid, out var shuttle) ||
            !TryComp<MapGridComponent>(terrainGrid, out var terrain))
        {
            return;
        }

        session.CoveredPadTiles.Clear();
        var tiles = _mapSystem.GetAllTilesEnumerator(shuttleGrid, shuttle);
        while (tiles.MoveNext(out var tileRef))
        {
            if (tileRef == null)
                continue;

            var center = _mapSystem.ToCenterCoordinates(tileRef.Value, shuttle);
            var mapCenter = _transform.ToMapCoordinates(center);
            var terrainIndex = _mapSystem.TileIndicesFor(terrainGrid, terrain, mapCenter);
            if (site.TileEntities.TryGetValue(terrainIndex, out var padTile))
                session.CoveredPadTiles.Add(padTile);
        }

        SetParkingPadCovered(session, true);
    }

    private void SetParkingPadCovered(KoronusLandingSession session, bool covered)
    {
        if (session.PadTilesCovered == covered)
            return;

        _landingPads.SetCovered(session.CoveredPadTiles, covered);
        session.PadTilesCovered = covered;
    }

    private static bool TryGetLandingSession(
        Entity<KoronusSectorRuleComponent> rule,
        EntityUid shuttleGrid,
        out KoronusLandingSession? session)
    {
        foreach (var candidate in rule.Comp.LandingSessions.Values)
        {
            if (!candidate.Fragments.Contains(shuttleGrid))
                continue;

            session = candidate;
            return true;
        }

        session = null;
        return false;
    }

    private void OnLandedGridSplit(
        EntityUid uid,
        KoronusLandedShuttleFragmentComponent component,
        ref GridSplitEvent args)
    {
        if (!TryGetSectorRule(out var rule) ||
            !rule.Comp.LandingSessions.TryGetValue(component.SessionId, out var session))
        {
            return;
        }

        session.Fragments.Add(uid);
        LockLandedFragment(
            uid,
            component.SessionId,
            component.AddedPreventPilot,
            component.AddedPreventAnchorChanges);
        foreach (var grid in args.NewGrids)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            if (!TryComp<KoronusLandedShuttleFragmentComponent>(grid, out var fragment))
            {
                fragment = EnsureComp<KoronusLandedShuttleFragmentComponent>(grid);
                fragment.OriginalBodyType = component.OriginalBodyType;
                fragment.OriginalBodyStatus = component.OriginalBodyStatus;
                fragment.OriginalFixedRotation = component.OriginalFixedRotation;
                fragment.PhysicsLocked = component.PhysicsLocked;
            }

            LockLandedFragment(
                grid,
                component.SessionId,
                component.AddedPreventPilot,
                component.AddedPreventAnchorChanges);
            session.Fragments.Add(grid);
        }
    }

    private void OnLandedShuttleTerminating(
        EntityUid uid,
        KoronusLandedShuttleFragmentComponent component,
        ref EntityTerminatingEvent args)
    {
        if (!TryGetSectorRule(out var rule) ||
            !rule.Comp.LandingSessions.TryGetValue(component.SessionId, out var session))
        {
            return;
        }

        session.Fragments.Remove(uid);
        if (session.Fragments.Count != 0)
            return;

        SetParkingPadCovered(session, false);
        rule.Comp.LandingReservations.Remove(session.ReservationKey);
        rule.Comp.LandingSessions.Remove(session.Id);
    }

    private void UpdateParkingSessions()
    {
        if (!TryGetSectorRule(out var rule))
            return;

        _parkingSessionSnapshot.Clear();
        _parkingSessionSnapshot.AddRange(rule.Comp.LandingSessions.Values);
        foreach (var session in _parkingSessionSnapshot)
        {
            if (!rule.Comp.LandingSessions.ContainsKey(session.Id))
                continue;

            session.Fragments.RemoveWhere(fragment => TerminatingOrDeleted(fragment));
            if (session.Fragments.Count == 0)
            {
                SetParkingPadCovered(session, false);
                rule.Comp.LandingReservations.Remove(session.ReservationKey);
                rule.Comp.LandingSessions.Remove(session.Id);
                continue;
            }

            if (session.Launching || session.Deadline == null || _timing.CurTime < session.Deadline.Value)
                continue;

            if (session.Fragments.Count == 1 &&
                session.Fragments.Contains(session.PrimaryGrid) &&
                TryLaunch(session.PrimaryGrid, out _))
            {
                session.Launching = true;
                continue;
            }

            EmergencyDepart(rule, session);
        }
    }

    private void EmergencyDepart(
        Entity<KoronusSectorRuleComponent> rule,
        KoronusLandingSession session)
    {
        if (!_prototypes.TryIndex<KoronusCelestialBodyPrototype>(session.BodyId, out var body) ||
            body.Surface == null ||
            !_prototypes.TryIndex<KoronusPlanetSurfacePrototype>(body.Surface.Value, out var surface) ||
            !_sector.TryGetSystemPrototype(body.System, out var system) ||
            !_sector.TryEnsureSystemMapLoaded(system.ID, out var orbitalMap) ||
            !_maps.TryGetMap(orbitalMap, out var orbitalMapUid))
        {
            return;
        }

        var fragments = session.Fragments.Where(fragment => !TerminatingOrDeleted(fragment)).ToArray();
        if (fragments.Length == 0)
            return;

        var anchor = fragments.Contains(session.PrimaryGrid) ? session.PrimaryGrid : fragments[0];
        if (!TryGetOrbitalLaunchOrigin(anchor, system, body, surface, out var anchorTarget))
            return;

        var anchorPosition = _transform.GetWorldPosition(Transform(anchor));
        var offsets = fragments.ToDictionary(
            fragment => fragment,
            fragment => _transform.GetWorldPosition(Transform(fragment)) - anchorPosition);

        ReleaseParkingSession(session);
        _maps.SetPaused(orbitalMap, false);
        foreach (var fragment in fragments)
            MoveGrid(fragment, orbitalMapUid.Value, anchorTarget + offsets[fragment]);

        FinishDeparture(rule, anchor, body.ID, session.ReservationKey, session);
    }

    private void FinishDeparture(
        Entity<KoronusSectorRuleComponent> rule,
        EntityUid shuttleGrid,
        string bodyId,
        string reservationKey,
        KoronusLandingSession? knownSession = null)
    {
        var session = knownSession;
        if (session == null)
            TryGetLandingSession(rule, shuttleGrid, out session);

        if (session != null)
            ReleaseParkingSession(session);

        var cooldownTargets = session?.Fragments.ToArray() ?? new[] { shuttleGrid };
        foreach (var fragment in cooldownTargets)
        {
            if (TerminatingOrDeleted(fragment))
                continue;

            RemCompDeferred<KoronusLandedShuttleFragmentComponent>(fragment);
            var cooldown = EnsureComp<KoronusPlanetaryLandingCooldownComponent>(fragment);
            cooldown.BodyId = bodyId;
            cooldown.Until = _timing.CurTime + LandingCooldown;
        }

        if (session != null)
            rule.Comp.LandingSessions.Remove(session.Id);
        rule.Comp.LandingReservations.Remove(reservationKey);
    }

    private void BeginTransit(
        EntityUid shuttleGrid,
        bool landing,
        MapId targetMap,
        Vector2 targetOrigin,
        string reservationKey,
        float startupTime,
        float travelTime,
        float arrivalTime,
        string? launchBodyId = null)
    {
        var sourceTransform = Transform(shuttleGrid);
        var transitMapUid = EnsureAtmosphericTransitMap();
        var transit = EnsureComp<KoronusPlanetaryTransitComponent>(shuttleGrid);
        transit.Landing = landing;
        transit.EnteredTransitSpace = false;
        transit.TravelStream = null;
        transit.TransitMobs.Clear();
        transit.SourceMap = sourceTransform.MapID;
        transit.SourceOrigin = _transform.GetWorldPosition(sourceTransform);
        transit.SourceAngle = _transform.GetWorldRotation(sourceTransform);
        transit.TargetMap = targetMap;
        transit.TargetOrigin = targetOrigin;
        transit.LaunchBodyId = landing ? null : launchBodyId;
        transit.TransitMap = Comp<MapComponent>(transitMapUid).MapId;
        transit.TransitOrigin = AllocateAtmosphericTransitOrigin(shuttleGrid);
        transit.TravelAt = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(0f, startupTime));
        transit.ArrivalAt = transit.TravelAt + TimeSpan.FromSeconds(MathF.Max(0f, travelTime));
        transit.CompleteAt = transit.ArrivalAt + TimeSpan.FromSeconds(MathF.Max(0f, arrivalTime));
        transit.ReservationKey = reservationKey;
        transit.AddedPreventPilot = !HasComp<PreventPilotComponent>(shuttleGrid);
        if (transit.AddedPreventPilot)
            EnsureComp<PreventPilotComponent>(shuttleGrid);

        var startupAudio = _audio.PlayPvs(_atmosphericStartupSound, shuttleGrid);
        _audio.SetGridAudio(startupAudio);
        StopPhysics(shuttleGrid);
        EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles(shuttleGrid);
    }

    /// <summary>
    /// A landing or launch replaces long-range navigation with controlled planetary transfer.
    /// Remove both an active HTN plan and its blackboard targets so it cannot resume after the
    /// shuttle reaches the surface or the atmospheric transit map.
    /// </summary>
    private void CancelShuttleAutopilot(EntityUid shuttleGrid)
    {
        var shuttleConsoleSystem = EntityManager.System<ShuttleConsoleSystem>();
        _shuttleConsoles.Clear();
        _lookup.GetChildEntities(shuttleGrid, _shuttleConsoles);

        foreach (var console in _shuttleConsoles)
        {
            shuttleConsoleSystem.ClearPilots(console.Comp);
            if (TryComp<HTNComponent>(console, out var htn))
            {
                htn.Blackboard.Remove<EntityCoordinates>(console.Comp.AutopilotTargetKey);
                htn.Blackboard.Remove<Angle>(console.Comp.AutopilotRotationKey);
                htn.Blackboard.Remove<EntityCoordinates>(console.Comp.AutoDockTargetKey);
                htn.Blackboard.Remove<Angle>(console.Comp.AutoDockRotationKey);
                _htn.ShutdownPlan(htn);
            }

            RemComp<ShuttleConsoleAutoDockingComponent>(console);
            _steering.Stop(console.Owner);
        }
    }

    private void UpdatePlanetaryTransfers()
    {
        var query = EntityQueryEnumerator<KoronusPlanetaryTransitComponent>();
        while (query.MoveNext(out var shuttleGrid, out var transit))
        {
            if (!transit.EnteredTransitSpace && _timing.CurTime >= transit.TravelAt)
            {
                if (!EnterAtmosphericTransitSpace(shuttleGrid, transit))
                {
                    CancelTransit(shuttleGrid, transit);
                    continue;
                }
            }

            if (_timing.CurTime < transit.CompleteAt)
            {
                if (transit.EnteredTransitSpace)
                {
                    HandleAtmosphericFallers(shuttleGrid, transit);
                    MaintainAtmosphericTransitMotion(shuttleGrid);
                }
                else
                    StopPhysics(shuttleGrid);
                continue;
            }

            if (!_maps.TryGetMap(transit.TargetMap, out var targetMap))
            {
                CancelTransit(shuttleGrid, transit);
                continue;
            }

            if (!transit.Landing && transit.LaunchBodyId is { } launchBodyId)
            {
                if (!_prototypes.TryIndex<KoronusCelestialBodyPrototype>(launchBodyId, out var launchBody) ||
                    launchBody.Surface == null ||
                    !_prototypes.TryIndex<KoronusPlanetSurfacePrototype>(launchBody.Surface.Value, out var launchSurface) ||
                    !_sector.TryGetSystemPrototype(launchBody.System, out var launchSystem) ||
                    !TryGetOrbitalLaunchOrigin(shuttleGrid, launchSystem, launchBody, launchSurface, out var currentOrigin))
                {
                    CancelTransit(shuttleGrid, transit);
                    continue;
                }

                transit.TargetOrigin = currentOrigin;
            }

            HandleAtmosphericFallers(shuttleGrid, transit);
            StopTransitAudio(transit);
            MoveGrid(shuttleGrid, targetMap.Value, transit.TargetOrigin);
            var arrivalAudio = _audio.PlayPvs(_atmosphericArrivalSound, shuttleGrid);
            _audio.SetGridAudio(arrivalAudio);
            if (TryGetSectorRule(out var rule))
            {
                if (transit.Landing &&
                    TryGetSurfaceRuntime(transit.TargetMap, out _, out var landingRuntime) &&
                    TryResolveBodyForSurface(landingRuntime.SurfaceId, out var landingBody) &&
                    landingBody.Surface != null &&
                    _prototypes.TryIndex<KoronusPlanetSurfacePrototype>(landingBody.Surface.Value, out var landingSurface) &&
                    TryGetLandingSite(landingRuntime, landingBody, transit.ReservationKey, out var landingSite))
                {
                    BeginParkingSession(
                        rule,
                        shuttleGrid,
                        landingBody,
                        landingSurface,
                        landingSite,
                        landingRuntime.TerrainGrid,
                        transit.ReservationKey,
                        transit.AddedPreventPilot);
                    // The parking lock now owns the component installed for descent.
                    transit.AddedPreventPilot = false;
                }
                else if (!transit.Landing)
                {
                    FinishDeparture(rule, shuttleGrid, transit.LaunchBodyId ?? string.Empty, transit.ReservationKey);
                }
            }

            CompleteTransit(shuttleGrid, transit);
        }
    }

    private void CancelTransit(EntityUid shuttleGrid, KoronusPlanetaryTransitComponent transit)
    {
        StopTransitAudio(transit);
        if (transit.EnteredTransitSpace && _maps.TryGetMap(transit.SourceMap, out var sourceMap))
            MoveGrid(shuttleGrid, sourceMap.Value, transit.SourceOrigin, transit.SourceAngle);

        if (TryGetSectorRule(out var rule))
        {
            if (transit.Landing)
            {
                rule.Comp.LandingReservations.Remove(transit.ReservationKey);
            }
            else if (TryGetLandingSession(rule, shuttleGrid, out var session) && session != null)
            {
                if (transit.EnteredTransitSpace)
                {
                    LockLandedFragment(shuttleGrid, session.Id, transit.AddedPreventPilot);
                    transit.AddedPreventPilot = false;
                }

                session.Launching = false;
                SetParkingPadCovered(session, true);
            }
        }

        CompleteTransit(shuttleGrid, transit);
    }

    private void CompleteTransit(EntityUid shuttleGrid, KoronusPlanetaryTransitComponent transit)
    {
        if (transit.AddedPreventPilot)
            RemComp<PreventPilotComponent>(shuttleGrid);

        RemComp<KoronusPlanetaryTransitComponent>(shuttleGrid);
        EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles(shuttleGrid);
    }

    private EntityUid EnsureAtmosphericTransitMap()
    {
        if (_atmosphericTransitMap != EntityUid.Invalid && !TerminatingOrDeleted(_atmosphericTransitMap))
            return _atmosphericTransitMap;

        var mapUid = _maps.CreateMap(out _);
        _metaData.SetEntityName(mapUid, "Atmospheric transit");
        var parallax = EnsureComp<ParallaxComponent>(mapUid);
        parallax.Parallax = "AtmosphericTransit";
        Dirty(mapUid, parallax);
        _atmosphericTransitMap = mapUid;
        _nextAtmosphericTransitCoordinate = 0f;
        return mapUid;
    }

    private Vector2 AllocateAtmosphericTransitOrigin(EntityUid shuttleGrid)
    {
        var grid = Comp<MapGridComponent>(shuttleGrid);
        var origin = new Vector2(_nextAtmosphericTransitCoordinate, 0f) - grid.LocalAABB.Center;
        _nextAtmosphericTransitCoordinate += MathF.Max(1f, grid.LocalAABB.Width) + AtmosphericTransitSpacing;
        if (_nextAtmosphericTransitCoordinate > MaxAtmosphericTransitCoordinate)
            _nextAtmosphericTransitCoordinate = 0f;

        return origin;
    }

    private bool EnterAtmosphericTransitSpace(EntityUid shuttleGrid, KoronusPlanetaryTransitComponent transit)
    {
        if (!_maps.TryGetMap(transit.TransitMap, out var transitMap))
            return false;

        if (!transit.Landing &&
            TryGetSectorRule(out var rule) &&
            TryGetLandingSession(rule, shuttleGrid, out var session) &&
            session != null)
        {
            if (TryComp<KoronusLandedShuttleFragmentComponent>(shuttleGrid, out var fragment) &&
                fragment.AddedPreventPilot)
            {
                transit.AddedPreventPilot = true;
            }

            // The shuttle remains bolted down for the whole launch startup. Release it only as it
            // actually leaves the surface, while atmospheric transit retains the pilot lock.
            ReleaseParkingSession(session, preservePreventPilot: true);
        }

        TrackAtmosphericTransitMobs(shuttleGrid, transit);
        MoveGrid(shuttleGrid, transitMap.Value, transit.TransitOrigin);
        transit.EnteredTransitSpace = true;
        MaintainAtmosphericTransitMotion(shuttleGrid);

        var travelAudio = _audio.PlayPvs(_atmosphericTravelSound, shuttleGrid);
        transit.TravelStream = travelAudio?.Entity;
        _audio.SetGridAudio(travelAudio);
        EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles(shuttleGrid);
        return true;
    }

    private void TrackAtmosphericTransitMobs(EntityUid shuttleGrid, KoronusPlanetaryTransitComponent transit)
    {
        transit.TransitMobs.Clear();
        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var mob, out _, out var transform))
        {
            if (transform.GridUid == shuttleGrid)
                transit.TransitMobs.Add(mob);
        }
    }

    /// <summary>
    /// A mob which leaves its shuttle during atmospheric transit falls to the planetary surface.
    /// During descent that is the target map; during ascent it is the source map.
    /// </summary>
    private void HandleAtmosphericFallers(EntityUid shuttleGrid, KoronusPlanetaryTransitComponent transit)
    {
        if (transit.TransitMobs.Count == 0)
            return;

        var surfaceMap = transit.Landing ? transit.TargetMap : transit.SourceMap;
        if (!TryGetSurfaceRuntime(surfaceMap, out _, out var runtime))
            return;

        _atmosphericTransitMobRemovals.Clear();
        foreach (var mob in transit.TransitMobs)
        {
            if (TerminatingOrDeleted(mob) || !TryComp<TransformComponent>(mob, out var transform))
            {
                _atmosphericTransitMobRemovals.Add(mob);
                continue;
            }

            if (transform.MapID != transit.TransitMap || transform.GridUid == shuttleGrid)
                continue;

            if (!TryFindAtmosphericFallCoordinates(surfaceMap, runtime, out var fallCoordinates))
                continue;

            _transform.SetCoordinates(mob, transform, fallCoordinates);
            StopPhysics(mob);
            ApplyAtmosphericFallDamage(mob);
            _atmosphericTransitMobRemovals.Add(mob);
        }

        foreach (var mob in _atmosphericTransitMobRemovals)
            transit.TransitMobs.Remove(mob);
    }

    private bool TryFindAtmosphericFallCoordinates(
        MapId surfaceMap,
        KoronusPlanetSurfaceMapComponent runtime,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        if (!TryComp<MapGridComponent>(runtime.TerrainGrid, out var terrain) ||
            !_prototypes.TryIndex<KoronusPlanetSurfacePrototype>(runtime.SurfaceId, out var surface))
        {
            return false;
        }

        // Only consider already materialised turf. This prevents a falling mob from being placed on
        // an empty biome chunk and keeps the rare operation bounded by the authored playable area.
        var tiles = _mapSystem
            .GetLocalTilesIntersecting(runtime.TerrainGrid, terrain, runtime.PlayableBounds, true)
            .ToList();
        _random.Shuffle(tiles);

        foreach (var tile in tiles)
        {
            var candidate = _turf.GetTileCenter(tile);
            var mapPosition = _transform.ToMapCoordinates(candidate).Position;
            if (!runtime.PlayableBounds.Contains(mapPosition) ||
                IsInsideLandingSite(mapPosition, runtime) ||
                _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
            {
                continue;
            }

            coordinates = candidate;
            return coordinates.GetMapId(EntityManager) == surfaceMap;
        }

        return false;
    }

    private bool IsInsideLandingSite(
        Vector2 position,
        KoronusPlanetSurfaceMapComponent runtime)
    {
        foreach (var site in GetLandingSites(runtime))
        {
            var halfSize = site.Size / 2f + new Vector2(AtmosphericFallLandingSitePadding);
            if (new Box2(site.Position - halfSize, site.Position + halfSize).Contains(position))
                return true;
        }

        return false;
    }

    private void ApplyAtmosphericFallDamage(EntityUid mob)
    {
        if (!HasComp<DamageableComponent>(mob))
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", FixedPoint2.New(AtmosphericFallDamage));
        _damageable.TryChangeDamage(mob, damage, ignoreResistances: true);
    }

    private void MaintainAtmosphericTransitMotion(EntityUid shuttleGrid)
    {
        if (!TryComp<PhysicsComponent>(shuttleGrid, out var physics))
            return;

        _physics.SetLinearVelocity(shuttleGrid, Vector2.UnitY * AtmosphericTravelSpeed, body: physics);
        _physics.SetAngularVelocity(shuttleGrid, 0f, body: physics);
    }

    private void StopTransitAudio(KoronusPlanetaryTransitComponent transit)
    {
        transit.TravelStream = _audio.Stop(transit.TravelStream);
    }

    /// <summary>
    /// The surface gameplay perimeter is a square. Only map-root grids are polled; loose root
    /// entities are handled by movement events through KoronusSurfaceBoundaryTrackedComponent.
    /// Biome output has a separate visual buffer and the terrain grid itself is never displaced.
    /// </summary>
    private void EnforceSurfaceBoundaries()
    {
        foreach (var (mapId, boundary) in _surfaceBoundaries)
        {
            if (_maps.IsPaused(mapId) ||
                !TryComp<TransformComponent>(boundary.MapUid, out var mapTransform))
            {
                continue;
            }

            var children = mapTransform.ChildEnumerator;
            while (children.MoveNext(out var gridUid))
            {
                if (gridUid == boundary.TerrainGrid ||
                    !TryComp<MapGridComponent>(gridUid, out _) ||
                    !TryComp<TransformComponent>(gridUid, out var transform))
                {
                    continue;
                }

                var worldBounds = _physics.GetWorldAABB(gridUid, xform: transform);
                var correction = GetBoundsCorrection(worldBounds, boundary.Bounds);
                if (correction == Vector2.Zero)
                    continue;

                _transform.SetWorldPosition((gridUid, transform), _transform.GetWorldPosition(transform) + correction);
                StopPhysics(gridUid);
            }
        }
    }

    private void SeedSurfaceBoundaryTracking(MapId mapId)
    {
        if (!_surfaceBoundaries.TryGetValue(mapId, out var boundary) ||
            !TryComp<TransformComponent>(boundary.TerrainGrid, out var terrainTransform))
        {
            return;
        }

        var children = terrainTransform.ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (!TryComp<TransformComponent>(uid, out var transform))
                continue;

            UpdateSurfaceBoundaryTracking(uid, transform);
        }

        if (boundary.MapUid == boundary.TerrainGrid ||
            !TryComp<TransformComponent>(boundary.MapUid, out var mapTransform))
        {
            return;
        }

        // Leaving the last materialised planet tile makes grid traversal temporarily parent a
        // mover directly to the map. Those roots must remain tracked until they are clamped back.
        var mapChildren = mapTransform.ChildEnumerator;
        while (mapChildren.MoveNext(out var uid))
        {
            if (TryComp<TransformComponent>(uid, out var transform))
                UpdateSurfaceBoundaryTracking(uid, transform);
        }
    }

    private void OnPhysicsParentChanged(Entity<PhysicsComponent> entity, ref EntParentChangedMessage args)
    {
        if (TryComp<TransformComponent>(entity.Owner, out var transform))
            UpdateSurfaceBoundaryTracking(entity.Owner, transform);
    }

    private void OnTrackedEntityMoved(Entity<KoronusSurfaceBoundaryTrackedComponent> entity, ref MoveEvent args)
    {
        if (!TryComp<TransformComponent>(entity.Owner, out var transform) ||
            !_surfaceBoundaries.TryGetValue(transform.MapID, out var boundary) ||
            !IsSurfaceBoundaryRoot(transform, boundary))
        {
            StopSurfaceBoundaryTracking(entity.Owner);
            return;
        }

        if (transform.Anchored)
            return;

        ClampSurfaceEntity(entity.Owner, transform, boundary);
    }

    private void UpdateSurfaceBoundaryTracking(EntityUid uid, TransformComponent transform)
    {
        if (HasComp<MapComponent>(uid) ||
            HasComp<MapGridComponent>(uid) ||
            !_surfaceBoundaries.TryGetValue(transform.MapID, out var boundary) ||
            !IsSurfaceBoundaryRoot(transform, boundary))
        {
            StopSurfaceBoundaryTracking(uid);
            return;
        }

        if (transform.Anchored)
            return;

        EnsureComp<KoronusSurfaceBoundaryTrackedComponent>(uid);
        ClampSurfaceEntity(uid, transform, boundary);
    }

    private void RefreshSurfaceBoundaryTracking()
    {
        if (_timing.CurTime < _nextBoundaryTrackingRefresh)
            return;

        _nextBoundaryTrackingRefresh = _timing.CurTime + BoundaryTrackingRefreshInterval;
        foreach (var mapId in _surfaceBoundaries.Keys)
            SeedSurfaceBoundaryTracking(mapId);
    }

    private void StopSurfaceBoundaryTracking(EntityUid uid)
    {
        if (HasComp<KoronusSurfaceBoundaryTrackedComponent>(uid))
            RemComp<KoronusSurfaceBoundaryTrackedComponent>(uid);
    }

    private static bool IsSurfaceBoundaryRoot(TransformComponent transform, SurfaceBoundary boundary)
    {
        return transform.ParentUid == boundary.TerrainGrid ||
               transform.ParentUid == boundary.MapUid && transform.GridUid == null;
    }

    private void ClampSurfaceEntity(EntityUid uid, TransformComponent transform, SurfaceBoundary boundary)
    {
        var position = _transform.GetWorldPosition(transform);
        var correction = TryComp<PhysicsComponent>(uid, out _)
            ? GetBoundsCorrection(_physics.GetWorldAABB(uid, xform: transform), boundary.Bounds)
            : GetBoundsCorrection(new Box2(position, position), boundary.Bounds);
        if (correction == Vector2.Zero)
            return;

        _transform.SetWorldPosition((uid, transform), position + correction);
        StopPhysics(uid);
    }

    private void OnSurfaceMapShutdown(Entity<KoronusPlanetSurfaceMapComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp<MapComponent>(entity.Owner, out var map))
            return;

        _surfaceBoundaries.Remove(map.MapId);
    }

    private static Vector2 GetBoundsCorrection(Box2 value, Box2 bounds)
    {
        var x = value.Width > bounds.Width
            ? bounds.Center.X - value.Center.X
            : value.Left < bounds.Left
                ? bounds.Left - value.Left
                : value.Right > bounds.Right
                    ? bounds.Right - value.Right
                    : 0f;
        var y = value.Height > bounds.Height
            ? bounds.Center.Y - value.Center.Y
            : value.Bottom < bounds.Bottom
                ? bounds.Bottom - value.Bottom
                : value.Top > bounds.Top
                    ? bounds.Top - value.Top
                    : 0f;
        return new Vector2(x, y);
    }

    private void StopPhysics(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return;

        _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(uid, 0f, body: physics);
    }

    private bool TryGetSurfaceRuntime(
        MapId mapId,
        out EntityUid mapUid,
        out KoronusPlanetSurfaceMapComponent runtime)
    {
        mapUid = EntityUid.Invalid;
        runtime = default!;
        if (!_maps.TryGetMap(mapId, out var foundMap) ||
            !TryComp<KoronusPlanetSurfaceMapComponent>(foundMap.Value, out var foundRuntime))
        {
            return false;
        }

        mapUid = foundMap.Value;
        runtime = foundRuntime;
        return true;
    }

    private bool TryResolveBodyForSurface(string surfaceId, out KoronusCelestialBodyPrototype body)
    {
        foreach (var candidate in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (candidate.Surface != null && candidate.Surface.Value == surfaceId)
            {
                body = candidate;
                return true;
            }
        }

        body = default!;
        return false;
    }

    private static Box2 GetPlayableBounds(KoronusPlanetSurfacePrototype surface)
    {
        var halfSize = surface.PlayableSize / 2f;
        return new Box2(-halfSize, halfSize);
    }

    private static Box2 GetGenerationBounds(KoronusPlanetSurfacePrototype surface)
    {
        var buffer = Math.Clamp(surface.SceneryBuffer, 0f, MaxGeneratedPlanetSceneryBuffer);
        var halfSize = surface.PlayableSize / 2f + new Vector2(buffer);
        return new Box2(-halfSize, halfSize);
    }

    private static string GetReservationKey(string bodyId, string siteId) => $"{bodyId}/{siteId}";

    private readonly record struct SurfaceBoundary(EntityUid MapUid, Box2 Bounds, EntityUid TerrainGrid);

    private bool IsWithinLandingApproach(
        EntityUid shuttleGrid,
        KoronusSystemPrototype system,
        KoronusCelestialBodyPrototype body)
    {
        if (body.LandingApproachRadius <= 0f)
            return true;

        var shuttlePosition = _transform.GetWorldPosition(Transform(shuttleGrid));
        var bodyPosition = GetCelestialPosition(system, body);
        return Vector2.DistanceSquared(shuttlePosition, bodyPosition) <=
               body.LandingApproachRadius * body.LandingApproachRadius;
    }

    public bool IsWithinLandingApproach(EntityUid shuttleGrid, string bodyId)
    {
        return _prototypes.TryIndex<KoronusCelestialBodyPrototype>(bodyId, out var body) &&
               _sector.TryGetSystemPrototype(body.System, out var system) &&
               _sector.TryGetSystemId(Transform(shuttleGrid).MapID, out var currentSystem) &&
               currentSystem == system.ID &&
               IsWithinLandingApproach(shuttleGrid, system, body);
    }

    private Vector2 GetCelestialPosition(KoronusSystemPrototype system, KoronusCelestialBodyPrototype body)
    {
        if (body.OrbitRadius <= 0f)
            return system.NavigationCenter;

        var positionAngle = _sector.GetCelestialBodyPositionAngle(system, body);
        var phase = (positionAngle + body.OrbitAngularSpeed * (float) _timing.CurTime.TotalSeconds) * MathF.PI / 180f;
        return system.NavigationCenter + new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * body.OrbitRadius;
    }

    private bool HasLandableBodies(string systemId)
    {
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.System == systemId && body.Surface != null)
                return true;
        }

        return false;
    }

    private KoronusPlanetaryInterfaceState BuildInterfaceState(
        Entity<KoronusSectorRuleComponent> rule,
        KoronusSystemPrototype system,
        EntityUid shuttleGrid,
        bool canLand,
        bool canLaunch,
        string? landedBody,
        bool navigationSuppressed = false)
    {
        var transitState = TryComp<KoronusPlanetaryTransitComponent>(shuttleGrid, out var transit)
            ? new KoronusPlanetaryTransitState(
                transit.Landing,
                Math.Max(0f, (float) (transit.CompleteAt - _timing.CurTime).TotalSeconds))
            : null;

        var bodies = new List<KoronusCelestialBodyState>();
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.System != system.ID)
                continue;

            var sites = new List<KoronusLandingSiteState>();
            if (body.Surface != null &&
                _prototypes.TryIndex(body.Surface.Value, out KoronusPlanetSurfacePrototype? surface) &&
                rule.Comp.SurfaceMaps.TryGetValue(surface.ID, out var surfaceMap) &&
                TryGetSurfaceRuntime(surfaceMap, out _, out var surfaceRuntime))
            {
                foreach (var site in GetLandingSites(surfaceRuntime))
                {
                    var key = GetReservationKey(body.ID, site.Id);
                    var occupied = rule.Comp.LandingReservations.TryGetValue(key, out var owner);
                    sites.Add(new KoronusLandingSiteState(
                        site.Id,
                        site.DisplayName,
                        site.Size,
                        site.Enabled,
                        occupied,
                        occupied && owner == shuttleGrid));
                }
            }

            bodies.Add(new KoronusCelestialBodyState(
                body.ID,
                body.DisplayName,
                body.Description,
                body.Climate.Id,
                body.BodyType.ToString(),
                body.OrbitRadius,
                _sector.GetCelestialBodyPositionAngle(system, body),
                body.OrbitAngularSpeed,
                body.NavVisualRadius,
                body.LandingApproachRadius,
                landedBody == null && IsWithinLandingApproach(shuttleGrid, system, body),
                sites));
        }

        bodies.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return new KoronusPlanetaryInterfaceState(
            true,
            canLand,
            canLaunch,
            navigationSuppressed || landedBody != null,
            system.ID,
            landedBody,
            system.NavigationCenter,
            bodies,
            transitState);
    }

    private bool TryBuildTransitInterfaceState(
        Entity<KoronusSectorRuleComponent> rule,
        EntityUid shuttleGrid,
        KoronusPlanetaryTransitComponent transit,
        out KoronusPlanetaryInterfaceState state)
    {
        if (_sector.TryGetSystemId(transit.SourceMap, out var systemId) &&
            _sector.TryGetSystemPrototype(systemId, out var orbitalSystem))
        {
            state = BuildInterfaceState(rule, orbitalSystem, shuttleGrid, false, false, null, navigationSuppressed: true);
            return true;
        }

        if (TryGetSurfaceRuntime(transit.SourceMap, out _, out var surfaceRuntime) &&
            _sector.TryGetSystemPrototype(surfaceRuntime.SystemId, out var surfaceSystem) &&
            TryResolveBodyForSurface(surfaceRuntime.SurfaceId, out var landedBody))
        {
            state = BuildInterfaceState(rule, surfaceSystem, shuttleGrid, false, false, landedBody.ID, navigationSuppressed: true);
            return true;
        }

        state = KoronusPlanetaryInterfaceState.Unavailable();
        return false;
    }

    private static bool TryGetReservation(
        Entity<KoronusSectorRuleComponent> rule,
        EntityUid shuttleGrid,
        out string reservationKey)
    {
        foreach (var (key, owner) in rule.Comp.LandingReservations)
        {
            if (owner == shuttleGrid)
            {
                reservationKey = key;
                return true;
            }
        }

        reservationKey = string.Empty;
        return false;
    }

    /// <summary>
    /// The ability to land changes while a shuttle is manually flying. Console BUI state is event
    /// driven, so refresh only the consoles whose per-planet approach mask actually changed.
    /// </summary>
    private void RefreshLandingApproachStateChanges()
    {
        _activeApproachShuttles.Clear();
        var shuttles = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
        while (shuttles.MoveNext(out var shuttleGrid, out _, out var transform))
        {
            _activeApproachShuttles.Add(shuttleGrid);
            var state = GetLandingApproachState(shuttleGrid, transform);
            if (!_landingApproachStates.TryGetValue(shuttleGrid, out var previousState))
            {
                _landingApproachStates[shuttleGrid] = state;
                continue;
            }

            if (previousState == state)
                continue;

            _landingApproachStates[shuttleGrid] = state;
            EntityManager.System<ShuttleConsoleSystem>().RefreshShuttleConsoles(shuttleGrid);
        }

        _landingApproachRemovals.Clear();
        foreach (var shuttleGrid in _landingApproachStates.Keys)
        {
            if (!_activeApproachShuttles.Contains(shuttleGrid))
                _landingApproachRemovals.Add(shuttleGrid);
        }

        foreach (var shuttleGrid in _landingApproachRemovals)
            _landingApproachStates.Remove(shuttleGrid);
    }

    private LandingApproachState GetLandingApproachState(EntityUid shuttleGrid, TransformComponent transform)
    {
        if (!_sector.TryGetSystemId(transform.MapID, out var systemId) ||
            !_sector.TryGetSystemPrototype(systemId, out var system) ||
            !HasLandableBodies(system.ID))
        {
            return default;
        }

        var count = 0;
        long hashSum = 0;
        var hashXor = 0;
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.System != system.ID || body.Surface == null || !IsWithinLandingApproach(shuttleGrid, system, body))
                continue;

            var bodyHash = StringComparer.Ordinal.GetHashCode(body.ID);
            count++;
            hashSum += bodyHash;
            hashXor ^= bodyHash;
        }

        return new LandingApproachState(count, hashSum, hashXor);
    }

    private void PruneStaleReservations()
    {
        if (!TryGetSectorRule(out var rule))
            return;

        _reservationRemovals.Clear();
        foreach (var (key, shuttleGrid) in rule.Comp.LandingReservations)
        {
            if (!TerminatingOrDeleted(shuttleGrid) &&
                TryComp<KoronusPlanetaryTransitComponent>(shuttleGrid, out var transit) &&
                transit.Landing &&
                transit.ReservationKey == key)
            {
                continue;
            }

            if (TerminatingOrDeleted(shuttleGrid) ||
                !TryGetSurfaceRuntime(Transform(shuttleGrid).MapID, out _, out var surface))
            {
                _reservationRemovals.Add(key);
                continue;
            }

            // A completed landing session is the authoritative snapshot of the occupied pad.
            // Re-scanning entity pads can temporarily fail while overlapping grids update, and
            // the pad may also be damaged while a shuttle is parked on it.
            if (HasLandingSessionReservation(rule.Comp, key, shuttleGrid))
                continue;

            if (!SurfaceOwnsReservation(surface.SurfaceId, key))
                _reservationRemovals.Add(key);
        }

        foreach (var key in _reservationRemovals)
            rule.Comp.LandingReservations.Remove(key);
    }

    private static bool HasLandingSessionReservation(
        KoronusSectorRuleComponent rule,
        string reservationKey,
        EntityUid shuttleGrid)
    {
        foreach (var session in rule.LandingSessions.Values)
        {
            if (session.ReservationKey == reservationKey && session.Fragments.Contains(shuttleGrid))
                return true;
        }

        return false;
    }

    private bool SurfaceOwnsReservation(string surfaceId, string reservationKey)
    {
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.Surface == null ||
                body.Surface.Value != surfaceId)
                continue;

            if (TryGetSurfaceMap(surfaceId, out var surfaceMap) &&
                TryGetSurfaceRuntime(surfaceMap, out _, out var runtime))
            {
                return GetLandingSites(runtime)
                    .Any(site => GetReservationKey(body.ID, site.Id) == reservationKey);
            }

            return false;
        }

        return false;
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

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _surfaceBoundaries.Clear();
        _nextBoundaryUpdate = default;
        _nextBoundaryTrackingRefresh = default;
        _nextParkingUpdate = default;
        _nextReservationPrune = default;
        _nextApproachRefresh = default;
        _atmosphericTransitMap = EntityUid.Invalid;
        _nextAtmosphericTransitCoordinate = 0f;
        _landingApproachStates.Clear();
        _parkingSessionSnapshot.Clear();
        _reservationRemovals.Clear();
        _activeApproachShuttles.Clear();
        _landingApproachRemovals.Clear();

        if (!TryGetSectorRule(out var rule))
            return;

        rule.Comp.SurfaceMaps.Clear();
        rule.Comp.LandingReservations.Clear();
        rule.Comp.LandingSessions.Clear();
        rule.Comp.NextLandingSessionId = 1;
    }

    private readonly record struct LandingResolution(
        Entity<KoronusSectorRuleComponent> Rule,
        KoronusCelestialBodyPrototype Body,
        KoronusPlanetSurfacePrototype Surface,
        ResolvedLandingSite Site,
        MapId SurfaceMap,
        EntityUid SurfaceMapUid,
        KoronusPlanetSurfaceMapComponent SurfaceRuntime);

    private readonly record struct LandingApproachState(int Count, long HashSum, int HashXor);

    private sealed record ResolvedLandingSite(
        string Id,
        string DisplayName,
        Vector2 Position,
        Vector2 Size,
        bool Enabled,
        bool PublicAccess,
        HashSet<Vector2i>? Tiles,
        IReadOnlyDictionary<Vector2i, EntityUid>? TileEntities,
        EntityUid? Console,
        int ParkingTime);
}

public enum KoronusPlanetaryTransferFailure : byte
{
    None,
    InvalidTarget,
    InvalidShuttle,
    ShuttleInFtl,
    ShuttleDocked,
    OutsideLandingApproach,
    TransferInProgress,
    LandingSiteOccupied,
    ShuttleTooLarge,
    LandingAreaBlocked,
    NotLanded,
    NotOnLandingSite,
    InvalidLaunchPosition,
    AccessDenied,
    LandingCooldown,
    ShuttleFragmented,
}
