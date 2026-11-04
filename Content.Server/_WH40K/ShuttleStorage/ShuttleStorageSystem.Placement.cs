using System.Linq;
using System.Numerics;
using Content.Server.Shuttles;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Map;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSystem
{
    [Dependency] private IMapManager _placementMaps = default!;

    private bool BerthStillValid(Transfer operation, DockingConfig config)
    {
        if (!Exists(operation.Grid) || !Facility(operation.Console, out _, out var facility) ||
            facility != operation.Facility || !TryComp(operation.Grid, out MapGridComponent? grid))
            return false;
        foreach (var (first, second, dockA, dockB) in config.Docks)
        {
            if (TerminatingOrDeleted(first) || TerminatingOrDeleted(second) || dockA.Deleted || dockB.Deleted ||
                dockA.Docked || dockB.Docked || !Transform(first).Anchored || !Transform(second).Anchored ||
                Transform(first).GridUid != operation.Grid || Transform(second).GridUid != facility)
                return false;
        }
        var coordinates = _transform.ToMapCoordinates(config.Coordinates);
        var angle = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);
        var bounds = new Box2Rotated(grid.LocalAABB.Translated(coordinates.Position), angle, coordinates.Position);
        var grids = new List<Entity<MapGridComponent>>();
        _placementMaps.FindGridsIntersecting(coordinates.MapId, bounds, ref grids, includeMap: false);
        return grids.All(other => other.Owner == operation.Grid || other.Owner == facility) && FitsBerth(operation.Grid, config);
    }

    private bool FitsBerth(EntityUid grid, DockingConfig config)
    {
        if (!TryComp(config.TargetGrid, out MapGridComponent? target) ||
            _serializer.Inspect(grid, out var info) != Content.Shared._WH40K.ShuttleStorage.ShuttleStorageResult.Ready)
            return false;
        var inverse = _transform.GetInvWorldMatrix(grid);
        var rotation = _transform.GetWorldRotation(grid);
        foreach (var entity in info.Entities)
        {
            if (entity == grid || !TryComp(entity, out PhysicsComponent? body) || !body.Hard || !body.CanCollide ||
                !TryComp(entity, out FixturesComponent? fixtures))
                continue;
            var local = Vector2.Transform(_transform.GetWorldPosition(entity), inverse);
            var projected = new Robust.Shared.Physics.Transform(config.Coordinates.Position + config.Angle.RotateVec(local),
                _transform.GetWorldRotation(entity) - rotation + config.Angle);
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;
                for (var child = 0; child < fixture.Shape.ChildCount; child++)
                {
                    // Remove the physics skin plus a small rounding margin at an exactly shared tile edge.
                    var bounds = fixture.Shape.ComputeAABB(projected, child).Enlarged(-0.02f);
                    if (_maps.GetLocalTilesIntersecting(config.TargetGrid, target, bounds).Any())
                    {
                        Log.Debug($"Placement blocked by {ToPrettyString(entity)} at {bounds}; {fixture.Shape.GetType().Name}.");
                        return false;
                    }
                }
            }
        }
        return true;
    }
}
