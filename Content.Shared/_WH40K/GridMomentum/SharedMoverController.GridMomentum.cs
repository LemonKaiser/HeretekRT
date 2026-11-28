using System.Numerics;
using Content.Shared._WH40K.GridMomentum;
using Robust.Shared.Network;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private INetManager _wh40kNet = default!;

    private const float Wh40kGridFrameRange = 4f;

    private Vector2 Wh40kGridFrame(EntityUid uid, TransformComponent xform)
    {
        return xform.GridUid == null && TryComp<Wh40kGridMomentumComponent>(uid, out var momentum) ? momentum.Frame : Vector2.Zero;
    }

    private void Wh40kTrackGrid(EntityUid uid, TransformComponent xform)
    {
        if (!TryComp<Wh40kGridMomentumComponent>(uid, out var momentum))
        {
            if (_wh40kNet.IsClient || xform.GridUid == null)
                return;
            momentum = EnsureComp<Wh40kGridMomentumComponent>(uid);
        }

        if (xform.GridUid is { } grid)
        {
            if (momentum.Grid != grid || momentum.Frame != Vector2.Zero || momentum.Left != null)
            {
                momentum.Grid = grid;
                momentum.Left = null;
                momentum.Frame = Vector2.Zero;
                Dirty(uid, momentum);
            }

            return;
        }

        if (momentum.Grid is { } left)
        {
            momentum.Grid = null;
            momentum.Left = left;
            momentum.Frame = !TerminatingOrDeleted(left) && PhysicsQuery.TryComp(left, out var body)
                ? PhysicsSystem.GetLinearVelocity(left, Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(left)), body)
                : Vector2.Zero;
            Dirty(uid, momentum);
            return;
        }

        if ((momentum.Frame != Vector2.Zero || momentum.Left != null) && !Wh40kBeside(xform, momentum.Left))
        {
            momentum.Left = null;
            momentum.Frame = Vector2.Zero;
            Dirty(uid, momentum);
        }
    }

    private bool Wh40kBeside(TransformComponent xform, EntityUid? grid)
    {
        if (grid is not { } left || TerminatingOrDeleted(left) || !MapGridQuery.TryComp(left, out var gridComp) ||
            Transform(left).MapID != xform.MapID)
            return false;

        var local = Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(left));
        var bounds = gridComp.LocalAABB;
        var nearest = Vector2.Clamp(local, bounds.BottomLeft, bounds.TopRight);
        return (nearest - local).LengthSquared() <= Wh40kGridFrameRange * Wh40kGridFrameRange;
    }
}
