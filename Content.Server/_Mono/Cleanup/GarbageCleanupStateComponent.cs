using System.Numerics;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Persistent eligibility time and runtime movement sample for explicit garbage.
/// </summary>
[RegisterComponent]
public sealed partial class GarbageCleanupStateComponent : Component
{
    [DataField]
    public TimeSpan EligibleFor = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastEvaluation = TimeSpan.Zero;

    [ViewVariables]
    public bool EligibilityActive;

    [ViewVariables]
    public EntityUid LastParent = EntityUid.Invalid;

    [ViewVariables]
    public Vector2 LastLocalPosition;

    [ViewVariables]
    public bool HasPositionSample;
}
