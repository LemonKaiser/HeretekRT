using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WH40K.Bosses;

/// <summary>Selects reachable crew on the boarded vessel without requiring line of sight.</summary>
public sealed partial class Wh40kBoarderTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        return _entities.System<Wh40kSpaceBossSystem>().PlanBoardingTarget(blackboard, cancelToken);
    }
}
