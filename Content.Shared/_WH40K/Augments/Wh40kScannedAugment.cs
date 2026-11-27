using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Augments;

[Serializable, NetSerializable]
public readonly record struct Wh40kScannedAugment(
    string PrototypeId,
    Wh40kAugmentBedZone Zone);
