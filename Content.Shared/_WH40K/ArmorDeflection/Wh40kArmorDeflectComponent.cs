using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.ArmorDeflection;

/// <summary>
/// Worn heavy armor plating that ricochets light rounds off without dealing damage.
/// Armor-piercing rounds and rounds that ignore resistances always punch through.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kArmorDeflectComponent : Component
{
    /// <summary>Damage type that decides whether a round is light enough to bounce.</summary>
    [DataField]
    public ProtoId<DamageTypePrototype> DamageType = "Piercing";

    /// <summary>Highest effective damage of that type, per projectile, that ricochets.</summary>
    [DataField]
    public FixedPoint2 Threshold = 24;

    /// <summary>A round with at least this much armor penetration always gets through.</summary>
    [DataField]
    public float PenetratingArmorPenetration = 0.3f;

    /// <summary>Spread of the ricochet around the direction the round came from.</summary>
    [DataField]
    public Angle Spread = Angle.FromDegrees(120);

    [DataField]
    public SoundSpecifier? Sound = new SoundCollectionSpecifier("Wh40kRicochet");
}

/// <summary>Added to a wearer of <see cref="Wh40kArmorDeflectComponent"/> armor while equipped.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kArmorDeflectWearerComponent : Component;
