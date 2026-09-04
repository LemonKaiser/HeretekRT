using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;

namespace Content.Shared.RCD;

[Serializable, NetSerializable]
public sealed class RCDSystemMessage : BoundUserInterfaceMessage
{
    public ProtoId<RCDPrototype> ProtoId;

    public RCDSystemMessage(ProtoId<RCDPrototype> protoId)
    {
        ProtoId = protoId;
    }
}

[Serializable, NetSerializable]
public sealed class RCDConstructionGhostRotationEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;
    public readonly Direction Direction;

    public RCDConstructionGhostRotationEvent(NetEntity netEntity, Direction direction)
    {
        NetEntity = netEntity;
        Direction = direction;
    }
}

/// <summary>
/// Raised server-side after an RCD has successfully changed the world.
/// Cosmetic systems may use this as a post-operation hook; it is not networked and does not affect gameplay.
/// </summary>
[ByRefEvent]
public readonly record struct RCDOperationCompletedEvent(MapCoordinates Coordinates, RcdMode Mode, EntityUid? Target)
{
    public readonly MapCoordinates Coordinates = Coordinates;
    public readonly RcdMode Mode = Mode;
    public readonly EntityUid? Target = Target;
}

[Serializable, NetSerializable]
public enum RcdUiKey : byte
{
    Key
}
