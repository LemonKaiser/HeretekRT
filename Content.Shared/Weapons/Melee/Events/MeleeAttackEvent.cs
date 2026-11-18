using Robust.Shared.Map;

namespace Content.Shared.Weapons.Melee.Events;

/// <summary>
/// Event raised on the user after attacking with a melee weapon, regardless of whether it hit anything.
/// Disarms have no click location for effects that require a strike.
/// </summary>
[ByRefEvent]
public record struct MeleeAttackEvent(EntityUid Weapon, EntityCoordinates? ClickLocation = null);
