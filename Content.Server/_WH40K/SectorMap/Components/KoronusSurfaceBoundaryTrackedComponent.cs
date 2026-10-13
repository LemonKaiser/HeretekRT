using Content.Server._WH40K.SectorMap.Systems;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Marks a movable root entity on a planetary surface. Boundary enforcement is driven by its
/// movement events, with corrections deferred until Update instead of nesting transform events.
/// </summary>
[RegisterComponent, Access(typeof(KoronusPlanetarySystem))]
public sealed partial class KoronusSurfaceBoundaryTrackedComponent : Component;
