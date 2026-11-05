using Content.Shared.VendingMachines;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.VendingMachines;

[Serializable, NetSerializable]
public enum VendingMachineKeypadSound : byte
{
    Beep,
    Success,
    Error,
    Timeout,
}

[Serializable, NetSerializable]
public sealed class VendingMachineKeypadAudioMessage(VendingMachineKeypadSound sound, float pitch = 1f)
    : BoundUserInterfaceMessage
{
    public readonly VendingMachineKeypadSound Sound = sound;
    public readonly float Pitch = pitch;
}

[Serializable, NetSerializable]
public sealed class VendingMachineVendResultMessage(InventoryType type, string id, bool success)
    : BoundUserInterfaceMessage
{
    public readonly InventoryType Type = type;
    public readonly string Id = id;
    public readonly bool Success = success;
}
