using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Marks an augment intended for an arm cavity.
/// This is also used by the surgery catalogue to select a compatible implant.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentArmComponent : Component;
