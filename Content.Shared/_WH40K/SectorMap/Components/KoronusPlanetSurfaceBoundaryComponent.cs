using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>
/// Networked visual data for the square enforced perimeter of a planetary surface.
/// Both sides use this for mover containment; clients also draw the local red warning band.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusPlanetSurfaceBoundaryComponent : Component
{
    [DataField, AutoNetworkedField]
    public Vector2 Minimum;

    [DataField, AutoNetworkedField]
    public Vector2 Maximum;
}
