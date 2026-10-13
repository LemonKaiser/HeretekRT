using System.Numerics;
using Robust.Shared.Serialization;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>Finite mining sites retained by the native sector snapshot, including sites not yet loaded.</summary>
[RegisterComponent]
public sealed partial class KoronusAsteroidFieldComponent : Component
{
    [DataField] public List<KoronusAsteroidCluster> Clusters = new();
    [DataField] public List<KoronusAsteroidSite> Sites = new();
    // Round time keeps resource recovery progressing while an empty sector is paused.
    [DataField] public TimeSpan NextReplenishment;
    [DataField] public int ReplenishmentCycle;
}

[DataDefinition]
public sealed partial class KoronusAsteroidCluster
{
    [DataField] public Vector2 Position;
    [DataField] public float Radius = 1250f;
}

[DataDefinition]
public sealed partial class KoronusAsteroidSite
{
    [DataField] public Vector2 Position;
    [DataField] public int Cluster;
    [DataField] public string Prototype = "KoronusMiningAsteroidSmall";
    [DataField] public bool Materialized;
    [DataField] public bool Depleted;
    [DataField] public int AvailableOnCycle;
}

/// <summary>Tracks safe unloading of pristine grids and delayed retirement of exhausted grids.</summary>
[RegisterComponent]
public sealed partial class KoronusMiningAsteroidComponent : Component
{
    [DataField] public int Site;
    [DataField] public bool Visited;
    [DataField] public bool IsShuttle;
    [DataField] public TimeSpan? FarSince;
    [DataField] public TimeSpan? AbandonedSince;
    [DataField] public bool Modified;
    // References are rebuilt only when the saved grid was verified pristine before serialization.
    [DataField] public bool PristineOnRestore;
    [DataField] public int OriginalChildCount;
    public HashSet<EntityUid>? OriginalChildren;
    [DataField] public Dictionary<Vector2i, KoronusAsteroidTile>? OriginalTiles;
    public bool Evicting;
}

/// <summary>Exact floor baseline without grid entity references or process-local numeric tile IDs.</summary>
[DataDefinition]
public sealed partial class KoronusAsteroidTile
{
    [DataField] public string Type = string.Empty;
    [DataField] public byte Flags;
    [DataField] public byte Variant;
    [DataField] public byte RotationMirroring;
}
