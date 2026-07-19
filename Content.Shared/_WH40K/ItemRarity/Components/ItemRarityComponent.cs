using Content.Shared._WH40K.ItemRarity.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.ItemRarity.Components;

/// <summary>
/// Stores the rolled rarity and its immutable bonus for an item.
/// The server writes the roll once; presentation systems may use the component
/// without changing the result while the item is moved between containers.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ItemRarityComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<ItemRarityPrototype> Rarity = ItemRarityPrototypeIds.Stamped;

    /// <summary>
    /// The rolled percentage bonus for this particular item. Stage 1 stores the
    /// value; gameplay systems apply it in the following stage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BonusPercent;

    /// <summary>
    /// Prevents a second roll when a loot event is raised more than once.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsRolled;

    /// <summary>
    /// Set after the item has been picked up once. The rarity and all gameplay
    /// bonuses remain active, but the world-only aura is not shown again after
    /// the item is dropped.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool WorldEffectSuppressed;
}
