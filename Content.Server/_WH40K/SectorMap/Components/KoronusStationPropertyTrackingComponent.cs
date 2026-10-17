using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>Only entities carrying station property receive movement listeners.</summary>
[RegisterComponent]
public sealed partial class KoronusStationPropertyTrackingComponent : Component
{
    public readonly HashSet<EntityUid> Items = new();
}

[RegisterComponent]
public sealed partial class KoronusStationPropertyOriginComponent : Component
{
    public EntityCoordinates HomePosition;
    public EntityCoordinates OriginalPosition;
    public BaseContainer? OriginalContainer;
    public readonly List<EntityUid> Ancestors = new();
    public TimeSpan? ReturnAt;
    public long ReturnSequence;
}

/// <summary>Seals the initial snapshot so a repeated configuration cannot claim player possessions.</summary>
[RegisterComponent]
public sealed partial class KoronusStationPropertyRegistryComponent : Component;
