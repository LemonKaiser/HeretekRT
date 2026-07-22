using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared.Item;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.ItemRarity.Systems;

/// <summary>
/// Public resolver for explicit item rarity overrides and the default stamped rarity.
/// Presentation and future gameplay systems should use this system instead of reading
/// <see cref="ItemRarityComponent"/> directly or duplicating fallback rules.
/// </summary>
public sealed class SharedItemRaritySystem : EntitySystem
{
    public static readonly ProtoId<ItemRarityPrototype> DefaultRarity = ItemRarityPrototypeIds.Stamped;

    /// <summary>
    /// Returns an item's explicit rarity or <see cref="DefaultRarity"/> when it has no override.
    /// This method only classifies the item; it does not change any gameplay characteristic.
    /// Callers that have not already established that the entity is an item should use
    /// <see cref="TryGetRarity"/> instead.
    /// </summary>
    public ProtoId<ItemRarityPrototype> GetRarity(EntityUid uid, ItemRarityComponent? rarity = null)
    {
        return Resolve(uid, ref rarity, false)
            ? rarity.Rarity
            : DefaultRarity;
    }

    /// <summary>
    /// Safely resolves rarity only when the entity is an item.
    /// This is the preferred public entry point for callers handling arbitrary entities.
    /// </summary>
    public bool TryGetRarity(
        EntityUid uid,
        out ProtoId<ItemRarityPrototype> rarityId,
        ItemComponent? item = null,
        ItemRarityComponent? rarity = null)
    {
        if (!Resolve(uid, ref item, false))
        {
            rarityId = default;
            return false;
        }

        rarityId = GetRarity(uid, rarity);
        return true;
    }
}
