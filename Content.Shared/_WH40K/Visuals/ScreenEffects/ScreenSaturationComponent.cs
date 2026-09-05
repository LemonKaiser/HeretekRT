using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Visuals.ScreenEffects;

/// <summary>
/// Sets a target saturation for the local player's screen. A value of one is neutral.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ScreenSaturationComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Saturation = 1f;

    [DataField, AutoNetworkedField]
    public float FadeRate = 0.1f;
}
