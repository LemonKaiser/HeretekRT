using System.Numerics;

namespace Content.Shared._WH40K.GridMomentum;

/// <summary>Forgets a removed grid on every <see cref="Wh40kGridMomentumComponent"/> so deleted uids are never networked.</summary>
public sealed class Wh40kGridMomentumSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
    }

    private void OnGridRemoval(GridRemovalEvent ev)
    {
        var grid = ev.EntityUid;
        var query = EntityQueryEnumerator<Wh40kGridMomentumComponent>();
        while (query.MoveNext(out var uid, out var momentum))
        {
            if (momentum.Grid != grid && momentum.Left != grid)
                continue;

            if (momentum.Grid == grid)
                momentum.Grid = null;

            if (momentum.Left == grid)
            {
                momentum.Left = null;
                momentum.Frame = Vector2.Zero;
            }

            Dirty(uid, momentum);
        }
    }
}
