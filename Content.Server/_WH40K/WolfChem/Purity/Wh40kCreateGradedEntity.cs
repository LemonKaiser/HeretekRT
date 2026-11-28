using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>
/// Spawns a solid per unit reaction, as <c>CreateEntityReactionEffect</c> does, and grades every reagent in it at the
/// reaction's grade.
/// </summary>
public sealed partial class Wh40kCreateGradedEntity : EntityEffect
{
    [DataField(required: true)]
    public EntProtoId Entity;

    /// <summary>Entities per unit reaction.</summary>
    [DataField]
    public int Number = 1;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-create-entity-reaction-effect",
            ("chance", Probability),
            ("entname", prototype.Index(Entity).Name),
            ("amount", Number));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        var transform = entMan.System<SharedTransformSystem>();
        var solutions = entMan.System<SolutionContainerSystem>();
        var grade = entMan.System<ChemicalReactionSystem>().Wh40kLastReactionGrade;
        var coords = transform.GetMapCoordinates(args.TargetEntity);

        var count = Number;
        if (args is EntityEffectReagentArgs reagentArgs)
            count *= reagentArgs.Quantity.Int();

        for (var i = 0; i < count; i++)
        {
            var uid = entMan.SpawnEntity(Entity, coords);
            transform.AttachToGridOrMap(uid);

            if (grade >= Solution.StandardPurityThreshold)
                continue;

            foreach (var (_, soln) in solutions.EnumerateSolutions(uid))
            {
                var solution = soln.Comp.Solution;
                foreach (var (reagent, _) in solution.Contents)
                {
                    solution.SetPurity(reagent.Prototype, solution.GetPurity(reagent.Prototype) * grade);
                }

                solutions.UpdateChemicals(soln, needsReactionsProcessing: false);
            }
        }
    }
}
