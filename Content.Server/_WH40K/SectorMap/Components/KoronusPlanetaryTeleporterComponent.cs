using Content.Server._WH40K.SectorMap.Systems;

namespace Content.Server._WH40K.SectorMap.Components;

[RegisterComponent, Access(typeof(KoronusPlanetaryTeleporterSystem))]
public sealed partial class KoronusPlanetaryTeleporterComponent : Component
{
    [DataField]
    public bool PublicAccess = true;

    [DataField]
    public bool Locked;

    public string? SelectedTarget;
    public EntityUid? ActiveUser;
    public TimeSpan CompleteAt;
}
