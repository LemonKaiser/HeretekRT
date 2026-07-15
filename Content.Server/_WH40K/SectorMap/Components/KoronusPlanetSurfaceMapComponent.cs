using Content.Server._WH40K.SectorMap.Systems;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Server-only identity and distinct gameplay/generation boundaries of one preloaded planetary
/// surface map.
/// </summary>
[RegisterComponent, Access(typeof(KoronusPlanetarySystem), typeof(KoronusSectorResidencySystem))]
public sealed partial class KoronusPlanetSurfaceMapComponent : Component
{
    [ViewVariables]
    public string SurfaceId = string.Empty;

    [ViewVariables]
    public string SystemId = string.Empty;

    /// <summary>
    /// The authored terrain grid is allowed to overlap a landed shuttle; other grids are not.
    /// </summary>
    [ViewVariables]
    public EntityUid TerrainGrid;

    /// <summary>
    /// Square gameplay perimeter. Shuttles, players and loose objects are safely stopped and moved
    /// back inside without a physics collision.
    /// </summary>
    [ViewVariables]
    public Box2 PlayableBounds;

    /// <summary>
    /// Tile-generation perimeter. It may extend a small visual buffer beyond
    /// <see cref="PlayableBounds"/>, but the terrain grid itself is never displaced to fit either
    /// boundary.
    /// </summary>
    [ViewVariables]
    public Box2 GenerationBounds;

    /// <summary>
    /// Start of the current empty interval, used by the ordinary sector residency policy.
    /// </summary>
    [ViewVariables]
    public TimeSpan? EmptySince;
}
