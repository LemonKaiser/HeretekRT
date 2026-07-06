using Content.Shared.DoAfter;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Interaction.Components;

public sealed partial class HandheldEntityPlacementAttemptEvent : CancellableEntityEventArgs
{
    public EntityUid User;
    public EntityCoordinates Coordinates;
    public Direction Direction;

    public TimeSpan DeployDelay = TimeSpan.Zero;
    public bool BreakOnMove = true;
    public bool BreakOnDamage;
    public bool BreakOnHandChange = true;
    public bool NeedHand = true;

    public HandheldEntityPlacementAttemptEvent(EntityUid user, EntityCoordinates coordinates, Direction direction)
    {
        User = user;
        Coordinates = coordinates;
        Direction = direction;
    }
}

public sealed partial class HandheldEntityPlacementCompleteEvent : HandledEntityEventArgs
{
    public EntityUid User;
    public EntityCoordinates Coordinates;
    public Direction Direction;

    public HandheldEntityPlacementCompleteEvent(EntityUid user, EntityCoordinates coordinates, Direction direction)
    {
        User = user;
        Coordinates = coordinates;
        Direction = direction;
    }
}

[Serializable, NetSerializable]
public sealed partial class HandheldEntityPlacementDoAfterEvent : DoAfterEvent
{
    public NetCoordinates Coordinates;
    public Direction Direction = Direction.Invalid;

    public HandheldEntityPlacementDoAfterEvent()
    {
    }

    public HandheldEntityPlacementDoAfterEvent(NetCoordinates coordinates, Direction direction)
    {
        Coordinates = coordinates;
        Direction = direction;
    }

    public override DoAfterEvent Clone()
    {
        return new HandheldEntityPlacementDoAfterEvent(Coordinates, Direction);
    }
}

public sealed partial class HandheldEntityFoldRequestEvent : CancellableEntityEventArgs
{
    public EntityUid User;
    public bool Handled;

    public HandheldEntityFoldRequestEvent(EntityUid user)
    {
        User = user;
    }
}

public sealed partial class HandheldEntityFoldAttemptEvent : CancellableEntityEventArgs
{
    public EntityUid User;
    public TimeSpan FoldDelay = TimeSpan.Zero;
    public bool BreakOnMove = true;
    public bool BreakOnDamage;
    public bool BreakOnHandChange = true;
    public bool NeedHand = true;

    public HandheldEntityFoldAttemptEvent(EntityUid user)
    {
        User = user;
    }
}

public sealed partial class HandheldEntityFoldCompleteEvent : HandledEntityEventArgs
{
    public EntityUid User;

    public HandheldEntityFoldCompleteEvent(EntityUid user)
    {
        User = user;
    }
}

[Serializable, NetSerializable]
public sealed partial class HandheldEntityFoldDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone()
    {
        return new HandheldEntityFoldDoAfterEvent();
    }
}
