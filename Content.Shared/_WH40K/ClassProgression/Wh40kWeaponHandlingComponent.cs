using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.ClassProgression;

/// <summary>
/// Authored weapon-handling category used by the shared WH40K movement and moving-shot model.
/// The fixed values are code-owned so individual weapon prototypes cannot exceed the approved limits.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kWeaponHandlingComponent : Component
{
    [DataField(required: true)]
    public Wh40kWeaponHandlingCategory Category;
}

public enum Wh40kWeaponHandlingCategory : byte
{
    Pistol,
    SemiAutomaticRifle,
    Automatic,
    LightMachineGun,
    HeavyMachineGun,
}
