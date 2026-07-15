using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>
/// Identifies a loaded planetary system map for client-only orbital scenery.
/// It has no gameplay, collision or movement responsibilities.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusPlanetarySystemVisualComponent : Component
{
    [DataField, AutoNetworkedField]
    public string SystemId = string.Empty;

    /// <summary>
    /// Server-selected per-round placement angles for bodies with randomized positions. Keeping
    /// these on the map component makes the server, NAV and decorative background use one value.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, float> PositionAngleOverrides = new();
}
