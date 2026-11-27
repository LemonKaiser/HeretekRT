using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Refractor palm shield projector: blocks incoming bullets, melee blows, stamina strikes, and deflects lasers
/// within a 135° frontal arc until its charge pool (200) collapses. Recharges only while lowered.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kHandShieldComponent : Wh40kDeployableImplantComponent
{
    [DataField]
    public float MaxCharge = 200f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float Charge = 200f;

    [ViewVariables, AutoNetworkedField]
    public bool Collapsed;

    [DataField]
    public float RechargeRate = 10f;

    [DataField, AutoNetworkedField]
    public Angle Arc = Angle.FromDegrees(135);

    [DataField]
    public Angle DeflectSpread = Angle.FromDegrees(60);

    [DataField]
    public SoundSpecifier? ShotSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Hits/laser_sear_wall.ogg");

    [DataField]
    public SoundSpecifier? BlowSound = new SoundPathSpecifier("/Audio/Weapons/block_metal1.ogg");

    [DataField]
    public SoundSpecifier? CollapseSound = new SoundPathSpecifier("/Audio/Weapons/ebladeoff.ogg");

    [ViewVariables]
    public EntityUid? RaisedBy;

    [ViewVariables]
    public TimeSpan LastBlockSound;
}

/// <summary>
/// Attached to a body while its palm shield is deployed.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kHandShieldUserComponent : Component
{
    /// <summary>Entity holding <see cref="Wh40kHandShieldComponent"/> (organ or hand part).</summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid Implant;
}
