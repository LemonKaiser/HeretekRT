namespace Content.Shared.Damage;

/// <summary>
/// Raised after damage has been accepted, before damage is relayed to body parts.
/// Paired with <see cref="DamageChangeFinishedEvent"/> even if resistances prevent all damage.
/// </summary>
[ByRefEvent]
public readonly record struct DamageChangeStartedEvent;

/// <summary>
/// Raised after both body part damage and the owning entity's damage have been processed.
/// </summary>
[ByRefEvent]
public readonly record struct DamageChangeFinishedEvent;
