using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.ItemRarity.Components;

/// <summary>
/// Persistent base/effective snapshot for rarity-modified characteristics.
/// It prevents a loaded item or repeated event from applying the same bonus
/// more than once.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ItemRarityStatsComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Applied;

    [DataField, AutoNetworkedField]
    public bool HasArmor;

    [DataField, AutoNetworkedField]
    public float BaseArmorRating;

    [DataField, AutoNetworkedField]
    public bool HasDurability;

    [DataField, AutoNetworkedField]
    public float BaseMaxDurability;

    [DataField, AutoNetworkedField]
    public bool HasWeapon;

    [DataField, AutoNetworkedField]
    public float BaseWeaponDamageMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float EffectiveWeaponDamageMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float BaseWeaponArmorPenetration;

    [DataField, AutoNetworkedField]
    public float EffectiveWeaponArmorPenetration;

    [DataField, AutoNetworkedField]
    public bool HasMeleeDamage;

    [DataField, AutoNetworkedField]
    public DamageSpecifier BaseMeleeDamage = new();
}
