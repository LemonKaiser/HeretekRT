using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Prototypes;
using Content.Shared.Damage;

namespace Content.Server._WH40K.Bosses;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kSpaceBossComponent : Component
{
    [DataField] public float DetectionRadius = 1000f;
    [DataField] public float RetaliationRadius = 2000f;
    [DataField] public float RoamRadius = 300f;
    [DataField] public float HeadDistance = 16f;
    [DataField] public float BiteReach = 3f;
    [DataField] public float BiteRadius = 8f;
    /// <summary>Thickness of the bite around the first hull segment, independent of jaw reach.</summary>
    [DataField] public float BiteDepth = 0.75f;
    /// <summary>Exposed targets selected before the primary hit can destroy its obstruction.</summary>
    public readonly List<EntityUid> BiteVictims = new();
    [DataField] public DamageSpecifier BiteHullDamage = new() { DamageDict = { ["Structural"] = 1500 } };
    [DataField] public EntProtoId? DeathLoot;
    [DataField] public bool DeleteOnDeath = true;
    [DataField] public bool DeathHandled;
    [DataField] public EntProtoId? BoardingPrototype;
    [DataField] public int BoardingCount = 3;
    [DataField] public int MaxBoarders = 12;
    [DataField] public float BoardingRadius = 6f;
    [DataField] public TimeSpan BoardingCooldown = TimeSpan.FromSeconds(10);
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextBoarding;
    [ViewVariables] public EntityCoordinates? BoardingImpact;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan BoardingAt;
    [DataField] public EntProtoId? SporeMinePrototype;
    [DataField] public int MaxSporeMines = 50;
    [DataField] public TimeSpan SporeInterval = TimeSpan.FromSeconds(3);
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSpore;
    [ViewVariables] public HashSet<EntityUid> EvadedProjectiles = new();
    [DataField] public float TurnSpeed = 1.6f;
    [DataField] public float TurnAcceleration = 4f;
    [DataField] public float AvoidanceRadius = 2f;
    [DataField] public float PursuitLeadTime = 1.5f;
    [DataField] public float EvasionDistance = 45f;
    [DataField] public float EvasionDamageThreshold = 120f;
    /// <summary>Distance to withdraw after a bite or an unsuccessful attack run, in metres.</summary>
    [DataField] public float RetreatDistance = 65f;
    /// <summary>Lateral travel between attack runs, alternating sides after each withdrawal.</summary>
    [DataField] public float FlankDistance = 45f;
    /// <summary>Maximum time spent trying to bite once within striking distance, in seconds.</summary>
    [DataField] public float AttackRunDuration = 6f;
    /// <summary>Maximum duration of each withdrawal or flanking leg, in seconds.</summary>
    [DataField] public float RepositionDuration = 6f;
    [ViewVariables] public Wh40kSpaceBossPhase Phase;
    [ViewVariables] public Vector2 AttackDirection;
    [ViewVariables] public float AttackSide = 1f;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan PhaseUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan AttackRunUntil;
    /// <summary>After a defensive break, finish repositioning and give the next approach time to attack.</summary>
    [ViewVariables] public bool CommitNextApproach;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan AttackCommitUntil;
    [ViewVariables] public EntityUid? Target;
    [ViewVariables] public EntityCoordinates? Home;
    [ViewVariables] public EntityCoordinates? Destination;
    [ViewVariables] public Vector2? LookPosition;
    [ViewVariables] public float TurnVelocity;
    [ViewVariables] public EntityUid? RetaliationTarget;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan RetaliationUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ManeuverUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextEvasion;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan PressureUntil;
    [ViewVariables] public float PressureDamage;
    [ViewVariables] public Vector2? ThreatPosition;
}

public enum Wh40kSpaceBossPhase : byte
{
    Approach,
    Retreat,
    Flank,
}
