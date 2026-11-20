using Robust.Shared.GameStates;

namespace Content.Shared.Buckle.Components;

/// <summary>Temporarily prevents normal release of a strap.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BuckleLockComponent : Component;
