namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Server-side registration for the client-only landing-pad visual marker.
/// The prototype must be known by both assemblies; gameplay uses KoronusLandingPadComponent.
/// </summary>
[RegisterComponent]
public sealed partial class KoronusLandingPadVisualComponent : Component;
