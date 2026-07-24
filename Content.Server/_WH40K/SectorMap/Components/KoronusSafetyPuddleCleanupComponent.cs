namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Server-only timer for a puddle created in an area with the anti-flooding rule.
/// </summary>
[RegisterComponent]
public sealed partial class KoronusSafetyPuddleCleanupComponent : Component
{
    public TimeSpan CleanupAt;
}
