using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._WH40K.NPC;
/// <summary>Uses native utility scoring, excluding a recently abandoned target.</summary>
public sealed partial class Wh40kChaosSelectTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var tactics = _entities.GetComponent<Wh40kChaosTacticsComponent>(owner);
        var target = _entities.System<Wh40kChaosTacticsSystem>().SelectCombatTarget(blackboard, tactics);
        Dictionary<string, object>? effects = target.IsValid()
            ? new() { { "Target", target }, { "TargetCoordinates", new EntityCoordinates(target, Vector2.Zero) } }
            : null;
        return Task.FromResult<(bool, Dictionary<string, object>?)>((effects != null, effects));
    }
}
