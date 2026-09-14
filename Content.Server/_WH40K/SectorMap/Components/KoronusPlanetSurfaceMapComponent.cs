using Content.Server._WH40K.SectorMap.Systems;
using Robust.Shared.Maths;
using System.Collections.Generic;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Server-only identity and distinct gameplay/generation boundaries of one preloaded planetary
/// surface map.
/// </summary>
[RegisterComponent, Access(typeof(KoronusPlanetarySystem), typeof(KoronusSectorResidencySystem),
    typeof(Content.Server._WH40K.DeployableFieldBase.DeployableFieldBaseSystem),
    typeof(Content.Server._WH40K.Activities.KoronusActivityMaterializationSystem),
    typeof(Content.Server._WH40K.Activities.KoronusActivitySetPieceExecutorSystem))]
public sealed partial class KoronusPlanetSurfaceMapComponent : Component
{
    [ViewVariables]
    public string SurfaceId = string.Empty;

    [ViewVariables]
    public string SystemId = string.Empty;

    /// <summary>
    /// Procedural biome grid that owns the surface terrain and provides its coordinate space.
    /// </summary>
    [ViewVariables]
    public EntityUid TerrainGrid;

    /// <summary>
    /// Grids loaded from the authored surface map. Planet setup gives static grids a shuttle
    /// component too, so this explicit set is the authority for distinguishing the permanent base
    /// from actual visiting shuttles.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> AuthoredBaseGrids = new();

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
