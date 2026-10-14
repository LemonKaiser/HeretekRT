using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.NPC;
/// <summary>Uses native utility scoring, excluding a recently abandoned target.</summary>
public sealed partial class Wh40kChaosSelectTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var tactics = _entities.GetComponent<Wh40kChaosTacticsComponent>(owner);
        if (!_entities.System<Wh40kChaosTacticsSystem>().TryGetThreatTarget(owner, tactics, out var target))
        {
            var result = _entities.System<NPCUtilitySystem>().GetEntities(blackboard, "NearbyMeleeTargets", bestOnly: false);
            if (_timing.CurTime < tactics.IgnoreUntil && tactics.IgnoredTarget is { } ignored)
                result.Entities.Remove(ignored);
            target = result.GetHighest();
        }
        Dictionary<string, object>? effects = target.IsValid()
            ? new() { { "Target", target }, { "TargetCoordinates", new EntityCoordinates(target, Vector2.Zero) } }
            : null;
        return Task.FromResult<(bool, Dictionary<string, object>?)>((effects != null, effects));
    }
}
