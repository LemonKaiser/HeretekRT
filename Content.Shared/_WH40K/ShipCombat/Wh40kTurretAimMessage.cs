using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShipCombat;

/// <summary>
/// Instructs selected rotating turrets to track the gunner's cursor without firing.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kTurretAimMessage : BoundUserInterfaceMessage
{
    public List<NetEntity> Selected;
    public NetCoordinates Coordinates;

    public Wh40kTurretAimMessage(List<NetEntity> selected, NetCoordinates coordinates)
    {
        Selected = selected;
        Coordinates = coordinates;
    }
}
