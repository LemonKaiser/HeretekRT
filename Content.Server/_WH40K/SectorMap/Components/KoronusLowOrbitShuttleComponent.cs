namespace Content.Server._WH40K.SectorMap.Components;

[RegisterComponent, UnsavedComponent]
public sealed partial class KoronusLowOrbitShuttleComponent : Component
{
    public TimeSpan UnoccupiedTime;
    public TimeSpan? LastUnoccupiedCheck;
}
