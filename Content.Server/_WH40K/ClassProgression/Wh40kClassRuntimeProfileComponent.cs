using Content.Server._WH40K.Progression;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Unsaved, body-local projection of an account's permanent class entitlements.
/// Every collection is rebuilt or reconciled from immutable prototypes and cached account records.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassRuntimeProfileComponent : Component
{
    public NetUserId UserId;
    public Wh40kAccountRpgRecord? Account;
    public Wh40kAccountClassProgressRecord? Progress;

    public readonly Dictionary<string, Wh40kResolvedClassEffect> ActiveEffects =
        new(StringComparer.Ordinal);

    public readonly Dictionary<string, EntityUid> GrantedActions =
        new(StringComparer.Ordinal);

    public readonly Dictionary<string, EntProtoId> GrantedActionPrototypes =
        new(StringComparer.Ordinal);

    public readonly HashSet<EntityUid> RelayedEquipment = new();
    public readonly Dictionary<string, TimeSpan> CooldownEnds = new(StringComparer.Ordinal);
    public readonly Dictionary<string, Wh40kClassRuntimeState> RuntimeStates = new(StringComparer.Ordinal);

    public readonly Dictionary<Wh40kClassRuntimeModifierKey, Wh40kClassRuntimeModifier> ProfileModifierLayers = new();
    public readonly Dictionary<Wh40kClassRuntimeModifierKey, Wh40kClassRuntimeModifier> TimedModifierLayers = new();

    public TimeSpan LastMarksmanShotAt;
    public TimeSpan LastStationaryResetAt;

    public IReadOnlyDictionary<Wh40kCharacteristic, int> TalentModifiers { get; internal set; } =
        new Dictionary<Wh40kCharacteristic, int>();

    public IReadOnlyDictionary<Wh40kCharacteristic, int> EquipmentModifiers { get; internal set; } =
        new Dictionary<Wh40kCharacteristic, int>();

    public IReadOnlyDictionary<Wh40kCharacteristic, int> TemporaryModifiers { get; internal set; } =
        new Dictionary<Wh40kCharacteristic, int>();

    // These baselines are captured only while the class projection owns the corresponding body field. They make
    // the projection reversible when a player dies, changes body, loses the class profile, or reconnects.
    public FixedPoint2? CriticalThresholdBaseline;
    public float? StaminaCriticalThresholdBaseline;
    public float? StaminaDecayBaseline;
}

/// <summary>
/// Marks an Action instance created by the class runtime. It cannot survive removal of its owning profile.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassGrantedActionComponent : Component
{
    public EntityUid Body;
    public string EffectId = string.Empty;
    public Wh40kClassEffectSafety Safety;
    public bool IsSecondary;
}

public sealed record Wh40kClassRuntimeState(
    EntityUid? Target,
    TimeSpan ExpiresAt,
    EntityCoordinates? Origin = null,
    int Charges = 1);

/// <summary>
/// Unsaved relay placed only on a currently held/equipped supporting item. Hit and shot handlers still
/// verify that the event user is the body and that the item remains in its live equipment snapshot.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassEquipmentRelayComponent : Component
{
    public EntityUid Body;
    public readonly HashSet<string> EffectIds = new(StringComparer.Ordinal);
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassCloakRuntimeComponent : Component
{
    public bool AddedStealth;
    public bool AddedStealthOnMove;
    public bool OriginalEnabled;
    public float OriginalVisibility = 1f;
}

public readonly record struct Wh40kClassNpcPressureSource(
    EntityUid Source,
    string EffectId);

public sealed record Wh40kClassNpcPressureState(
    TimeSpan ExpiresAt,
    int Magnitude,
    bool AddedHostility,
    Wh40kClassModifierCategory Category);

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassNpcPressureComponent : Component
{
    public readonly Dictionary<Wh40kClassNpcPressureSource, Wh40kClassNpcPressureState> Sources = new();
}

/// <summary>
/// The single winning Overseer order in a non-stacking category. It is deliberately body-local and
/// short-lived: the resolver rebuilds it from current sessions, map position and party revision.
/// </summary>
public sealed record Wh40kClassCommandRecipientState(
    EntityUid Source,
    NetUserId SourceUserId,
    string EffectId,
    Wh40kClassModifierCategory Category,
    Wh40kClassRuntimeMechanic Mechanic,
    int Magnitude,
    TimeSpan ExpiresAt,
    EntityUid? Target,
    Guid? PartyId,
    long PartyRevision);

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassCommandRecipientComponent : Component
{
    public readonly Dictionary<Wh40kClassModifierCategory, Wh40kClassCommandRecipientState> Categories = new();
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassCommandBeaconComponent : Component
{
    public EntityUid Source;
    public TimeSpan ExpiresAt;
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassInterceptTargetComponent : Component
{
    public readonly Dictionary<Wh40kClassInterceptSource, Wh40kClassInterceptState> Sources = new();
}

public readonly record struct Wh40kClassInterceptSource(EntityUid Source, string EffectId);

public readonly record struct Wh40kClassInterceptState(TimeSpan ExpiresAt, int Magnitude);

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassDashRuntimeComponent : Component
{
    public TimeSpan EndsAt;
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassDashSpeedRuntimeComponent : Component
{
    public int BonusPercent;
    public TimeSpan EndsAt;
}

/// <summary>
/// Short hand-off from the authoritative pre-shot event to projectile creation. It prevents an ordinary gun
/// callback from guessing which player or which limited-charge Soldier effect owns a shot.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassPendingShotComponent : Component
{
    public EntityUid Body;
    public TimeSpan ExpiresAt;
    public float ArmorPenetrationMultiplier = 1f;
    public float LowHealthDamageBonus;
    public float PriorityTargetDamageBonus;
    public bool HitConfirmation;
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassShotModifierComponent : Component
{
    public EntityUid Body;
    public float LowHealthDamageBonus;
    public float PriorityTargetDamageBonus;
    public bool HitConfirmation;
    public float ArmorPenetrationMultiplier = 1f;
}

/// <summary>
/// Body-local ordered route used exclusively by the Soldier's Nest Relocation ability.
/// It is never serialized and is removed with the class profile.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassNestRouteComponent : Component
{
    public string EffectId = string.Empty;
    public readonly List<MapCoordinates> Points = new(4);
    /// <summary>
    /// Points can be consumed only after the route has been completely authored. This also prevents adding
    /// replacement points partway through a cycle.
    /// </summary>
    public bool RouteLocked;
    public TimeSpan PointExpiry;
    public TimeSpan PlaceCooldownEnd;
    public TimeSpan AdvanceCooldownEnd;
    public TimeSpan CycleCooldownEnd;
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassTransferConsentComponent : Component
{
    public EntityUid Source;
    public EntityUid Item;
    public EntityUid Action;
    public TimeSpan ExpiresAt;
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassDeviceOverrideComponent : Component
{
    public EntityUid Source;
    public string EffectId = string.Empty;
    public TimeSpan ExpiresAt;
    public bool ChangedBolts;
    public bool OriginalBoltsDown;
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassThrownEffectComponent : Component
{
    public EntityUid Source;
    public string EffectId = string.Empty;
    public TimeSpan ExpiresAt;
    public int Magnitude;
    public TimeSpan Duration;
}

[RegisterComponent, UnsavedComponent]
public sealed partial class Wh40kClassWeaponCoatingComponent : Component
{
    public EntityUid Source;
    public string EffectId = string.Empty;
    public TimeSpan ExpiresAt;
    public int Magnitude;
    public int Charges = 1;
}

public sealed class Wh40kClassProfileReconciledEvent : EntityEventArgs;
