using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// A safety profile bound to one facility grid. Unlike <c>KoronusSafetyZoneComponent</c>, it has
/// no radius or client visual and therefore cannot spill over into the surrounding sector space.
/// </summary>
[RegisterComponent, Access(typeof(KoronusSectorRuleSystem), typeof(KoronusSafetyPolicySystem))]
public sealed partial class KoronusGridSafetyProfileComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KoronusSafetyProfilePrototype> Profile;
}
