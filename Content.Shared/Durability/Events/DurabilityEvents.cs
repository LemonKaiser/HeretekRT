using Content.Shared._Shitmed.Targeting;
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
/// Raised once after the armor pipeline selects the best matching protective item for incoming damage.
/// </summary>
public readonly record struct ArmorProtectionAppliedEvent(TargetBodyPart? TargetPart, float AbsorbedDamage);

/// <summary>
/// Raised by a repair station after its repair cycle ends.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class DurabilityRepairDoAfterEvent : SimpleDoAfterEvent;
