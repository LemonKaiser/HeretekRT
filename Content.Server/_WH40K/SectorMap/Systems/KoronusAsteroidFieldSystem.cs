using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WH40K.Activities;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Adds a small, finite mining field to explicitly configured external Koronus systems. This is
/// world scenery and resource gameplay, not an activity: it creates no marker, NPC, ghost role,
/// lease or respawn loop. A cold-restored map retains its field; a new round receives a new one.
/// </summary>
public sealed class KoronusAsteroidFieldSystem : EntitySystem
{
    private const int PlacementAttemptsPerAsteroid = 48;
    private const float GridClearance = 180f;

    private static readonly string[] AsteroidPrototypes =
    [
        "NFAsteroidDebrisSmall",
        "NFAsteroidDebrisMedium",
        "NFAsteroidDebrisLarge",
    ];

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private ActivitySafetyGuard _safety = default!;
    [Dependency] private ActivityContentAudit _contentAudit = default!;

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
    /// The method fails closed when the map or its safety policy cannot be resolved.
    /// </summary>
    public void EnsureField(EntityUid mapUid, KoronusSystemPrototype system)
    {
        if (_initializedSystems.Contains(system.ID) ||
            !KoronusAsteroidFieldPolicy.IsEligible(
                system.ID,
                system.Enabled,
                system.ActivityEligible,
                system.AsteroidField) ||
            !TryComp<MapComponent>(mapUid, out var map) ||
            !TryComp<KoronusSystemMapComponent>(mapUid, out var runtime) ||
            runtime.SystemId != system.ID ||
            !_sector.TryGetSystemMap(system.ID, out var registeredMap) ||
            registeredMap != map.MapId)
        {
            return;
        }

        var field = system.AsteroidField!;
        var spawned = 0;
        for (var index = 0; index < field.Count; index++)
        {
            if (!TryFindSafePosition(system, map.MapId, field, out var position))
                continue;

            var asteroid = Spawn(_random.Pick(AsteroidPrototypes), new EntityCoordinates(mapUid, position));
            if (!TryConfigureAsteroid(asteroid, system.ID, map.MapId, position))
            {
                QueueDel(asteroid);
                continue;
            }

            spawned++;
        }

        _initializedSystems.Add(system.ID);
        if (spawned < field.Count)
        {
            Log.Warning(
                $"Placed only {spawned}/{field.Count} Koronus asteroids in {system.ID}; the remainder failed the safety or clearance check.");
        }
    }

    private bool TryFindSafePosition(
        KoronusSystemPrototype system,
        MapId mapId,
        KoronusAsteroidFieldDefinition field,
        out Vector2 position)
    {
        for (var attempt = 0; attempt < PlacementAttemptsPerAsteroid; attempt++)
        {
            var angle = _random.NextFloat(0f, MathF.Tau);
            var radius = _random.NextFloat(field.InnerRadius, field.OuterRadius);
            var candidate = system.NavigationCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var coordinates = new MapCoordinates(candidate, mapId);

            if (_lookup.GetEntitiesInRange<MapGridComponent>(coordinates, GridClearance).Any() ||
                !_safety.TryValidate(new KoronusActivityTarget(system.ID, mapId, candidate), out _))
            {
                continue;
            }

            position = candidate;
            return true;
        }

        position = default;
        return false;
    }

    private bool TryConfigureAsteroid(EntityUid asteroid, string systemId, MapId mapId, Vector2 position)
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

        return _safety.TryValidate(new KoronusActivityTarget(systemId, mapId, position, asteroid), out _);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _initializedSystems.Clear();
    }
}
