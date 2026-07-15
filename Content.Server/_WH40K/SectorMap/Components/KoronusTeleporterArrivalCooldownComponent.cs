using Content.Server._WH40K.SectorMap.Systems;

namespace Content.Server._WH40K.SectorMap.Components;

[RegisterComponent, Access(typeof(KoronusPlanetaryTeleporterSystem))]
public sealed partial class KoronusTeleporterArrivalCooldownComponent : Component
{
    public TimeSpan Until;
}
