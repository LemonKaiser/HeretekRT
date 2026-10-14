using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.NPC;

[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kInnateArmorComponent : Component
{
    [DataField]
    public float ArmorRating;

    [DataField]
    public bool ChainWeaponWeakness;
}
