using Content.Server._WH40K.SectorMap.Systems;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>Prevents leaving one pad and immediately occupying another pad on the same planet.</summary>
[RegisterComponent, Access(typeof(KoronusPlanetarySystem))]
public sealed partial class KoronusPlanetaryLandingCooldownComponent : Component
{
    public string BodyId = string.Empty;
    public TimeSpan Until;
}
