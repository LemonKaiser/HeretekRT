using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShipPa;

[Serializable, NetSerializable]
public enum Wh40kShipPaSpeakerVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum Wh40kShipPaSpeakerState : byte
{
    Unpowered,
    Idle,
    Broadcasting,
    Damaged,
    Broken,
}
