using System.Numerics;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.Bosses;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kBossSpawnSystem : EntitySystem
{
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    public bool CanSpawnEncounter(EntityUid map)
    {
        if (TerminatingOrDeleted(map) || !HasComp<MapComponent>(map) ||
            TryComp<Wh40kBossSpawnedComponent>(map, out var spawned) && spawned.Spawned)
            return false;

        var query = EntityQueryEnumerator<Wh40kBossComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == map)
                return false;
        }
        return true;
    }

    public bool EnsureEncounter(EntityUid map, EntProtoId prototype, Vector2 position)
    {
        if (!CanSpawnEncounter(map) || !TryComp<MapComponent>(map, out var mapComponent) ||
            _safety.HasRule(mapComponent.MapId, position, KoronusSafetyRule.StationProtection | KoronusSafetyRule.HostileNpcAutoCleanup |
                                                        KoronusSafetyRule.PlayerDamage | KoronusSafetyRule.ShipWeapons) ||
            _maps.TryFindGridAt(new MapCoordinates(position, mapComponent.MapId), out _, out _))
            return false;

        SpawnAtPosition(prototype, new EntityCoordinates(map, position));
        EnsureComp<Wh40kBossSpawnedComponent>(map).Spawned = true;
        return true;
    }
}
