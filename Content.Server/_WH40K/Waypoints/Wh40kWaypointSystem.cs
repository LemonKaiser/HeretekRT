using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._WH40K.Waypoints;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Waypoints;

/// <summary>
/// Manages per-grid ship waypoints for helm and mass-scanner consoles.
/// </summary>
public sealed partial class Wh40kWaypointSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _mapSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UserInterfaceComponent, Wh40kWaypointAddMessage>(OnAdd);
        SubscribeLocalEvent<UserInterfaceComponent, Wh40kWaypointRemoveMessage>(OnRemove);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        if (!TryComp<Wh40kWaypointsComponent>(args.Grid, out var source))
            return;

        foreach (var grid in args.NewGrids)
        {
            var waypoints = EnsureComp<Wh40kWaypointsComponent>(grid);
            waypoints.Waypoints = new List<Wh40kWaypoint>(source.Waypoints);
            waypoints.NextId = source.NextId;
            Dirty(grid, waypoints);
        }
    }

    private static bool IsRadarUi(Enum key) => key is RadarConsoleUiKey or ShuttleConsoleUiKey;

    private void OnAdd(Entity<UserInterfaceComponent> ent, ref Wh40kWaypointAddMessage args)
    {
        if (IsRadarUi(args.UiKey))
            TryAdd(ent, args.Name, args.Position);
    }

    private void OnRemove(Entity<UserInterfaceComponent> ent, ref Wh40kWaypointRemoveMessage args)
    {
        if (IsRadarUi(args.UiKey))
            TryRemove(ent, args.Id);
    }

    /// <summary>
    /// Keeps an autopilot-engaged helm awake even if no living player stands next to it.
    /// </summary>
    public bool AutopilotEngaged(EntityUid uid, HTNComponent htn)
    {
        return TryComp<ShuttleConsoleComponent>(uid, out var helm) &&
               htn.Blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out _, EntityManager);
    }

    public bool TryAdd(EntityUid console, string name, Vector2 position)
    {
        if (!Wh40kWaypointRules.IsValidPosition(position) ||
            Transform(console) is not { GridUid: { } grid, MapUid: { } map })
            return false;

        var waypoints = EnsureComp<Wh40kWaypointsComponent>(grid);
        if (waypoints.Waypoints.Count >= Wh40kWaypointRules.MaxWaypoints)
            return false;

        var id = waypoints.NextId++;
        var clean = Wh40kWaypointRules.CleanName(name);
        if (clean.Length == 0)
            clean = Loc.GetString("wh40k-waypoint-default-name", ("id", id));

        waypoints.Waypoints.Add(new Wh40kWaypoint(id, clean, GetNetEntity(map), position));
        Dirty(grid, waypoints);
        return true;
    }

    public int? Place(EntityUid grid, int? id, string name, MapCoordinates at)
    {
        if (!Wh40kWaypointRules.IsValidPosition(at.Position) || !_mapSystem.TryGetMap(at.MapId, out var map))
            return null;

        var waypoints = EnsureComp<Wh40kWaypointsComponent>(grid);
        var clean = Wh40kWaypointRules.CleanName(name);
        if (id is { } existing)
        {
            var index = waypoints.Waypoints.FindIndex(waypoint => waypoint.Id == existing);
            if (index < 0)
                return null;

            var moved = new Wh40kWaypoint(existing, clean, GetNetEntity(map.Value), at.Position);
            if (waypoints.Waypoints[index] == moved)
                return existing;

            waypoints.Waypoints[index] = moved;
            Dirty(grid, waypoints);
            return existing;
        }

        if (waypoints.Waypoints.Count >= Wh40kWaypointRules.MaxWaypoints)
            return null;

        var newId = waypoints.NextId++;
        waypoints.Waypoints.Add(new Wh40kWaypoint(newId, clean, GetNetEntity(map.Value), at.Position));
        Dirty(grid, waypoints);
        return newId;
    }

    public void Remove(EntityUid grid, int id)
    {
        if (TryComp<Wh40kWaypointsComponent>(grid, out var waypoints) &&
            waypoints.Waypoints.RemoveAll(waypoint => waypoint.Id == id) > 0)
        {
            Dirty(grid, waypoints);
        }
    }

    public bool TryRemove(EntityUid console, int id)
    {
        if (Transform(console).GridUid is not { } grid ||
            !TryComp<Wh40kWaypointsComponent>(grid, out var waypoints) ||
            waypoints.Waypoints.RemoveAll(waypoint => waypoint.Id == id) == 0)
            return false;

        Dirty(grid, waypoints);
        return true;
    }
}
