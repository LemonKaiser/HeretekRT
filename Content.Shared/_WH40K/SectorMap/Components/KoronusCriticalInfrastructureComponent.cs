using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>Authored critical equipment locked when its facility is configured as protected.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusCriticalInfrastructureComponent : Component
{
    /// <summary>Only audited native activation handlers that open a window without changing the machine.</summary>
    [DataField, AutoNetworkedField]
    public bool AllowUiActivation;
}
