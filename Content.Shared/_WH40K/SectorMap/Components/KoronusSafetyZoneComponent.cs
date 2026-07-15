using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>
/// A safe area anchored to a moving facility grid. The server uses the grid transform as the
/// authoritative centre and the client draws it only when <see cref="ShowBoundary"/> is enabled.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusSafetyZoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<KoronusSafetyProfilePrototype> Profile;

    [DataField, AutoNetworkedField]
    public float Radius;

    [DataField, AutoNetworkedField]
    public bool ShowBoundary = true;
}
