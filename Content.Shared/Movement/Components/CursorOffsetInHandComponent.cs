using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Activates cursor eye offset while the item is held in hand.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CursorOffsetInHandComponent : Component
{
    /// <summary>
    /// If true, only applies the cursor offset when held in the active hand.
    /// </summary>
    [DataField]
    public bool UseActiveHand = true;
}
