using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Blood;

/// <summary>
///     Synchronizes the visual severity of an active bloodstream bleed to clients.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KBleedingVisualComponent : Component
{
    [DataField, AutoNetworkedField]
    public WH40KBleedingVisualSeverity Severity;
}

public enum WH40KBleedingVisualSeverity : byte
{
    Minor,
    Severe,
}
