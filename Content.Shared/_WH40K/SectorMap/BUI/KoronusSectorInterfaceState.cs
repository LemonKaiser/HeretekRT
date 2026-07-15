using System.Numerics;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Timing;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.BUI;

/// <summary>
/// Authored Koronus topology exposed by a shuttle console. The server remains authoritative for every jump.
/// </summary>
[Serializable, NetSerializable]
public sealed class KoronusSectorInterfaceState
{
    public bool Available;
    public bool CanJump;
    public string? CurrentSystem;
    public List<KoronusSectorNodeState> Systems;
    public List<KoronusSectorRouteState> Routes;
    public KoronusSectorTravelState? WarpTravel;

    public KoronusSectorInterfaceState(
        bool available,
        bool canJump,
        string? currentSystem,
        List<KoronusSectorNodeState> systems,
        List<KoronusSectorRouteState> routes,
        KoronusSectorTravelState? warpTravel = null)
    {
        Available = available;
        CanJump = canJump;
        CurrentSystem = currentSystem;
        Systems = systems;
        Routes = routes;
        WarpTravel = warpTravel;
    }

    public static KoronusSectorInterfaceState Unavailable() => new(false, false, null, new(), new());
}

/// <summary>
/// Presentation-only information about a Koronus jump already accepted by the server.
/// It never authorizes navigation; the server validates every new request separately.
/// </summary>
[Serializable, NetSerializable]
public sealed class KoronusSectorTravelState
{
    public string OriginSystem;
    public string DestinationSystem;
    public FTLState State;
    public StartEndTime StateTime;

    public KoronusSectorTravelState(
        string originSystem,
        string destinationSystem,
        FTLState state,
        StartEndTime stateTime)
    {
        OriginSystem = originSystem;
        DestinationSystem = destinationSystem;
        State = state;
        StateTime = stateTime;
    }
}

[Serializable, NetSerializable]
public sealed class KoronusSectorNodeState
{
    public string Id;
    public string Name;
    public Vector2 Position;
    public bool Enabled;
    public bool Current;
    public bool Reachable;

    public KoronusSectorNodeState(string id, string name, Vector2 position, bool enabled, bool current, bool reachable)
    {
        Id = id;
        Name = name;
        Position = position;
        Enabled = enabled;
        Current = current;
        Reachable = reachable;
    }
}

[Serializable, NetSerializable]
public sealed class KoronusSectorRouteState
{
    public string From;
    public string To;
    public string RouteClass;
    public bool Enabled;

    public KoronusSectorRouteState(string from, string to, string routeClass, bool enabled)
    {
        From = from;
        To = to;
        RouteClass = routeClass;
        Enabled = enabled;
    }
}
