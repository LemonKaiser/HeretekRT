using Robust.Shared.Map;

namespace Content.Server._WH40K.ShipCombat;

/// <summary>
/// Limits how fast a rotating ship turret turns; holds fire until laid within tolerance of its target.
/// </summary>
[RegisterComponent, Access(typeof(Wh40kTurretTraverseSystem))]
public sealed partial class Wh40kTurretTraverseComponent : Component
{
    [DataField]
    public float TurnRate = 90f;

    [DataField]
    public float Tolerance = 3f;

    [ViewVariables]
    public EntityCoordinates? AimTarget;

    [ViewVariables]
    public TimeSpan AimUntil;
}
