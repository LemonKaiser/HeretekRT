using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShipFacing;

/// <summary>
/// Sent by a pilot holding the cursor-facing key: the world point under their cursor, or null on release.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kShipFacingTargetEvent : EntityEventArgs
{
    public MapCoordinates? Target;

    public Wh40kShipFacingTargetEvent(MapCoordinates? target)
    {
        Target = target;
    }
}
