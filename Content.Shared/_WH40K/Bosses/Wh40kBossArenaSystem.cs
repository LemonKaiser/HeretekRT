using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Shared._WH40K.Bosses;

public sealed class Wh40kBossArenaSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public Vector2 Position(EntityUid anchor, EntityUid entity) =>
        Vector2.Transform(_transform.GetWorldPosition(entity), _transform.GetInvWorldMatrix(anchor));

    public Vector2 World(EntityUid anchor, Vector2 local) =>
        Vector2.Transform(local, _transform.GetWorldMatrix(anchor));

    public bool Contains(EntityUid boss, EntityUid entity, float margin = 0)
    {
        if (!TryComp<Wh40kBossArenaComponent>(boss, out var arena) || arena.Anchor is not { } anchor ||
            !Exists(anchor) || !TryComp<TransformComponent>(boss, out var bx) ||
            !TryComp<TransformComponent>(entity, out var tx) || bx.MapID != tx.MapID ||
            (HasComp<MapGridComponent>(anchor) ? tx.GridUid != anchor : bx.GridUid != tx.GridUid))
            return false;
        return Wh40kChaosMarineCombat.InArena(Position(anchor, entity), arena.Center, arena.HalfSize, margin);
    }
}
