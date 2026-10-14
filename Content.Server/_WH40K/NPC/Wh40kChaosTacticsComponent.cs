using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.NPC;

[RegisterComponent, AutoGenerateComponentPause]
/// <summary>Includes weapon spread as well as a margin for moving allies.</summary>
public sealed partial class Wh40kChaosTacticsComponent : Component
{
    [DataField]
    public float RetreatRange;

    [DataField]
    public float RangedRange = 10f;

    [DataField]
    public Angle FriendlyFireSpread = Angle.FromDegrees(4);

    [DataField]
    public TimeSpan BurstDuration;

    [DataField]
    public TimeSpan BurstPause = TimeSpan.FromSeconds(1.5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan BurstEnd;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ResumeFire;

    [ViewVariables]
    public EntityUid? Target;

    [DataField] public float PatrolRadius = 6f;
    [ViewVariables] public EntityCoordinates? PatrolOrigin;
    [ViewVariables] public bool Patrolling;
    [ViewVariables] public int PatrolStops;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextPatrol;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan PatrolDeadline;

    [ViewVariables] public bool TargetVisible;
    [ViewVariables] public EntityCoordinates? LastSeenPosition;
    [ViewVariables] public EntityUid? IgnoredTarget;
    [ViewVariables] public int PathRetries;
    [ViewVariables] public int FireRecoveries;
    [ViewVariables] public int MovementRetries;
    [ViewVariables] public TimeSpan ObservedLastFire;
    [ViewVariables] public Vector2 ProgressPosition;
    [ViewVariables] public Vector2 PathProgressPosition;
    [ViewVariables] public Vector2 AvoidDirection;

    [ViewVariables] public EntityUid? ThreatTarget;
    [ViewVariables] public EntityCoordinates? ThreatPosition;
    [ViewVariables] public Vector2 DodgeDirection;
    [ViewVariables] public bool DodgeLeft;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan ThreatUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan ThreatObservedAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan DodgeUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextDodge;

    [DataField] public TimeSpan MemoryDuration = TimeSpan.FromSeconds(4);
    [DataField] public TimeSpan ThreatMemoryDuration = TimeSpan.FromSeconds(8);
    [DataField] public TimeSpan MeleeWindup;
    [ViewVariables] public EntityUid? MeleeWarningTarget;
    [ViewVariables] public MapCoordinates? MeleeWarningPosition;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextSightCheck;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan LastSeenAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan IgnoreUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextPathRetry;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan SilentSince;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextFireCheck;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan ProgressSince;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan AvoidUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan MovementRetryAfter;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan MeleeReadyAt;

    [ViewVariables]
    public bool EvadingWithoutTarget;

    [ViewVariables]
    public Vector2 TacticalDirection;

    [ViewVariables]
    public Vector2 EscapeDirection;

    [ViewVariables]
    public Vector2 SeparationDirection;

    public readonly List<Vector2> SeparationNeighbours = new();

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDirectionUpdate;
}
