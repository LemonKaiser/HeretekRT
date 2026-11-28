using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared._WH40K.WolfChem.Purity;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>Keeps <see cref="Wh40kPurityTintVisuals.Tinted"/> in step with the grade of a <see cref="Wh40kPurityTintComponent"/>'s reagent.</summary>
public sealed class Wh40kPurityTintSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kPurityTintComponent, MapInitEvent>(OnMapInit,
            after: new[] { typeof(SolutionContainerSystem), typeof(Wh40kSolutionPuritySystem) });
        SubscribeLocalEvent<Wh40kPurityTintComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void OnMapInit(Entity<Wh40kPurityTintComponent> ent, ref MapInitEvent args)
    {
        Refresh(ent);
    }

    private void OnSolutionChanged(Entity<Wh40kPurityTintComponent> ent, ref SolutionContainerChangedEvent args)
    {
        Refresh(ent);
    }

    /// <summary>Shows the tint if the reagent is present at or above the threshold grade.</summary>
    public void Refresh(Entity<Wh40kPurityTintComponent> ent)
    {
        var tinted = _solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution)
                     && solution.ContainsPrototype(ent.Comp.Reagent)
                     && solution.GetPurity(ent.Comp.Reagent) >= ent.Comp.Threshold;
        _appearance.SetData(ent, Wh40kPurityTintVisuals.Tinted, tinted);
    }
}
