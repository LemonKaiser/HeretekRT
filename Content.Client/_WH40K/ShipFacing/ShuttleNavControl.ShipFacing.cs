using Content.Client._WH40K.ShipFacing;
using Content.Shared._WH40K.ShipFacing;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private readonly Wh40kHeldAngle _wh40kFacingRotation = new();

    private Angle Wh40kFacingRotation(Angle live) =>
        EntManager.System<Wh40kShipFacingSystem>().HoldRotation(_wh40kFacingRotation, live);
}
