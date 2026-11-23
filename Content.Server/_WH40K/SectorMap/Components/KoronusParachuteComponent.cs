namespace Content.Server._WH40K.SectorMap.Components;

[RegisterComponent]
public sealed partial class KoronusParachuteComponent : Component;

[RegisterComponent, UnsavedComponent]
public sealed partial class KoronusOrbitFallComponent : Component
{
    public EntityUid Orbit;
    public EntityUid Atmosphere;
    public EntityUid Surface;
    public EntityUid? Canopy;
    public bool AddedZPhysics;
    public bool HadVelocityEvent;
}
