/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Shared.GameStates;

namespace Content.Shared._CE.ZLevels.Core.Components;

/// <summary>
/// Automatically added to the map when it appears in zLevelNetwork.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class CEZMapComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid NetworkUid;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? MapAbove;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? MapBelow;

    [DataField, AutoNetworkedField]
    public int Depth;

    /// <summary>Relative width of a distant layer per Z-level of separation.</summary>
    [DataField, AutoNetworkedField]
    public float ViewShrink = 1f;

    /// <summary>
    /// This layer delegates grid altitude changes to its map's shuttle transfer system.
    /// </summary>
    [DataField]
    public bool ControlledGridAltitude;
}
