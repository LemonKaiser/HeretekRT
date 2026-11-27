using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Augments;

[Serializable, NetSerializable]
public enum Wh40kAugmentBedKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum Wh40kAugmentBedZone : byte
{
    Core,
    Eyes,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
    LeftHand,
    RightHand,
    LeftFoot,
    RightFoot,
    LeftEye,
    RightEye,
    Head,
}

[Serializable, NetSerializable]
public sealed record Wh40kAugmentBedOption(
    NetEntity Item,
    string Prototype,
    Wh40kAugmentBedZone Zone,
    string? ExistingPart,
    bool RemoveInAdvance);

[Serializable, NetSerializable]
public sealed record Wh40kInstalledAugment(string Prototype, Wh40kAugmentBedZone Zone);

[Serializable, NetSerializable]
public sealed class Wh40kAugmentBedState(
    bool powered, Wh40kAugmentBedOption[] options, Wh40kInstalledAugment[] installed) : BoundUserInterfaceState
{
    public bool Powered { get; } = powered;
    public Wh40kAugmentBedOption[] Options { get; } = options;
    public Wh40kInstalledAugment[] Installed { get; } = installed;
}

[Serializable, NetSerializable]
public sealed class Wh40kAugmentBedInstall(NetEntity item, Wh40kAugmentBedZone zone) : BoundUserInterfaceMessage
{
    public NetEntity Item { get; } = item;
    public Wh40kAugmentBedZone Zone { get; } = zone;
}

