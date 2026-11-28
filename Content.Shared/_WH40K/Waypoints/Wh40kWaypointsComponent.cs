using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Waypoints;

/// <summary>
/// Shared crew-marked waypoints stored on a ship's grid and visible across all radar/helm consoles aboard.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class Wh40kWaypointsComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public List<Wh40kWaypoint> Waypoints = new();

    [ViewVariables]
    public int NextId = 1;
}

[Serializable, NetSerializable]
public readonly record struct Wh40kWaypoint(int Id, string Name, NetEntity Map, Vector2 Position);

[Serializable, NetSerializable]
public sealed class Wh40kWaypointAddMessage(string name, Vector2 position) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
    public readonly Vector2 Position = position;
}

[Serializable, NetSerializable]
public sealed class Wh40kWaypointRemoveMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int Id = id;
}
