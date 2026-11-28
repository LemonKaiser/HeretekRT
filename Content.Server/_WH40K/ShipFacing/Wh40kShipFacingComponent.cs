using Robust.Shared.Map;

namespace Content.Server._WH40K.ShipFacing;

/// <summary>
/// Tracks the active cursor-facing world target for a pilot at a shuttle helm.
/// </summary>
[RegisterComponent, Access(typeof(Wh40kShipFacingSystem))]
public sealed partial class Wh40kShipFacingComponent : Component
{
    [ViewVariables]
    public MapCoordinates Target;

    [ViewVariables]
    public EntityUid Console;

    [ViewVariables]
    public TimeSpan Expires;
}
