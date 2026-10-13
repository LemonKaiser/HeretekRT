using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WH40K.Activities;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Components.GC;
using Content.Shared.GameTicking;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Tiles;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Charts finite mining clusters, loads nearby sites and conservatively retains player changes.
/// Uses native asteroid population and sector snapshots instead of infinite worldgen chunks.
/// </summary>
public sealed partial class KoronusAsteroidFieldSystem : EntitySystem
{
    private const int ClusterCount = 6;
    private const float ClusterRadius = 1250f;
    // Twenty-four sites per 4.91 km² area: 4.89 asteroids/km², with 280 m minimum separation.
    private const int AsteroidsPerCluster = 24;
    private const float ClusterCelestialClearance = 500f;
    public const float LoadingDistance = 1000f;
    private const float RetentionDistance = 1000f;
    private const float VisitDistance = 200f;
    private static readonly TimeSpan EvictionDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DepletedEvictionDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReplenishmentInterval = TimeSpan.FromMinutes(30);
    private const int ReplenishmentBudget = 10;
    private const int PlacementAttemptsPerAsteroid = 96;
    private const float GridClearance = 250f;
    private const float CelestialClearance = 250f;
    // ExtraGigantic has a 28-tile radius. Include blob overdraw and tile/AABB corners.
    private const float AsteroidRadius = 44f;
    private const float BoundaryClearance = 1000f;

    private static readonly ProtoId<WeightedRandomEntityPrototype> AsteroidPool = "KoronusMiningAsteroids";

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private ActivityContentAudit _contentAudit = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private TimeSpan _nextUpdate;
    private readonly Dictionary<MapId, List<Vector2>> _observers = new();
    private readonly HashSet<EntityUid> _occupiedGrids = new();
    private readonly List<EntityUid> _retire = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusMiningAsteroidComponent, EntityTerminatingEvent>(OnAsteroidTerminating);
        SubscribeLocalEvent<KoronusMiningAsteroidComponent, ComponentStartup>(OnAsteroidStartup);
        SubscribeLocalEvent<BeforeSerializationEvent>(OnBeforeSerialization);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Called only after the system has been registered in the authoritative Koronus map index.
    /// The method fails closed when that registration cannot be resolved.
    /// </summary>
    public void EnsureField(EntityUid mapUid, KoronusSystemPrototype system)
    {
        if (!system.Enabled ||
            !TryComp<MapComponent>(mapUid, out var map) ||
            !TryComp<KoronusSystemMapComponent>(mapUid, out var runtime) ||
            runtime.SystemId != system.ID ||
            !_sector.TryGetSystemMap(system.ID, out var registeredMap) ||
            registeredMap != map.MapId)
        {
            return;
        }

        if (!TryComp<KoronusAsteroidFieldComponent>(mapUid, out var field))
        {
            field = AddComp<KoronusAsteroidFieldComponent>(mapUid);
            field.NextReplenishment = _timing.CurTime + ReplenishmentInterval;
            for (var index = 0; index < ClusterCount; index++)
            {
                if (TryFindCluster(system, map.MapId, field, index < 3, out var position))
                    field.Clusters.Add(new KoronusAsteroidCluster { Position = position, Radius = ClusterRadius });
            }
        }

        if (field.Clusters.Count < ClusterCount)
            Log.Warning($"Charted only {field.Clusters.Count}/{ClusterCount} Koronus asteroid areas in {system.ID}; the remainder failed safe placement.");
        var asteroidCount = field.Clusters.Count * AsteroidsPerCluster;
        // Upgrade unloaded legacy sites without changing live grids, slot indices or recovery timers.
        foreach (var site in field.Sites)
        {
            if (site.Materialized || site.Depleted ||
                !site.Prototype.StartsWith("KoronusMiningAsteroid", StringComparison.Ordinal))
                continue;

            var cluster = field.Clusters[site.Cluster];
            var usableRadius = Math.Max(0f, cluster.Radius - AsteroidRadius);
            if (Vector2.DistanceSquared(site.Position, cluster.Position) > usableRadius * usableRadius)
            {
                if (!TryFindSafePosition(system, map.MapId, field, site.Cluster, out var position))
                    continue;
                site.Position = position;
            }
            site.Prototype = PickAsteroidPrototype();
        }

        for (var cluster = 0; cluster < field.Clusters.Count; cluster++)
        {
            var existing = 0;
            foreach (var site in field.Sites)
                if (site.Cluster == cluster)
                    existing++;

            for (var index = existing; index < AsteroidsPerCluster; index++)
            {
                if (!TryFindSafePosition(system, map.MapId, field, cluster, out var position))
                    continue;

                field.Sites.Add(new KoronusAsteroidSite
                {
                    Position = position,
                    Cluster = cluster,
                    Prototype = PickAsteroidPrototype(),
                });
            }
        }

        if (field.Sites.Count < asteroidCount)
        {
            Log.Warning(
                $"Planned only {field.Sites.Count}/{asteroidCount} Koronus asteroids in {system.ID}; the remainder failed safe placement.");
        }
    }

