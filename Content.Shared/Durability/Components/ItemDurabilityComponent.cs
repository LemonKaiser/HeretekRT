using Content.Shared._Shitmed.Targeting;
using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Durability.Components;

/// <summary>
/// Opt-in durability configuration and runtime state for an item.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ItemDurabilityComponent : Component
{
    /// <summary>
    /// Maximum durability of the item.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public float MaxDurability = 100f;

    /// <summary>
    /// Current durability. A negative value means that the server should initialize it to MaxDurability.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CurrentDurability = -1f;

    /// <summary>
    /// Whether the item has reached zero durability without being deleted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Broken;

    /// <summary>
    /// If true, the entity is deleted when durability reaches zero. If false, it remains present but unusable.
    /// </summary>
    [DataField]
    public bool DestroyAtZero = true;

    /// <summary>
    /// If true, durability is reduced when this entity's own DamageableComponent takes damage.
    /// This is used by shields, which absorb damage through BlockingSystem rather than ArmorComponent.
    /// </summary>
    [DataField]
    public bool DrainOnDamage;

    /// <summary>
    /// Marks the item as a protective item whose durability is reduced when its armor is applied.
    /// </summary>
    [DataField]
    public bool ProtectsWearer;

    /// <summary>
    /// Body parts covered by this item's protection. The default keeps the component useful until a prototype
    /// specifies a narrower coverage mask.
    /// </summary>
    [DataField]
    public TargetBodyPart ProtectedBodyParts = TargetBodyPart.All;

    /// <summary>
    /// Durability spent for each projectile actually fired by this item.
    /// </summary>
    [DataField]
    public float ShotDrain;

    /// <summary>
    /// Durability spent for one successful melee attack with this item.
    /// </summary>
    [DataField]
    public float MeleeDrain = 1f;

    /// <summary>
    /// Durability spent when a completed tool interaction uses this item.
    /// </summary>
    [DataField]
    public float ToolUseDrain;

    /// <summary>
    /// Fixed durability spent when this entity's own DamageableComponent takes damage and DrainOnDamage is enabled.
    /// This is primarily used by shields.
    /// </summary>
    [DataField]
    public float IncomingDamageDrain = 1f;

    /// <summary>
    /// Durability spent per point of damage actually prevented by this item's ArmorComponent.
    /// </summary>
    [DataField]
    public float ArmorDamageDrainMultiplier = 1f;

    /// <summary>
    /// Minimum repair station tier accepted by this item.
    /// </summary>
    [DataField]
    public int RequiredWorkbenchTier = 1;

    /// <summary>
    /// Stack prototype consumed by a future repair station.
    /// </summary>
    [DataField]
    public ProtoId<StackPrototype>? RepairMaterial;

    /// <summary>
    /// Durability restored by one unit of the configured repair material.
    /// A non-positive value delegates the amount to the repair station tier.
    /// </summary>
    [DataField]
    public float RepairPerMaterial;
}
