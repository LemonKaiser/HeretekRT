using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Corona defensive charge ring: deploys 8 orbs around the wearer while anchoring them in a 2-second shield.
/// </summary>
public abstract partial class Wh40kBurstLauncherComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Orb;

    [DataField]
    public int Orbs = 8;

    [DataField]
    public float OrbDistance = 1.6f;

    [DataField]
    public TimeSpan OrbFlight = TimeSpan.FromSeconds(0.35);

    [DataField]
    public TimeSpan Fuse = TimeSpan.FromSeconds(2);

    [DataField]
    public EntProtoId Shield = "WH40KHaloShield";

    [DataField(required: true)]
    public EntProtoId Action;

    [ViewVariables]
    public EntityUid? ActionEntity;

    [DataField]
    public TimeSpan RechargeTime = TimeSpan.FromMinutes(3);

    [ViewVariables]
    public TimeSpan? NextCharge;

    [DataField]
    public SoundSpecifier? ArmSound = new SoundPathSpecifier("/Audio/Effects/double_beep.ogg");
}

[RegisterComponent]
public sealed partial class Wh40kHaloBurstComponent : Wh40kBurstLauncherComponent;

[RegisterComponent]
public sealed partial class Wh40kHaloStunComponent : Wh40kBurstLauncherComponent;

public sealed partial class Wh40kFireBurstLauncherEvent : InstantActionEvent;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class Wh40kHaloArmingComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan End;

    [ViewVariables]
    public EntityUid? Shield;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class Wh40kHaloOrbComponent : Component
{
    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public float StaminaDamage;

    [DataField]
    public float Radius = 1.25f;

    [DataField]
    public float Knockback = 1.5f;

    [DataField]
    public float KnockbackSpeed = 10f;

    [DataField]
    public EntProtoId Blast = "WH40KHaloBlast";

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/explosion_small1.ogg",
        AudioParams.Default.WithVolume(-4f).WithVariation(0.15f));

    [ViewVariables, AutoPausedField]
    public TimeSpan DetonateAt;

    [ViewVariables]
    public Vector2 LaunchVelocity;

    [ViewVariables, AutoPausedField]
    public TimeSpan LaunchedAt;

    [ViewVariables, AutoPausedField]
    public TimeSpan? StopAt;

    [ViewVariables]
    public EntityUid? Source;
}