    private string PickAsteroidPrototype() => _prototypes.Index(AsteroidPool).Pick(_random);

    private bool TryFindCluster(KoronusSystemPrototype system, MapId mapId,
        KoronusAsteroidFieldComponent field, bool inner, out Vector2 position)
    {
        var radius = Math.Max(0f, system.BoundaryRadius - BoundaryClearance - ClusterRadius);
        Vector2? startingFacility = null;
        if (field.Clusters.Count == 0)
        {
            var largest = 0f;
            foreach (var grid in _maps.GetAllGrids(mapId))
            {
                var size = grid.Comp.LocalAABB.Size.LengthSquared();
                if (size <= largest)
                    continue;
                largest = size;
                startingFacility = _transform.GetWorldPosition(grid.Owner);
            }
        }
        for (var attempt = 0; attempt < PlacementAttemptsPerAsteroid; attempt++)
        {
            var angle = _random.NextFloat(0f, MathF.Tau);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            // Prefer the inhabited system, then widen the search when its planetary orbits leave no room.
            var searchRadius = inner && attempt < 48 ? Math.Min(radius, 8000f)
                : inner && attempt < 72 ? Math.Min(radius, 11000f) : radius;
            var candidate = startingFacility is { } facility && attempt < 32
                ? facility + direction * 3200f
                : system.NavigationCenter + direction * MathF.Sqrt(_random.NextFloat()) * searchRadius;
            if (Vector2.DistanceSquared(candidate, system.NavigationCenter) >
                radius * radius)
                continue;
            if (!HasGridClearance(mapId, candidate, ClusterRadius) ||
                !HasCelestialClearance(system, candidate, ClusterRadius, ClusterCelestialClearance))
                continue;
            var overlaps = false;
            foreach (var cluster in field.Clusters)
                overlaps |= Vector2.DistanceSquared(candidate, cluster.Position) < 3000f * 3000f;
            if (overlaps)
                continue;
            position = candidate;
            return true;
        }
        position = default;
        return false;
    }

    private bool TryFindSafePosition(
        KoronusSystemPrototype system,
        MapId mapId,
        KoronusAsteroidFieldComponent field,
        int clusterIndex,
        out Vector2 position)
    {
        var usableRadius = system.BoundaryRadius - BoundaryClearance;
        if (usableRadius <= 0f)
        {
            position = default;
            return false;
        }

        for (var attempt = 0; attempt < PlacementAttemptsPerAsteroid; attempt++)
        {
            var angle = _random.NextFloat(0f, MathF.Tau);
            var cluster = field.Clusters[clusterIndex];
            var radius = MathF.Sqrt(_random.NextFloat()) * Math.Max(0f, cluster.Radius - AsteroidRadius);
            var candidate = cluster.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;

            if (!HasGridClearance(mapId, candidate) ||
                !HasCelestialClearance(system, candidate) ||
                Vector2.DistanceSquared(candidate, system.NavigationCenter) > usableRadius * usableRadius)
            {
                continue;
            }

            var overlaps = false;
            foreach (var site in field.Sites)
                overlaps |= Vector2.DistanceSquared(candidate, site.Position) < 280f * 280f;
            if (overlaps)
                continue;
            position = candidate;
            return true;
        }

        position = default;
        return false;
    }

