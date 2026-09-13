using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Invisibility;

/// <summary>
/// Enables the client-side psyker invisibility shader. No prototype currently adds this component.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KPsykerInvisibilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public float ShaderVisibility = 0.01f;
}
