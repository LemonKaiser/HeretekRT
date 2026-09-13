using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WH40K.Activities;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Adds a small, finite mining field to every active Koronus system map. This is world scenery and
/// resource gameplay, not an activity: it creates no marker, NPC, ghost role, lease or respawn
/// loop. A cold-restored map retains its field; a new round receives a new one.
/// </summary>
public sealed class KoronusAsteroidFieldSystem : EntitySystem
{
    private const int AsteroidsPerSystem = 4;
    private const int PlacementAttemptsPerAsteroid = 96;
    private const float GridClearance = 1000f;
    private const float CelestialClearance = 1000f;
    private const float BoundaryClearance = 1000f;

    private static readonly string[] AsteroidPrototypes =
    [
        "NFAsteroidDebrisSmall",
        "NFAsteroidDebrisMedium",
        "NFAsteroidDebrisLarge",
    ];

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private ActivityContentAudit _contentAudit = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    // Map snapshots survive cold-unload, while this set prevents ConfigureSystemMap from creating
    // a second resource field when such a snapshot is restored during the same round.
    private readonly HashSet<string> _initializedSystems = new(StringComparer.Ordinal);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Called only after the system has been registered in the authoritative Koronus map index.
    /// The method fails closed when that registration cannot be resolved.
    /// </summary>
    public void EnsureField(EntityUid mapUid, KoronusSystemPrototype system)
    {
        if (_initializedSystems.Contains(system.ID) ||
            !system.Enabled ||
            !TryComp<MapComponent>(mapUid, out var map) ||
            !TryComp<KoronusSystemMapComponent>(mapUid, out var runtime) ||
            runtime.SystemId != system.ID ||
            !_sector.TryGetSystemMap(system.ID, out var registeredMap) ||
            registeredMap != map.MapId)
        {
            return;
        }

        var spawned = 0;
        for (var index = 0; index < AsteroidsPerSystem; index++)
        {
            if (!TryFindSafePosition(system, map.MapId, out var position))
                continue;

            var asteroid = Spawn(_random.Pick(AsteroidPrototypes), new EntityCoordinates(mapUid, position));
            if (!TryConfigureAsteroid(asteroid))
            {
                QueueDel(asteroid);
                continue;
            }

            spawned++;
        }

        _initializedSystems.Add(system.ID);
        if (spawned < AsteroidsPerSystem)
        {
            Log.Warning(
                $"Placed only {spawned}/{AsteroidsPerSystem} Koronus asteroids in {system.ID}; the remainder failed the clearance check.");
        }
    }

    private bool TryFindSafePosition(
        KoronusSystemPrototype system,
        MapId mapId,
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
            var radius = MathF.Sqrt(_random.NextFloat(0f, 1f)) * usableRadius;
            var candidate = system.NavigationCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;

            if (!HasGridClearance(mapId, candidate) ||
                !HasCelestialClearance(system, candidate))
            {
                continue;
            }

            position = candidate;
            return true;
        }

        position = default;
        return false;
    }

    private bool HasGridClearance(MapId mapId, Vector2 candidate)
    {
        foreach (var grid in _mapManager.GetAllGrids(mapId))
        {
            if (TerminatingOrDeleted(grid.Owner))
                continue;

            var transform = Transform(grid.Owner);
            var origin = _transform.GetWorldPosition(transform);
            var rotation = _transform.GetWorldRotation(transform);
            var center = origin + rotation.RotateVec(grid.Comp.LocalAABB.Center);
            var halfDiagonal = (grid.Comp.LocalAABB.TopRight - grid.Comp.LocalAABB.Center).Length();
            var clearance = GridClearance + halfDiagonal;
            if (Vector2.DistanceSquared(candidate, center) < clearance * clearance)
                return false;
        }

        return true;
    }

    private bool HasCelestialClearance(KoronusSystemPrototype system, Vector2 candidate)
    {
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.System != system.ID)
                continue;

            var clearance = CelestialClearance + Math.Max(0f, body.NavVisualRadius);
            if (body.OrbitRadius > 0f && body.OrbitAngularSpeed != 0f)
            {
                // Asteroids are static while planets move. Reserving the full orbit keeps the
                // one-kilometre exclusion zone intact for the whole round, not only at spawn time.
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
        _initializedSystems.Clear();
    }
}
