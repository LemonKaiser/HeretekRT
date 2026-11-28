using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>Distinguishes a Koronus planet's terrain from its atmosphere and low orbit.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusPlanetaryLayerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsSurface;
}
