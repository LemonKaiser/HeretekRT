using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>
/// Applies <see cref="Wh40kSolutionPurityComponent"/> grades once the entity's solutions exist.
/// </summary>
public sealed partial class Wh40kSolutionPuritySystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kSolutionPurityComponent, MapInitEvent>(OnMapInit, after: new[] { typeof(SharedSolutionContainerSystem) });
    }

    private void OnMapInit(Entity<Wh40kSolutionPurityComponent> ent, ref MapInitEvent args)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var soln, out var solution))
            return;

        foreach (var (reagent, grade) in ent.Comp.Reagents)
        {
            if (solution.ContainsPrototype(reagent))
                solution.SetPurity(reagent, grade);
        }

        Dirty(soln.Value);
    }
}
