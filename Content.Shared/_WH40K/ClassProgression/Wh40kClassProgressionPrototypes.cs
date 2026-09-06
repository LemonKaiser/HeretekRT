using System.Numerics;
using Content.Shared.Tag;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.ClassProgression;

public static class Wh40kClassProgressionConstants
{
    public const int TreeVersion = 5;
    public const int SpecializationsPerClass = 2;
    public const int LegacySkillsPerSpecialization = 20;
    public const int SkillsPerSpecialization = 25;
    public const int MaximumSkillsPerClass = SpecializationsPerClass * SkillsPerSpecialization;
    public const int MaximumActiveAbilities = 4;
    public const int SkillCost = 1;
}

/// <summary>
/// One of the two skill doctrines belonging to an account class.
/// </summary>
[Prototype]
public sealed partial class Wh40kClassSpecializationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<Wh40kCharacterClassPrototype> Class { get; private set; }

    [DataField(required: true)]
    public int Order { get; private set; }

    /// <summary>
    /// Number of visible, purchasable nodes in this specialization. Legacy trees remain at twenty nodes while
    /// the Soldier rework can opt into the twenty-five node layout without invalidating the other classes.
    /// </summary>
    [DataField]
    public int SkillCount { get; private set; } = Wh40kClassProgressionConstants.LegacySkillsPerSpecialization;

    [DataField(required: true)]
    public LocId Name { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Description { get; private set; } = default!;

    [DataField(required: true)]
    public ResPath Icon { get; private set; } = default!;
}

/// <summary>
/// Persistent entitlement described by content data. Runtime state never belongs here.
/// </summary>
[Prototype]
public sealed partial class Wh40kClassSkillPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<Wh40kClassSpecializationPrototype> Specialization { get; private set; }

    [DataField(required: true)]
    public int Order { get; private set; }

    [DataField(required: true)]
    public int MinimumLevel { get; private set; }

    [DataField]
    public int Cost { get; private set; } = Wh40kClassProgressionConstants.SkillCost;

    [DataField]
    public ProtoId<Wh40kClassSkillPrototype>? Prerequisite { get; private set; }

    /// <summary>
    /// Optional canonical persistent entitlement. A mirror node may point at the skill that owns the purchase and
    /// its action; buying either presentation node records only this canonical id.
    /// </summary>
    [DataField]
    public ProtoId<Wh40kClassSkillPrototype>? SharedPurchase { get; private set; }

    /// <summary>
    /// Presentation-only edges. Authorization uses only <see cref="Prerequisite"/>.
    /// </summary>
    [DataField]
    public List<ProtoId<Wh40kClassSkillPrototype>> Connections { get; private set; } = new();

    [DataField(required: true)]
    public Vector2 DisplayPosition { get; private set; }

    [DataField(required: true)]
    public Wh40kClassSkillKind Kind { get; private set; }

    [DataField(required: true)]
    public List<ProtoId<Wh40kClassSkillEffectPrototype>> Effects { get; private set; } = new();

    [DataField]
    public List<ProtoId<TagPrototype>> RequiredEquipmentTags { get; private set; } = new();

    [DataField(required: true)]
    public ResPath Icon { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Description { get; private set; } = default!;

    /// <summary>
    /// Coming-soon nodes are visible content but cannot be bought until their server handler is shipped.
    /// </summary>
    [DataField]
    public Wh40kClassContentAvailability Availability { get; private set; } =
        Wh40kClassContentAvailability.ComingSoon;
}

