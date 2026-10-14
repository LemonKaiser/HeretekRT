using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC.Pathfinding;
using Content.Shared.ActionBlocker;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.NPC;

/// <summary>Short idle trips around the spawn point, using native pathfinding and movement.</summary>
public sealed partial class Wh40kChaosPatrolOperator : HTNOperator
{
    private const string CoordinatesKey = "Wh40kPatrolCoordinates";
    private const string PathKey = "Wh40kPatrolPath";
    private const string ReturningKey = "Wh40kPatrolReturning";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly MoveToOperator _move = new()
    {
        TargetKey = CoordinatesKey,
        PathfindKey = PathKey,
        PathfindInPlanning = false,
    };

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _move.Initialize(sysManager);
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var comp = _entities.GetComponent<Wh40kChaosTacticsComponent>(owner);
        var xform = _entities.GetComponent<TransformComponent>(owner);
        if (comp.Patrolling || _timing.CurTime < comp.NextPatrol || comp.PatrolRadius <= 0f ||
            comp.PatrolOrigin is not { } origin || !origin.IsValid(_entities) ||
            !xform.Coordinates.TryDistance(_entities, origin, out var distance) ||
            !_entities.System<ActionBlockerSystem>().CanMove(owner))
            return (false, null);

        var returning = comp.PatrolStops >= 2 || distance > comp.PatrolRadius;
        var pathfinding = _entities.System<PathfindingSystem>();
        var path = returning
            ? await pathfinding.GetPath(owner, xform.Coordinates, origin, 0.6f, cancelToken,
                Wh40kChaosTacticsSystem.PatrolFlags)
            : await pathfinding.GetRandomPath(owner, comp.PatrolRadius, cancelToken, limit: 40,
                flags: Wh40kChaosTacticsSystem.PatrolFlags);

        cancelToken.ThrowIfCancellationRequested();
        var target = xform.Coordinates;
        if (path.Result == PathResult.Path && path.Path.Count > 0)
        {
            if (!returning)
            {
                // A bounded prefix prevents successive random trips from drifting across the station.
                var count = 0;
                foreach (var poly in path.Path)
                {
                    if (!poly.Coordinates.TryDistance(_entities, origin, out var fromHome) ||
                        fromHome > comp.PatrolRadius)
                        break;
                    count++;
                }
                path.Path.RemoveRange(count, path.Path.Count - count);
            }

            if (path.Path.Count > 0)
                target = returning ? origin : path.Path[^1].Coordinates;
        }

        // Failed or trivial picks still execute a wait, instead of repeatedly searching every HTN replan.
        if (path.Path.Count == 0 || !returning &&
            (!target.TryDistance(_entities, xform.Coordinates, out var travel) || travel < 1.5f))
        {
            target = xform.Coordinates;
            path = new PathResultEvent(PathResult.NoPath, new List<PathPoly>());
            returning = false;
        }

        return (true, new Dictionary<string, object>
        {
            { CoordinatesKey, target },
            { PathKey, path },
            { ReturningKey, returning },
        });
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var comp = _entities.GetComponent<Wh40kChaosTacticsComponent>(owner);
        comp.Patrolling = true;
        comp.EvadingWithoutTarget = false;
        comp.PatrolDeadline = _timing.CurTime + TimeSpan.FromSeconds(6);
        _entities.System<Wh40kChaosTacticsSystem>().DelayPatrol(comp);
        _move.Startup(blackboard);
        var steering = _entities.GetComponent<NPCSteeringComponent>(owner);
        steering.Flags = Wh40kChaosTacticsSystem.PatrolFlags;
        steering.Range = 0.6f;
        steering.DirectMove = false;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var comp = _entities.GetComponent<Wh40kChaosTacticsComponent>(owner);
        if (_timing.CurTime >= comp.PatrolDeadline ||
            _entities.System<Wh40kChaosTacticsSystem>().TryGetThreatTarget(owner, comp, out _) ||
            !_entities.System<ActionBlockerSystem>().CanMove(owner))
            return HTNOperatorStatus.Failed;

        return _move.Update(blackboard, frameTime);
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var comp = _entities.GetComponent<Wh40kChaosTacticsComponent>(owner);
        if (status == HTNOperatorStatus.Finished &&
            blackboard.GetValue<PathResultEvent>(PathKey).Result == PathResult.Path)
        {
            comp.PatrolStops = blackboard.GetValue<bool>(ReturningKey) ? 0 : comp.PatrolStops + 1;
        }
        comp.Patrolling = false;
        _move.ConditionalShutdown(blackboard);
        blackboard.Remove<bool>(ReturningKey);
    }
}
