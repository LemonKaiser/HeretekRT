using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Allows a <see cref="BallisticAmmoProviderComponent"/> gun or magazine to load matching ammo by clicking on it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BallisticAmmoInteractLoaderComponent : Component;
