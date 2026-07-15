namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Persistent eligibility timer for abandoned NPC cleanup.
/// </summary>
[RegisterComponent]
public sealed partial class MobCleanupStateComponent : Component
{
    [DataField]
    public TimeSpan EligibleFor = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastEvaluation = TimeSpan.Zero;
}

/// <summary>
///     Permanently protects a body that has ever been controlled by a player or a mind.
/// </summary>
[RegisterComponent]
public sealed partial class CleanupPlayerProtectedComponent : Component;

/// <summary>
///     Persistent grace timer for general loose-object space cleanup.
/// </summary>
[RegisterComponent]
public sealed partial class SpaceCleanupStateComponent : Component
{
    [DataField]
    public TimeSpan EligibleFor = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastEvaluation = TimeSpan.Zero;
}
