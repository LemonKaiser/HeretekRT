using Content.Server._WH40K.SectorMap.Systems;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Marks a movable root entity on a planetary surface. Boundary enforcement is driven by its
/// movement events instead of repeatedly scanning every transform on the server.
/// </summary>
[RegisterComponent, Access(typeof(KoronusPlanetarySystem))]
public sealed partial class KoronusSurfaceBoundaryTrackedComponent : Component;
