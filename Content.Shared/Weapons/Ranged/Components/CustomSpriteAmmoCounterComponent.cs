using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Replaces the default gun ammo counter HUD with custom sprites for loaded and spent rounds.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CustomSpriteAmmoCounterComponent : Component
{
    [DataField, AutoNetworkedField]
    public SpriteSpecifier LoadedAmmoSprite = SpriteSpecifier.Invalid;

    [DataField, AutoNetworkedField]
    public SpriteSpecifier SpentAmmoSprite = SpriteSpecifier.Invalid;

    [DataField, AutoNetworkedField]
    public int NumberOfRows = 2;
}
