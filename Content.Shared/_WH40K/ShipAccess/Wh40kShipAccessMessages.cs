using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShipAccess;

[Serializable, NetSerializable]
public sealed class Wh40kShipAccessRefresh : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public enum Wh40kShipAccessAction : byte
{
    ProtectShip,
    RestoreShip,
    ProtectDoor,
    RestoreDoor,
    GrantGuest,
    RevokeGuest,
    RestrictGuestGrant,
    AllowGuestSelfGrant,
}

[Serializable, NetSerializable]
public sealed class Wh40kShipAccessRequest(Wh40kShipAccessAction action, NetEntity target = default) : BoundUserInterfaceMessage
{
    public readonly Wh40kShipAccessAction Action = action;
    public readonly NetEntity Target = target;
}

[Serializable, NetSerializable]
public readonly record struct Wh40kShipAccessDoor(NetEntity Id, string Name, Vector2 Position, bool Protected);

[Serializable, NetSerializable]
public readonly record struct Wh40kShipAccessGuest(NetEntity Id, string Name);

[Serializable, NetSerializable]
public sealed class Wh40kShipAccessSnapshot(
    NetEntity? grid,
    string? registeredTo,
    bool canManage,
    bool ownerGrantsOnly,
    int protectedReaders,
    int totalReaders,
    Wh40kShipAccessDoor[] doors,
    Wh40kShipAccessGuest[] guests,
    Wh40kShipAccessGuest[] nearby) : BoundUserInterfaceMessage
{
    public readonly NetEntity? Grid = grid;
    public readonly string? RegisteredTo = registeredTo;
    public readonly bool CanManage = canManage;
    public readonly bool OwnerGrantsOnly = ownerGrantsOnly;
    public readonly int ProtectedReaders = protectedReaders;
    public readonly int TotalReaders = totalReaders;
    public readonly Wh40kShipAccessDoor[] Doors = doors;
    public readonly Wh40kShipAccessGuest[] Guests = guests;
    public readonly Wh40kShipAccessGuest[] Nearby = nearby;
}