/// <summary>
/// Closed, data-only effect descriptor. The enum is the dispatch boundary; YAML never names a C# type.
/// </summary>
[Prototype]
public sealed partial class Wh40kClassSkillEffectPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Wh40kClassSkillEffectKind Kind { get; private set; }

    /// <summary>
    /// Characteristic changed by a stat modifier. Other handlers must leave this unset.
    /// </summary>
    [DataField]
    public Wh40kCharacteristic? Characteristic { get; private set; }

    /// <summary>
    /// Base value applied from the immutable prototype. Runtime rarity always resolves from this value.
    /// </summary>
    [DataField]
    public int Magnitude { get; private set; }

    /// <summary>
    /// Hard absolute cap after rarity scaling.
    /// </summary>
    [DataField]
    public int MaximumMagnitude { get; private set; }

    /// <summary>
    /// A separately named secondary parameter for closed multi-parameter mechanics. It deliberately does not
    /// participate in rarity scaling, so authored combat bounds remain explicit.
    /// </summary>
    [DataField]
    public int SecondaryMagnitude { get; private set; }

    [DataField]
    public int TertiaryMagnitude { get; private set; }

    /// <summary>
    /// Stamina paid when an active effect starts. It is distinct from effect magnitude so a damage bonus cannot
    /// accidentally become an ability cost.
    /// </summary>
    [DataField]
    public float StaminaCost { get; private set; }

    [DataField]
    public Wh40kClassModifierCategory ModifierCategory { get; private set; }

    [DataField]
    public Wh40kClassModifierStacking Stacking { get; private set; } =
        Wh40kClassModifierStacking.StrongestByCategory;

    /// <summary>
    /// Action prototype granted exactly once to the current body by a grantAction effect.
    /// </summary>
    [DataField]
    public EntProtoId? Action { get; private set; }

    /// <summary>
    /// Optional companion action for a single ability slot. Used only by the ordered Nest Relocation route.
    /// </summary>
    [DataField]
    public EntProtoId? SecondaryAction { get; private set; }

    /// <summary>
    /// Base duration exposed to a typed handler. Zero means that the effect is profile-bound.
    /// </summary>
    [DataField]
    public TimeSpan Duration { get; private set; }

    /// <summary>
    /// Hard duration cap after rarity scaling. Zero disables duration scaling.
    /// </summary>
    [DataField]
    public TimeSpan MaximumDuration { get; private set; }

    /// <summary>
    /// Closed safety contract checked before an Action starts and again by its concrete handler.
    /// </summary>
    [DataField]
    public Wh40kClassEffectSafety Safety { get; private set; }

    [DataField]
    public Wh40kClassContentAvailability Availability { get; private set; } =
        Wh40kClassContentAvailability.ComingSoon;

    /// <summary>
    /// Closed server operation implemented by the class runtime. This is deliberately separate from the
    /// persistent effect id: content may tune parameters, but cannot name or instantiate arbitrary code.
    /// </summary>
    [DataField]
    public Wh40kClassRuntimeMechanic Mechanic { get; private set; }

    /// <summary>
    /// Conditions which make the effect interruptible or restrict its valid target. Every flag is
    /// re-evaluated by the authoritative server at the moment the effect starts or reacts to an event.
    /// </summary>
    [DataField]
    public Wh40kClassCounterplay Counterplay { get; private set; }

    /// <summary>
    /// Maximum server-authoritative reach in tiles. Zero is valid only for self/profile effects.
    /// </summary>
    [DataField]
    public float Range { get; private set; }

    /// <summary>
    /// Hard target cap for area effects. Zero means that the effect is not an area operation.
    /// </summary>
    [DataField]
    public int MaximumTargets { get; private set; }

    /// <summary>
    /// Internal cooldown owned by the current body. It is never persisted to the account database.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown { get; private set; }

    /// <summary>
    /// Optional HUD slot for a class action. Equal slots are mutually exclusive and the highest priority effect
    /// owns the slot, allowing an upgrade to replace its base action instead of creating another button.
    /// </summary>
    [DataField]
    public byte? ActionSlot { get; private set; }

    [DataField]
    public int ActionPriority { get; private set; }
}

/// <summary>
/// Whitelist entry allowing one suitable held or equipped item's immutable rarity roll to scale one effect parameter.
/// </summary>
[Prototype]
public sealed partial class Wh40kClassRarityModifierPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<Wh40kClassSkillEffectPrototype> Effect { get; private set; }

    [DataField(required: true)]
    public ProtoId<TagPrototype> EquipmentTag { get; private set; }

    /// <summary>
    /// Optional exact rarity whitelist. An empty list accepts every rarity at or above MinimumTier.
    /// </summary>
    [DataField]
    public List<ProtoId<ItemRarityPrototype>> Rarities { get; private set; } = new();

    [DataField]
    public byte MinimumTier { get; private set; } = 2;

    [DataField(required: true)]
    public Wh40kClassRarityParameter Parameter { get; private set; }

    /// <summary>
    /// Maximum percentage accepted from ItemRarityComponent.BonusPercent.
    /// </summary>
    [DataField(required: true)]
    public float MaximumBonusPercent { get; private set; }
}

public enum Wh40kClassSkillKind : byte
{
    Passive,
    Active,
}

public enum Wh40kClassContentAvailability : byte
{
    ComingSoon,
    Enabled,
}

/// <summary>
/// The only effect categories accepted from prototype data.
/// </summary>
public enum Wh40kClassSkillEffectKind : byte
{
    StatModifier,
    GrantAction,
    StatusReaction,
    TargetMark,
    AreaCommand,
    DeviceInteraction,
    NpcPressure,
}

