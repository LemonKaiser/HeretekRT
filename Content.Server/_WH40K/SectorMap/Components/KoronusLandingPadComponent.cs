namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Marks one visible, grid-snapped tile as part of a planetary landing pad. Cardinally adjacent
/// markers on the same grid form one runtime pad; diagonal contact deliberately does not join them.
/// </summary>
[RegisterComponent]
public sealed partial class KoronusLandingPadComponent : Component;
