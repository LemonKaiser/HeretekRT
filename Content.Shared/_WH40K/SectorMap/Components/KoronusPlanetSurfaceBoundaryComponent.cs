using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>
/// Networked visual data for the square enforced perimeter of a planetary surface.
/// The server performs non-colliding containment; clients use this for the local red warning band.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusPlanetSurfaceBoundaryComponent : Component
{
    [DataField, AutoNetworkedField]
    public Vector2 Minimum;

    [DataField, AutoNetworkedField]
    public Vector2 Maximum;
}
