using Content.Shared._WH40K.TurnInPlace;
using Content.Shared.Movement.Components;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    private bool Wh40kTryTurnInPlace(EntityUid entity, Direction dir, bool state)
    {
        if (!state || !HasComp<Wh40kTurnInPlaceComponent>(entity))
            return false;

        if (Wh40kMoverTarget(entity) is not { } target)
            return true;

        if (NoRotateQuery.HasComp(target))
            return false;

        if (!MoverQuery.TryComp(target, out var mover)
            || !mover.CanMove
            || _container.IsEntityInContainer(target)
            || !XformQuery.TryComp(target, out var xform))
        {
            return true;
        }

        var vec = dir.ToVec();
        var wish = _relativeMovement ? GetParentGridAngle(mover).RotateVec(vec) : vec;
        var worldRot = _transform.GetWorldRotation(xform);
        _transform.SetLocalRotation(target, xform.LocalRotation + wish.ToWorldAngle() - worldRot, xform);
        return true;
    }

    public bool Wh40kFacingLocked(EntityUid entity)
    {
        return Wh40kMoverTarget(entity) is { } target && NoRotateQuery.HasComp(target);
    }

    public void Wh40kReleaseDirections(EntityUid entity, ushort subTick)
    {
        if (Wh40kMoverTarget(entity) is { } target && MoverQuery.TryComp(target, out var mover))
            SetMoveInput((target, mover), subTick, false, MoveButtons.AnyDirection);
    }

    private EntityUid? Wh40kMoverTarget(EntityUid entity)
    {
        for (var i = 0; i < 4 && RelayQuery.TryComp(entity, out var relay); i++)
        {
            if (_mobState.IsIncapacitated(entity))
                return null;

            entity = relay.RelayEntity;
        }

        return entity;
    }
}
