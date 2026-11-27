using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Shoulder-mounted plasma servo-cannon deployed from a synthetic torso.
/// Leaves both hands free; middle-click aims and charges a heavy plasma bolt.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kShoulderCannonComponent : Component
{
    [DataField]
    public EntProtoId DeployAction = "WH40KActionDeployShoulderCannon";

    [ViewVariables]
    public EntityUid? DeployActionEntity;

    [DataField]
    public EntProtoId Bolt = "WH40KShoulderCannonBolt";

    [DataField]
    public float BoltSpeed = 18f;

    [DataField]
    public TimeSpan ChargeTime = TimeSpan.FromSeconds(0.8);

    [DataField]
    public TimeSpan FireDelay = TimeSpan.FromSeconds(5);

    [ViewVariables]
    public TimeSpan NextFire;

    [DataField]
    public TimeSpan EmpLockout = TimeSpan.FromSeconds(60);

    [DataField]
    public SoundSpecifier? DeploySound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/batrifle_magin.ogg");

    [DataField]
    public SoundSpecifier? StowSound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagOut/batrifle_magout.ogg");

    [DataField]
    public SoundSpecifier? ChargeSound = new SoundPathSpecifier("/Audio/Weapons/plasma_cutter.ogg");

    [DataField]
    public SoundSpecifier? FireSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/laser_cannon2.ogg");
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class Wh40kShoulderCannonDeployedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Charging;

    [DataField]
    public ResPath Rsi = new("/Textures/_WH40K/Augments/shoulder_cannon.rsi");

    [DataField]
    public string State = "deployed";

    [DataField]
    public string GlowState = "glow";
}

public sealed partial class Wh40kToggleShoulderCannonEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class Wh40kShoulderCannonDoAfterEvent : DoAfterEvent
{
    public NetCoordinates Target;

    private Wh40kShoulderCannonDoAfterEvent()
    {
    }

    public Wh40kShoulderCannonDoAfterEvent(NetCoordinates target)
    {
        Target = target;
    }

    public override DoAfterEvent Clone() => this;
}
