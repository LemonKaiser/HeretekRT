using System.Numerics;
using Content.Server._WH40K.ShipCombat;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    [Dependency] private Wh40kTurretTraverseSystem _wh40kTraverse = default!;

    private float Wh40kGridReach(TransformComponent xform)
    {
        if (xform.ParentUid != xform.GridUid || !TryComp<MapGridComponent>(xform.GridUid, out var grid))
            return float.MaxValue;

        var bounds = grid.LocalAABB;
        var local = xform.LocalPosition;
        var x = MathF.Max(MathF.Abs(bounds.Left - local.X), MathF.Abs(bounds.Right - local.X));
        var y = MathF.Max(MathF.Abs(bounds.Bottom - local.Y), MathF.Abs(bounds.Top - local.Y));
        return MathF.Sqrt(x * x + y * y) + 1f;
    }

    public bool Wh40kClearShot(EntityUid weapon, Vector2 from, Vector2 to, MapId map)
    {
        return HasLineOfSight(weapon, from, to, map);
    }
}
