using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Bosses;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause(Dirty = true)]
public sealed partial class Wh40kChaosMarineComponent : Component
{
    [DataField, AutoNetworkedField] public MarineStance Stance;
    [DataField, AutoNetworkedField] public MarineAttack Attack;
    [DataField, AutoNetworkedField] public MarineAttack LastAttack;
    [DataField, AutoNetworkedField] public bool PhaseTwo;
    [DataField, AutoNetworkedField] public bool Engaged;
    [DataField, AutoNetworkedField] public bool Dying;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan DeathBurnAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan DeathEndsAt;
    [DataField, AutoNetworkedField] public bool TransitionProtected;
    [DataField, AutoNetworkedField] public int HeatStep;
    [DataField, AutoNetworkedField] public int Sequence;
    [DataField, AutoNetworkedField] public int Shot;
    [DataField, AutoNetworkedField] public float WeaponReturnDuration = Wh40kChaosMarineCombat.WeaponReturnSeconds;
    [DataField, AutoNetworkedField] public Vector2 Origin;
    [DataField, AutoNetworkedField] public Vector2 Aim;
    [DataField, AutoNetworkedField] public Vector2? MaceStop;
    [DataField, AutoNetworkedField] public EntityUid? Mace;
    [DataField, AutoNetworkedField] public EntityUid? MaceGrip;
    [DataField, AutoNetworkedField] public List<EntityUid> MaceLinks = [];
    [DataField, AutoNetworkedField] public Vector2[] Points = [];
    [DataField, AutoNetworkedField] public List<EntityUid> RocketTargets = [];
    [DataField, AutoNetworkedField] public List<int> ResolvedRockets = [];
    [DataField, AutoNetworkedField] public EntityUid? Anchor;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan StartedAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan ExecuteAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndsAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan RecoverUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextShotAt;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kBossArenaComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Anchor;
    [DataField, AutoNetworkedField] public Vector2 Center;
    [DataField, AutoNetworkedField] public Vector2 HalfSize = new(12.5f);
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause(Dirty = true)]
public sealed partial class Wh40kMarineEffectComponent : Component
{
    [DataField, AutoNetworkedField] public MarineEffect Kind;
    [DataField, AutoNetworkedField] public EntityUid? Boss;
    [DataField, AutoNetworkedField] public float Radius = 1;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan StartedAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndsAt;
}

[Serializable, NetSerializable]
public enum MarineStance : byte { Ranged, Melee }

[Serializable, NetSerializable]
public enum MarineAttack : byte
{
    None, R1, R2, R3, R4, R5, R6,
    M1, M2, M3, M4, M5, M6,
    S1 = 14, S2, S3, Punch, Jump, Summon, Transition, Switch,
    S4 = 24,
    E = 25,
}

[Serializable, NetSerializable]
public enum MarineEffect : byte { Impact, Explosion, Seal, Summon, Landing, Ricochet, BarrageImpact }
