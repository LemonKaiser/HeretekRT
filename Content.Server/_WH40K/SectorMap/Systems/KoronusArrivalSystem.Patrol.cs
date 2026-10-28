using System.Numerics;
using Content.Server._Mono.Drone.Components;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WH40K.Drones.Components;
using Content.Server.NPC.HTN;
using Content.Server.Power.EntitySystems;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusArrivalSystem
{
    [Dependency] private ShipSteeringSystem _steering = default!;
    [Dependency] private PowerReceiverSystem _power = default!;

    private void ConfigurePatrol(EntityUid grid, KoronusSystemPrototype system)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<DroneAiCoreComponent>(child) && TryComp<HTNComponent>(child, out var htn))
                htn.SleepPlayerCheckRangeOverride = system.BoundaryRadius * 2f;
        }
    }

    // Combat HTN owns steering while engaged. Supply ordinary ship steering orders only when idle.
    private void UpdatePatrol(KoronusSystemPrototype system, ArrivalState state, TimeSpan now)
    {
        foreach (var grid in state.Grids)
        {
            if (TerminatingOrDeleted(grid) || !TryComp<HostileDroneGridComponent>(grid, out var drone) || drone.Defeated ||
                !TryComp<MapGridComponent>(grid, out var bounds))
                continue;

            var xform = Transform(grid);
            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var core))
            {
                if (!HasComp<DroneAiCoreComponent>(core) || !TryComp<HTNComponent>(core, out var htn))
                    continue;

                var ownsOrder = state.PatrolOrders.TryGetValue(core, out var order);
                var steering = CompOrNull<ShipSteererComponent>(core);
                if (steering != null && (!ownsOrder || !steering.Coordinates.Equals(order.Destination)))
                {
                    state.PatrolOrders.Remove(core);
                    continue;
                }

                if (!htn.Enabled || !Transform(core).Anchored || !_power.IsPowered(core))
                {
                    if (ownsOrder)
                        _steering.Stop(core);
                    state.PatrolOrders.Remove(core);
                    continue;
                }

                if (HasComp<ShipTargetingComponent>(core))
                    continue;

                if (steering != null && now < order.ExpiresAt &&
                    Vector2.DistanceSquared(_transform.GetWorldPosition(grid),
                        _transform.ToMapCoordinates(order.Destination).Position) > 100f * 100f)
                    continue;

                var radius = bounds.LocalAABB.Size.Length() / 2f;
                if (!_sector.TryGetArrivalPosition(system, xform.MapID, radius, out var destination))
                    continue;
                var coordinates = _transform.ToCoordinates(new MapCoordinates(destination, xform.MapID));
                var patrol = _steering.Steer(core, coordinates);
                if (patrol == null)
                    continue;
                patrol.Mode = ShipSteeringMode.GoToRange;
                patrol.Range = 75f;
                patrol.RangeTolerance = null;
                patrol.NoFinish = false;
                patrol.SetMaxVelocity = 60f;
                patrol.InRangeMaxSpeed = 5f;
                patrol.AvoidCollisions = true;
                patrol.AvoidProjectiles = true;
                state.PatrolOrders[core] = (coordinates, now + TimeSpan.FromMinutes(3));
            }
        }
    }
}