    private bool HasGridClearance(MapId mapId, Vector2 candidate, float featureRadius = AsteroidRadius)
    {
        foreach (var grid in _maps.GetAllGrids(mapId))
        {
            if (TerminatingOrDeleted(grid.Owner))
                continue;

            var transform = Transform(grid.Owner);
            var origin = _transform.GetWorldPosition(transform);
            var rotation = _transform.GetWorldRotation(transform);
            var center = origin + rotation.RotateVec(grid.Comp.LocalAABB.Center);
            var halfDiagonal = (grid.Comp.LocalAABB.TopRight - grid.Comp.LocalAABB.Center).Length();
            // Mining sites already have 280 m separation. Applying the ship/facility margin here
            // would prevent nearby planned sites from loading as soon as their neighbour exists.
            var margin = TryComp<KoronusMiningAsteroidComponent>(grid.Owner, out var asteroid) &&
                !asteroid.IsShuttle && !HasComp<ShuttleComponent>(grid.Owner)
                ? 0f
                : GridClearance;
            var clearance = margin + featureRadius + halfDiagonal;
            if (Vector2.DistanceSquared(candidate, center) < clearance * clearance)
                return false;
        }

        return true;
    }

    private bool HasCelestialClearance(KoronusSystemPrototype system, Vector2 candidate,
        float featureRadius = AsteroidRadius, float minimumClearance = CelestialClearance)
    {
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.System != system.ID)
                continue;

            var clearance = minimumClearance + featureRadius + Math.Max(0f, body.NavVisualRadius);
            if (body.OrbitRadius > 0f && body.OrbitAngularSpeed != 0f)
            {
                // Asteroids are static while planets move. Reserving the full orbit keeps the
                // exclusion zone intact for the whole round, not only at spawn time.
                if (MathF.Abs(Vector2.Distance(candidate, system.NavigationCenter) - body.OrbitRadius) < clearance)
                    return false;

                continue;
            }

            var bodyPosition = GetCelestialPosition(system, body);
            if (Vector2.DistanceSquared(candidate, bodyPosition) < clearance * clearance)
                return false;
        }

        return true;
    }

    private Vector2 GetCelestialPosition(KoronusSystemPrototype system, KoronusCelestialBodyPrototype body)
    {
        if (body.OrbitRadius <= 0f)
            return system.NavigationCenter;

        var angle = _sector.GetCelestialBodyPositionAngle(system, body);
        var phase = (angle + body.OrbitAngularSpeed * (float) _timing.CurTime.TotalSeconds) * MathF.PI / 180f;
        return system.NavigationCenter + new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * body.OrbitRadius;
    }

    private bool TryConfigureAsteroid(EntityUid asteroid)
    {
        // ShuttleSystem adds ShuttleComponent to every newly initialized grid, including the
        // native debris grids. These asteroids are scenery, not player or NPC ships: remove the
        // implicit component before the fail-closed audit so they can never acquire FTL or docks.
        RemComp<ShuttleComponent>(asteroid);
        // Finite fields own their lifecycle. Infinite-worldgen ownership and GC links are unset here
        // and would leave invalid controller references in the sector's cold snapshot.
        RemComp<OwnedDebrisComponent>(asteroid);
        RemComp<GCAbleObjectComponent>(asteroid);
        // Mining grids may inherit facility safeguards from native salvage prototypes.
        RemComp<ProtectedGridComponent>(asteroid);

        // The audited source pool is a MapGrid-only mining feature. Re-check it at runtime so a
        // future inherited component cannot turn it into a shuttle, dock, FTL target or ghost role.
        if (!_contentAudit.TryValidateStaticActivityGrid(asteroid, out _) ||
            !TryComp<PhysicsComponent>(asteroid, out var physics))
        {
            return false;
        }

        _physics.SetLinearVelocity(asteroid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(asteroid, 0f, body: physics);
        _physics.SetBodyType(asteroid, BodyType.Static, body: physics);
        _physics.SetFixedRotation(asteroid, true, body: physics);

        return true;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _observers.Clear();
        _occupiedGrids.Clear();
        _retire.Clear();
        _nextUpdate = default;
    }

}
