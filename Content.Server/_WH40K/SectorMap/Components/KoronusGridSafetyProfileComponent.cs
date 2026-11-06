using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Applies only the station section of a protection profile to this facility grid.
/// Area rules require a system/surface assignment or a separate <c>KoronusSafetyZoneComponent</c>.
/// </summary>
[RegisterComponent, Access(typeof(KoronusSectorRuleSystem), typeof(KoronusSafetyPolicySystem))]
public sealed partial class KoronusGridSafetyProfileComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KoronusSafetyProfilePrototype> Profile;
}
