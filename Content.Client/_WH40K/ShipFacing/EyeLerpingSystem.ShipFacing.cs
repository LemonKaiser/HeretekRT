using Content.Client._WH40K.ShipFacing;

namespace Content.Client.Eye;

public sealed partial class EyeLerpingSystem
{
    [Dependency] private Wh40kShipFacingSystem _wh40kShipFacing = default!;

    private Angle Wh40kFacingRotation(EntityUid uid, Angle live) => _wh40kShipFacing.HoldEyeRotation(uid, live);
}
