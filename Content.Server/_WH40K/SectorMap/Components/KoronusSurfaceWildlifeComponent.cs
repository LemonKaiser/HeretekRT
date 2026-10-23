using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>Natural hostile fauna allowed only outside the authored base on its own surface.</summary>
[RegisterComponent]
public sealed partial class KoronusSurfaceWildlifeComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KoronusPlanetSurfacePrototype> Surface;
}
