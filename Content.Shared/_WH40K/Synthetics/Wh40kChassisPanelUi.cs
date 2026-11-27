using Content.Shared._Shitmed.Targeting;
using Content.Shared._WH40K.Augments;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Synthetics;

[Serializable, NetSerializable]
public enum Wh40kChassisPanelUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class Wh40kChassisPartEntry
{
    public NetEntity? Part;
    public TargetBodyPart Slot;
    public string Name = string.Empty;
    public bool Attached;
    public bool Isolated;
    public bool Working;
    public bool Loose;
    public int LoosePasses;
    public float Integrity = 1f;
    public List<string> Faults = new();
    public bool CanDetach;
}

[Serializable, NetSerializable]
public sealed class Wh40kChassisOrganEntry
{
    public NetEntity? Organ;
    public string Slot = string.Empty;
    public TargetBodyPart Holder;
    public string Name = string.Empty;
    /// <summary>Integrity or charge fraction 0..1, or -1 if not applicable.</summary>
    public float Health = -1f;
    public string Band = string.Empty;
    public bool Vital;
    public bool IsBattery;
    public bool CanDisconnect;
}

[Serializable, NetSerializable]
public sealed class Wh40kChassisPanelState : BoundUserInterfaceState
{
    public List<Wh40kChassisPartEntry> Parts = new();
    public List<Wh40kChassisOrganEntry> Organs = new();
    public List<Wh40kAugmentBedOption> AugmentOptions = new();
    public List<Wh40kInstalledAugment> InstalledAugments = new();

    public bool BayOpen;
    public bool OnAugmentTable;
    public bool AugmentTablePowered;
    public int AugmentFocusSeq;
    public bool Overclocked;
    public bool LowPower;
    public bool Rebooting;
    public bool Dead;

    /// <summary>Battery charge 0..1, or -1 when no cell is present.</summary>
    public float Power = -1f;

    /// <summary>Overall chassis structural integrity 0..1.</summary>
    public float Integrity = 1f;

    /// <summary>Sensor/stamina headroom 0..1.</summary>
    public float Sensors = 1f;

    /// <summary>Core temperature in Kelvin, or -1 when unavailable.</summary>
    public float CoreTemperature = -1f;

    /// <summary>Overclock internal heat load 0..1.</summary>
    public float ChassisHeat;

    /// <summary>Rad collector charge 0..1, or -1 when missing/burnt out.</summary>
    public float RadCollector = -1f;

    /// <summary>Rad collector integrity 0..1, or -1 when missing.</summary>
    public float RadCollectorIntegrity = -1f;

    /// <summary>Ambient radiation exposure in rad/s.</summary>
    public float Radiation;

    public string Status = string.Empty;
}

[Serializable, NetSerializable]
public sealed class Wh40kChassisIsolatePartMessage(NetEntity part, bool isolate) : BoundUserInterfaceMessage
{
    public readonly NetEntity Part = part;
    public readonly bool Isolate = isolate;
}

[Serializable, NetSerializable]
public sealed class Wh40kChassisDetachPartMessage(NetEntity part) : BoundUserInterfaceMessage
{
    public readonly NetEntity Part = part;
}

[Serializable, NetSerializable]
public sealed class Wh40kChassisToggleBayMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class Wh40kChassisDisconnectOrganMessage(NetEntity organ) : BoundUserInterfaceMessage
{
    public readonly NetEntity Organ = organ;
}

[Serializable, NetSerializable]
public sealed class Wh40kChassisRebootMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class Wh40kChassisOverclockMessage(bool on) : BoundUserInterfaceMessage
{
    public readonly bool On = on;
}

[Serializable, NetSerializable]
public sealed class Wh40kChassisInstallAugmentMessage(NetEntity item, Wh40kAugmentBedZone zone) : BoundUserInterfaceMessage
{
    public readonly NetEntity Item = item;
    public readonly Wh40kAugmentBedZone Zone = zone;
}
