using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Assigns its designated <see cref="Target"/> to any fired chasing/homing projectiles.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TargetAssignComponent : Component
{
    [DataField]
    public EntityUid? Target;
}
