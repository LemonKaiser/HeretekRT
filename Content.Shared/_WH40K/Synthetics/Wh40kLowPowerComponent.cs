using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Synthetics;

/// <summary>
/// Applied to a cybernetic/IPC chassis when its battery is empty or missing:
/// the body remains alive and conscious, crawls at 25% speed, and runs on a single switchable arm.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kLowPowerComponent : Component
{
    [DataField]
    public float Speed = 0.25f;

    [DataField, AutoNetworkedField]
    public EntityUid? PoweredArm;

    [DataField]
    public TimeSpan NextSwitch;
}
