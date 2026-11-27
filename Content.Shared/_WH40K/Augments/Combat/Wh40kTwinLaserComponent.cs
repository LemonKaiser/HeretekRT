namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Paired forearm laser emitter: holding one in each hand synchronizes both shots while doubling spread.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kTwinLaserComponent : Component
{
    [DataField]
    public float Spread = 2f;
}
