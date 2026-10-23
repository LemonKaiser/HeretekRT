using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.FactionStorage;

[Serializable, NetSerializable]
public enum FactionStorageUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed record FactionStorageSlotState(
    int Slot,
    Guid Revision,
    string? Prototype,
    string? Name,
    string? Description,
    int Count);

[Serializable, NetSerializable]
public sealed class FactionStorageStateMessage(
    string faction,
    List<FactionStorageSlotState> slots,
    bool busy) : BoundUserInterfaceMessage
{
    public string Faction = faction;
    public List<FactionStorageSlotState> Slots = slots;
    public bool Busy = busy;
}

[Serializable, NetSerializable]
public sealed class FactionStorageTransferMessage(int slot, Guid revision, bool deposit) : BoundUserInterfaceMessage
{
    public int Slot = slot;
    public Guid Revision = revision;
    public bool Deposit = deposit;
}

[Serializable, NetSerializable]
public sealed class FactionStorageRefreshMessage : BoundUserInterfaceMessage;
