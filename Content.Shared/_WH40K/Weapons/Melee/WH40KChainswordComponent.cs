using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Weapons.Melee;

/// <summary>
/// Marks a powered chainsword for its server-side contact visual.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KChainswordComponent : Component;
