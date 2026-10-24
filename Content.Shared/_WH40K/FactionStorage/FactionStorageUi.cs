using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.FactionStorage;

[Serializable, NetSerializable]
public enum FactionStorageUiKey : byte { Key }

[Serializable, NetSerializable]
public enum FactionDepotStatus : byte { Loading, Ready, Busy, Unavailable }

[Serializable, NetSerializable]
public sealed record FactionDepotSlot(int Index, Guid Revision, string? Prototype, string? Name, string? Description, int Count);

[Serializable, NetSerializable]
public sealed class FactionDepotState(string faction, FactionDepotSlot[] slots, FactionDepotStatus status) : BoundUserInterfaceState
{
    public readonly string Faction = faction;
    public readonly FactionDepotSlot[] Slots = slots;
    public readonly FactionDepotStatus Status = status;
}

[Serializable, NetSerializable]
public sealed class FactionDepotTransfer(int slot, Guid revision, bool deposit) : BoundUserInterfaceMessage
{
    public readonly int Slot = slot;
    public readonly Guid Revision = revision;
    public readonly bool Deposit = deposit;
}

[Serializable, NetSerializable]
public sealed class FactionDepotRefresh : BoundUserInterfaceMessage;
