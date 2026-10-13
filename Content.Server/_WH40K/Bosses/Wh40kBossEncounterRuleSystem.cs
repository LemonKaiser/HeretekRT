using System.Linq;
using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.StationEvents.Events;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Bosses;

public sealed class Wh40kBossEncounterRuleSystem : StationEventSystem<Wh40kBossEncounterRuleComponent>
{
    private const int SpawnAttempts = 24;
    private const float SpawnClearance = 40f;
    private const KoronusSafetyRule ProtectedRules = KoronusSafetyRule.PlayerDamage | KoronusSafetyRule.ShipWeapons |
                                                     KoronusSafetyRule.StationProtection | KoronusSafetyRule.HostileNpcAutoCleanup;

    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private Wh40kBossSpawnSystem _spawns = default!;
    [Dependency] private Wh40kSpaceBossSystem _bosses = default!;

    protected override void Started(EntityUid uid, Wh40kBossEncounterRuleComponent component,
        GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        if (RobustRandom.Prob(component.Chance))
            TrySpawnEncounter(component);
        ForceEndSelf(uid, gameRule);
    }

    private bool TrySpawnEncounter(Wh40kBossEncounterRuleComponent component)
    {
        var candidates = new List<EntityUid>();
        var checkedGrids = new HashSet<EntityUid>();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player || !TryGetTarget(player, out var xform, out _) ||
                xform.GridUid is { } grid && !checkedGrids.Add(grid))
                continue;
            candidates.Add(player);
        }

        while (candidates.Count > 0)
        {
            var index = RobustRandom.Next(candidates.Count);
            var player = candidates[index];
            candidates.RemoveAt(index);
            if (!TryGetTarget(player, out var xform, out var system) || xform.MapUid is not { } map)
                continue;

            var origin = _transform.GetWorldPosition(xform);
            for (var attempt = 0; attempt < SpawnAttempts; attempt++)
            {
                var position = origin + RobustRandom.NextAngle().ToVec() *
                    RobustRandom.NextFloat(component.MinimumSpawnDistance, component.MaximumSpawnDistance);
                var coordinates = new MapCoordinates(position, xform.MapID);
                var boundary = system.BoundaryRadius * system.WarningFraction - SpawnClearance;
                if (boundary <= 0 || Vector2.DistanceSquared(position, system.NavigationCenter) > boundary * boundary ||
                    !IsClearSpawn(coordinates))
                    continue;

                if (!_spawns.EnsureEncounter(map, component.BossPrototype, position))
                    continue;
                Sawmill.Info($"Spawned {component.BossPrototype} in {system.ID} at {position}.");
                return true;
            }
        }
        return false;
    }

    private bool TryGetTarget(EntityUid player, out TransformComponent xform, out KoronusSystemPrototype system)
    {
        xform = default!;
        system = default!;
        if (TerminatingOrDeleted(player) || Paused(player) || HasComp<GhostComponent>(player) || !_mobState.IsAlive(player) ||
            !TryComp(player, out TransformComponent? transform) || transform.MapUid is not { } map ||
            !_sector.TryGetSystemId(transform.MapID, out var systemId) || systemId == "Footfall" ||
            !_sector.TryGetSystemPrototype(systemId, out system) || !system.Enabled ||
            !_spawns.CanSpawnEncounter(map) || _bosses.IsProtectedTarget(player) ||
            _safety.HasRule(transform.MapID, _transform.GetWorldPosition(transform), ProtectedRules))
            return false;

        if (transform.GridUid is { } grid &&
            (HasComp<FTLComponent>(grid) || HasComp<KoronusPlanetaryTransitComponent>(grid)))
            return false;

        xform = transform;
        return true;
    }

    private bool IsClearSpawn(MapCoordinates coordinates)
    {
        if (_lookup.GetEntitiesInRange<MapGridComponent>(coordinates, SpawnClearance).Any())
            return false;

        if (_safety.HasRule(coordinates.MapId, coordinates.Position, ProtectedRules))
            return false;
        for (var sample = 0; sample < 8; sample++)
        {
            var angle = sample * MathF.PI / 4;
            var point = coordinates.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * SpawnClearance;
            if (_safety.HasRule(coordinates.MapId, point, ProtectedRules))
                return false;
        }
        return true;
    }
}
