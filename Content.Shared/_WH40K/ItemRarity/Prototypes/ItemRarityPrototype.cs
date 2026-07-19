using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.ItemRarity.Prototypes;

/// <summary>
/// Data-driven rarity classification shared by item descriptions, inventory
/// controls and world effects. Stage 1 also stores the roll weights and bonus
/// range used to initialize an item's persistent rarity state.
/// </summary>
[Prototype("itemRarity")]
public sealed partial class ItemRarityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Localized display name of the rarity.
    /// </summary>
    [DataField(required: true)]
    public LocId Name { get; private set; }

    /// <summary>
    /// One-based position in the rarity hierarchy.
    /// </summary>
    [DataField(required: true)]
    public byte Tier { get; private set; }

    /// <summary>
    /// Main color used for labels, borders and world effects.
    /// </summary>
    [DataField(required: true)]
    public Color Color { get; private set; }

    /// <summary>
    /// Secondary color used by ornaments and higher-tier effects.
    /// </summary>
    [DataField(required: true)]
    public Color AccentColor { get; private set; }

    /// <summary>
    /// Relative chance used by the global rarity roll.
    /// </summary>
    [DataField]
    public float RandomWeight { get; private set; }

    /// <summary>
    /// Inclusive lower bound of the per-item bonus roll.
    /// </summary>
    [DataField]
    public float BonusMinPercent { get; private set; }

    /// <summary>
    /// Inclusive upper bound of the per-item bonus roll.
    /// </summary>
    [DataField]
    public float BonusMaxPercent { get; private set; }
}

/// <summary>
/// Canonical rarity IDs used by content and presentation systems.
/// </summary>
public static class ItemRarityPrototypeIds
{
    public static readonly ProtoId<ItemRarityPrototype> Stamped = "Stamped";
    public static readonly ProtoId<ItemRarityPrototype> Consecrated = "Consecrated";
    public static readonly ProtoId<ItemRarityPrototype> MasterCrafted = "MasterCrafted";
    public static readonly ProtoId<ItemRarityPrototype> Relic = "Relic";
    public static readonly ProtoId<ItemRarityPrototype> OmnissiahShrine = "OmnissiahShrine";
    public static readonly ProtoId<ItemRarityPrototype> Archeotech = "Archeotech";
}