/// <summary>
/// Non-stacking categories. Within one characteristic/category pair only the strongest layer wins.
/// </summary>
public enum Wh40kClassModifierCategory : byte
{
    Core,
    Offense,
    Defense,
    Mobility,
    Medical,
    Technical,
    Command,
}

public enum Wh40kClassModifierStacking : byte
{
    StrongestByCategory,
}

/// <summary>
/// Safety semantics are data, not names of C# handlers.
/// </summary>
public enum Wh40kClassEffectSafety : byte
{
    SelfOnly,
    OffensiveDamage,
    OffensiveStamina,
    OffensiveControl,
    DeviceInteraction,
    AreaEffect,
    Mobility,
    NpcOnly,
    Supportive,
}

public enum Wh40kClassRarityParameter : byte
{
    Magnitude,
    Duration,
}

/// <summary>
/// Finite operations understood by the stage-4 runtime. These are gameplay families rather than handler
/// type names; individual skill identity and tuning remain in immutable prototypes.
/// </summary>
public enum Wh40kClassRuntimeMechanic : byte
{
    None,
    ProfileModifier,
    IncomingDamageReaction,
    IncomingStaminaReaction,
    KnockbackReaction,
    MovementModifier,
    MeleeDamageModifier,
    MeleeTempoModifier,
    GunControlModifier,
    GunTempoModifier,
    ReloadModifier,
    HealingModifier,
    ServiceModifier,
    GuardPreparation,
    AttackPreparation,
    TimedOffenseStance,
    StationaryStance,
    SuppressionMode,
    Cloak,
    TargetMark,
    NpcPressure,
    AreaPressure,
    Intercept,
    DashToEntity,
    DashToPoint,
    Distraction,
    MedicalProtocol,
    TransferHeldItem,
    DeployHeldItem,
    PullAlly,
    TriageArea,
    DeviceScan,
    DeviceBypass,
    DeviceDisable,
    DeviceRepair,
    WeaponCoating,
    Finisher,
    PrivateInformation,
    CommandAura,
    CommandStamina,
    CommandBeacon,
    PressureAmplifier,
    CriticalThresholdModifier,
    StaminaCriticalThresholdModifier,
    StaminaDecayModifier,
    StunDurationReduction,
    KnockedDownDurationReduction,
    GunHeldPenaltyCompensation,
    GunShotPenaltyCompensation,
    GunMovingSpreadCompensation,
    GunShotPenaltyDurationOverride,
    GunCameraRecoilModifier,
    HeavyMeleeStaminaCostModifier,
    DashCostOverride,
    DashCooldownOverride,
    DashRangeOverride,
    DashAfterSpeed,
    DashShotCostReduction,
    FirePosition,
    Barrage,
    MeleeBreach,
    CombatDash,
    AssaultJump,
    GunAngleDecayModifier,
    GunAngleIncreaseModifier,
    GunMaxAngleModifier,
    GunProjectileSpeedModifier,
    SemiAutoGunTempoModifier,
    WeaponStaminaDamageReduction,
    LowHealthGunDamageBonus,
    StationaryGunControlModifier,
    HoldBreath,
    VerdictShot,
    NestRoute,
    HitConfirmation,
}

/// <summary>
/// Composable, closed counter-play contract. It describes checks, never arbitrary execution.
/// </summary>
[Flags]
public enum Wh40kClassCounterplay : uint
{
    None = 0,
    RequiresStationary = 1 << 0,
    RequiresFrontArc = 1 << 1,
    RequiresLowHealth = 1 << 2,
    RequiresMarkedTarget = 1 << 3,
    RequiresInjuredTarget = 1 << 4,
    RequiresBackstab = 1 << 5,
    RequiresCloak = 1 << 6,
    BreakOnMove = 1 << 7,
    BreakOnDamage = 1 << 8,
    BreakOnAttack = 1 << 9,
    BreakOnItemChange = 1 << 10,
    RequiresLineOfSight = 1 << 11,
    RequiresDoAfter = 1 << 12,
    RequiresConsent = 1 << 13,
    RequiresOpenPanel = 1 << 14,
    RequiresHackableTarget = 1 << 15,
    RequiresConsumable = 1 << 16,
    RequiresDownedTarget = 1 << 17,
    RequiresAlly = 1 << 18,
    RequiresHostile = 1 << 19,
    NpcOnly = 1 << 20,
    PlayerNonLethal = 1 << 21,
}
