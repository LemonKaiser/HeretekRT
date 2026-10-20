using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.NPC;

[RegisterComponent, AutoGenerateComponentPause]
/// <summary>Includes weapon spread as well as a margin for moving allies.</summary>
public sealed partial class Wh40kChaosTacticsComponent : Component
{
    [DataField] public bool ExternallyDirected;
    [ViewVariables] public bool DirectedMovementLocked = true;
    [ViewVariables] public Vector2 DirectedDirection;
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

    [ViewVariables] public EntityUid? AssignedTarget;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan TargetCommittedUntil;

    [ViewVariables] public EntityCoordinates? CoverPosition;
    [ViewVariables] public EntityCoordinates? PeekPosition;
    [ViewVariables] public EntityUid? CoverTarget;
    [ViewVariables] public bool CoverPeeking;
    [ViewVariables] public bool CoverEntering;
    [ViewVariables] public Vector2 CoverProgressPosition;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextCoverSearch;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextCoverValidation;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan CoverExpiresAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan CoverPhaseUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan CoverPeekStartedAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan CoverFirstFireAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan CoverProgressAt;

    [ViewVariables] public EntityUid? LastLostAlly;
    [ViewVariables] public int RecentLosses;
    [ViewVariables] public EntityCoordinates? RegroupOrigin;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan LossWindowUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan RegroupUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextRegroup;

    [ViewVariables] public EntityCoordinates? CampPosition;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan StationarySince;

    [DataField] public float PatrolRadius = 6f;
    [ViewVariables] public EntityCoordinates? PatrolOrigin;
    [ViewVariables] public bool Patrolling;
    [ViewVariables] public int PatrolStops;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextPatrol;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan PatrolDeadline;

    [ViewVariables] public bool TargetVisible;
    [ViewVariables] public bool ClearShot;
    [ViewVariables] public bool DirectPathClear;
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
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan DodgeSideUntil;
    [ViewVariables] public EntityUid? DodgeShooter;

    // Thresholds above one require sustained aimed gunfire, rather than individual damage/melee events.
    [DataField] public int DodgeShotThreshold = 1;
    [DataField] public TimeSpan DodgeShotWindow = TimeSpan.FromSeconds(1.5);
    [DataField] public TimeSpan DodgeDuration = TimeSpan.FromSeconds(.75);
    [DataField] public TimeSpan DodgeCooldown = TimeSpan.FromSeconds(1.2);
    [ViewVariables] public int DodgeShots;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan DodgeShotWindowUntil;

    [DataField] public TimeSpan MemoryDuration = TimeSpan.FromSeconds(16);
    [DataField] public TimeSpan ThreatMemoryDuration = TimeSpan.FromSeconds(16);
    [DataField] public TimeSpan SearchWaitDuration = TimeSpan.FromSeconds(4);
    [DataField] public TimeSpan MeleeWindup;
    [DataField] public TimeSpan MeleeStunDuration;
    [ViewVariables] public EntityUid? MeleeWarningTarget;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextSightCheck;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextTargetCheck;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan LastSeenAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan SearchArrivedAt;
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
