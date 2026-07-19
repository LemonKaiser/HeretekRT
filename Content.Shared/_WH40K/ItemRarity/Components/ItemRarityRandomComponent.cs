using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.ItemRarity.Components;

/// <summary>
/// Marks an item as eligible for a server-side rarity roll.
/// The profile is data-only: the random roll is performed by the server after
/// a random loot event and is persisted in <see cref="ItemRarityComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class ItemRarityRandomComponent : Component
{
    /// <summary>
    /// Highest tier this item may receive. The global rarity weights are kept
    /// unchanged; results above this cap are folded into the cap.
    /// </summary>
    [DataField(required: true)]
    public byte MaxTier = 1;

    /// <summary>
    /// Used only by the two Stage 1 test prototypes so they can be spawned
    /// directly from the menu. Production profiles roll on random loot events.
    /// </summary>
    [DataField]
    public bool RandomizeOnDirectSpawn;

    /// <summary>
    /// Optional base damage multiplier for a profiled weapon. A non-positive
    /// value means that the weapon's existing multiplier is used.
    /// </summary>
    [DataField]
    public float BaseWeaponDamageMultiplier;

    /// <summary>
    /// Optional base weapon armour penetration. A negative value means zero for
    /// ranged weapons or the existing melee value for melee weapons.
    /// Projectile/ammunition penetration remains independent.
    /// </summary>
    [DataField]
    public float BaseWeaponArmorPenetration = -1f;
}
