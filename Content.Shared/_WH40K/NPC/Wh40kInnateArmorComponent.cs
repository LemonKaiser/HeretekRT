using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.NPC;

[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kInnateArmorComponent : Component
{
    [DataField(required: true)]
    public DamageModifierSet Modifiers = new();

    [DataField]
    public bool ChainWeaponWeakness;
}
