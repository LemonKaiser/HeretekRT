namespace Content.Shared._WH40K.OperationalDomain.Components;

/// <summary>
/// Explicitly marks a grid as an independent vessel which can own operational subsystems.
/// This is deliberately separate from <c>ShuttleComponent</c>: most shuttles are not personal vessels.
/// </summary>
[RegisterComponent]
public sealed partial class OperationalDomainVesselComponent : Component
{
}
