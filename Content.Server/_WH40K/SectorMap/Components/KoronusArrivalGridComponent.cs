using Content.Server._WH40K.SectorMap.Systems;

namespace Content.Server._WH40K.SectorMap.Components;

[RegisterComponent, Access(typeof(KoronusArrivalSystem))]
public sealed partial class KoronusArrivalGridComponent : Component
{
    [DataField]
    public long LeaseId;
}
