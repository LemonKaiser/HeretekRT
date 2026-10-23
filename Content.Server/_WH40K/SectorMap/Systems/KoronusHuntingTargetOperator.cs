using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusHuntingTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var target = _entities.System<KoronusHuntingTurretSystem>().SelectTarget(blackboard);
        Dictionary<string, object>? effects = target.IsValid()
            ? new() { { "Target", target }, { "TargetCoordinates", new EntityCoordinates(target, Vector2.Zero) } }
            : null;
        return Task.FromResult<(bool, Dictionary<string, object>?)>((effects != null, effects));
    }
}
