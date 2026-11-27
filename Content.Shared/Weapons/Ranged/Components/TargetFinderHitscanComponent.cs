using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Marks the hit entity as the designated target on the firing <see cref="TargetFinderComponent"/> gun.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TargetFinderHitscanComponent : Component;
