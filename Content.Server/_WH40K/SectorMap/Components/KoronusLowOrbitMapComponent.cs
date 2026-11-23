namespace Content.Server._WH40K.SectorMap.Components;

[RegisterComponent, UnsavedComponent]
public sealed partial class KoronusLowOrbitMapComponent : Component
{
    public EntityUid Surface;
    public EntityUid Atmosphere;
    public string BodyId = string.Empty;
}
