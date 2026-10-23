using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Durability.Events;

public enum DurabilityReason : byte
{
    Initialization,
    Shot,
    MeleeHit,
    ToolUse,
    IncomingDamage,
    Repair,
}

/// <summary>
/// Raised after the current durability value changes.
/// </summary>
public readonly record struct DurabilityChangedEvent(
    float OldValue,
    float NewValue,
    float Amount,
    DurabilityReason Reason,
    EntityUid? User);

/// <summary>
/// Raised after a durability component has sanitized its maximum and current
/// values during map initialization.
/// </summary>
public readonly record struct DurabilityInitializedEvent;

/// <summary>
/// Raised once when an item reaches zero durability.
/// </summary>
public readonly record struct DurabilityDepletedEvent(DurabilityReason Reason, EntityUid? User);

/// <summary>
/// Raised for an item created by a random loot/spawn table. Such items start partially worn instead of at full durability.
/// </summary>
public readonly record struct RandomLootSpawnedEvent;

/// <summary>
/// Raised once per protective item with the damage prevented by that item's modifiers.
/// </summary>
public readonly record struct ArmorProtectionAppliedEvent(DamageSpecifier AbsorbedDamage);

/// <summary>
/// Raised by a repair station after its repair cycle ends.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class DurabilityRepairDoAfterEvent : SimpleDoAfterEvent;
