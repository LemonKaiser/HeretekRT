using Content.Shared._WH40K.ItemRarity.Systems;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.ItemRarity;

/// <summary>
/// Shared tooltip text for hands, inventory slots and grid storage pieces.
/// </summary>
internal static class ItemRarityTooltip
{
    public static string? GetRarityName(
        SharedItemRaritySystem itemRaritySystem,
        IPrototypeManager prototypeManager,
        EntityUid? entity)
    {
        if (entity is not { } uid ||
            !itemRaritySystem.TryGetRarity(uid, out var rarityId) ||
            !prototypeManager.TryIndex(rarityId, out ItemRarityPrototype? rarity))
        {
            return null;
        }

        return Loc.GetString(rarity.Name);
    }

    public static string? GetNameWithRarity(
        SharedItemRaritySystem itemRaritySystem,
        IPrototypeManager prototypeManager,
        EntityUid? entity,
        string name)
    {
        var rarityName = GetNonDefaultRarityName(itemRaritySystem, prototypeManager, entity);
        return rarityName == null
            ? null
            : Loc.GetString("item-rarity-name", ("name", name), ("rarity", rarityName));
    }

    public static string? GetText(
        IEntityManager entityManager,
        SharedItemRaritySystem itemRaritySystem,
        IPrototypeManager prototypeManager,
        EntityUid? entity)
    {
        if (entity is not { } uid ||
            !entityManager.TryGetComponent<MetaDataComponent>(uid, out var metadata))
        {
            return null;
        }

        var rarityName = GetNonDefaultRarityName(itemRaritySystem, prototypeManager, uid);
        return rarityName == null
            ? metadata.EntityName
            : Loc.GetString(
                "item-rarity-tooltip",
                ("name", metadata.EntityName),
                ("rarity", rarityName));
    }

    /// <summary>
    /// The stamped profile is the implicit baseline for every item. It still
    /// drives the neutral visuals, but names should only be annotated when an
    /// item has obtained a quality above that baseline.
    /// </summary>
    private static string? GetNonDefaultRarityName(
        SharedItemRaritySystem itemRaritySystem,
        IPrototypeManager prototypeManager,
        EntityUid? entity)
    {
        if (entity is not { } uid ||
            !itemRaritySystem.TryGetRarity(uid, out var rarityId) ||
            rarityId.Equals(SharedItemRaritySystem.DefaultRarity) ||
            !prototypeManager.TryIndex(rarityId, out ItemRarityPrototype? rarity))
        {
            return null;
        }

        return Loc.GetString(rarity.Name);
    }
}
