using Content.Shared._WH40K.Bosses;
using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Player;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WH40K.Bosses;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kChaosMarineBrainComponent : Component
{
    [DataField] public float DetectionInterval = .1f;
    [ViewVariables] public int ProjectilesFired;
    [ViewVariables] public EntityUid? Target;
    [ViewVariables] public EntityUid? ActionTarget;
    [ViewVariables] public bool Suspended;
    [ViewVariables] public int WavesQueued;
    [ViewVariables] public int WavesDone;
    [ViewVariables] public int PendingWave;
    [ViewVariables] public int PendingSummonCount;
    [ViewVariables] public int RangedSeries;
    [ViewVariables] public int CancellationSequence;
    [ViewVariables] public MarineAttack Previous;
    [ViewVariables] public int Repeats;
    [ViewVariables] public readonly Dictionary<MarineAttack, int> PatternUses = new();
    [ViewVariables] public bool DeathFinished;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan ShotRetryUntil;
    [ViewVariables] public bool ActionHit;
    [ViewVariables] public bool SummonPlaced;
    [ViewVariables] public bool SealsPrimed;
    [ViewVariables] public bool DashPending;
    [ViewVariables] public Vector2? DashPrevious;
    [ViewVariables] public bool Landed;
    [ViewVariables] public bool Activated;
    [ViewVariables] public bool ManualAttack;
    [ViewVariables] public float CooldownSpeed = 1;
    [ViewVariables] public int OrbitSide = 1;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextMovement;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextOrbitChange;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextPatrol;
    [ViewVariables] public float PreviousProgress;
    [ViewVariables] public readonly List<DistanceJoint> MaceJoints = [];
    [ViewVariables] public Vector2 MacePreviousGoal;
    [ViewVariables] public float MaceInertiaScale = 1;
    [ViewVariables] public int MaceStrike = -1;
    [ViewVariables] public float MaceLength;
    [ViewVariables] public int MeleeStage;
    [ViewVariables] public Vector2 MeleeReturn;
    [ViewVariables] public float ThrowReturnReach;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan MeleeStageAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan MeleeNextAt;
    [ViewVariables] public readonly List<EntityUid> Targets = [];
    [ViewVariables] public readonly HashSet<EntityUid> Hit = [];
    public readonly HashSet<ICommonSession> Audience = [];
    [ViewVariables] public readonly Dictionary<EntityUid, TimeSpan> CloseSince = new();
    [ViewVariables] public readonly List<MarineSpawn> Summons = [];
    [ViewVariables] public readonly Dictionary<int, int> SpawnedMinions = [];
    [ViewVariables] public readonly List<MarineSeal> Seals = [];
    [ViewVariables] public Entity<AudioComponent>? Voice;
    [ViewVariables] public int VoicePriority;
    [ViewVariables] public string? LastVoice;
    [ViewVariables] public int PendingVoice;
    [ViewVariables] public bool PendingDownVoice;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextThink;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextSummonRetry;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextAttack;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextPattern;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextBasicRanged;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextBasicMelee;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextStance;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextRocket;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextJump;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextSmoke;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextPunch;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan FireDamageUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextShot;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextAim;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan EmptySince;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan VoiceUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextVoice;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextKillVoice;
}

public readonly record struct MarineSpawn(EntityUid Circle, string Prototype, EntityCoordinates Center, TimeSpan At, int Wave, int TypeIndex, int Count);
public readonly record struct MarineSeal(EntityUid Effect, EntityCoordinates Position);

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kMarineShotComponent : Component
{
    [DataField] public EntityUid? Boss;
    [DataField] public EntityCoordinates Destination;
    [DataField] public bool Seal;
    [DataField] public bool Rocket;
    [DataField] public bool GroundRocket;
    [DataField] public int RocketIndex;
    [DataField] public EntityUid? HomingTarget;
    [DataField] public bool Spent;
    [DataField] public int Sequence;
    [DataField] public bool Independent;
    [DataField] public int CancellationSequence;
    [DataField] public Vector2 PreviousPosition;
    [DataField] public int BouncesRemaining;
    [ViewVariables] public EntityUid? LastBounce;
    [ViewVariables] public Vector2 ContactNormal;
    [ViewVariables] public Vector2 BounceNormal;
    [ViewVariables] public Vector2 BouncePosition;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan BounceIgnoreUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan Expires;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kMarineMinionComponent : Component
{
    [DataField] public EntityUid? Boss;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan ReadyAt;
}
