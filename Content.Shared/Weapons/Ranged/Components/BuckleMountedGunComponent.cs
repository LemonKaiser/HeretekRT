using System.Numerics;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Marks a gun entity as usable by an operator buckled to its strap.
/// </summary>
[RegisterComponent]
public sealed partial class BuckleMountedGunComponent : Component
{
    /// <summary>
    /// When true, the mounted gun is only available while its strap is enabled.
    /// </summary>
    [DataField]
    public bool RequireEnabledStrap = true;

    /// <summary>
    /// Local-space shot origin offset used for mounted weapons with a visible barrel offset.
    /// </summary>
    [DataField]
    public Vector2 ShootOriginOffset = Vector2.Zero;
}
