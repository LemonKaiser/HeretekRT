using System.Text;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared._WH40K.WolfChem.Purity;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>
/// Reports reagent grades when a <see cref="Wh40kPurityTesterComponent"/> is used on something.
/// </summary>
public sealed partial class Wh40kPurityTesterSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kPurityTesterComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<Wh40kPurityTesterComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = true;
        _popup.PopupEntity(Report(target), target, args.User);
    }

    /// <summary>The tester's readout for an entity: each graded reagent's grade, mixed over all its solutions.</summary>
    public string Report(EntityUid target)
    {
        if (!TryComp<SolutionContainerManagerComponent>(target, out var manager))
            return Loc.GetString("wh40k-purity-tester-nothing");

        var totals = new Dictionary<string, (float Quantity, float Weighted)>();
        foreach (var (_, soln) in _solutions.EnumerateSolutions((target, manager)))
        {
            var solution = soln.Comp.Solution;
            foreach (var (reagent, quantity) in solution.Contents)
            {
                var amount = quantity.Float();
                var (total, weighted) = totals.GetValueOrDefault(reagent.Prototype);
                totals[reagent.Prototype] = (total + amount, weighted + amount * solution.GetPurity(reagent.Prototype));
            }
        }

        if (totals.Count == 0)
            return Loc.GetString("wh40k-purity-tester-nothing");

        var report = new StringBuilder();
        foreach (var (prototype, (quantity, weighted)) in totals)
        {
            var purity = quantity > 0f ? weighted / quantity : 1f;
            if (purity >= Content.Shared.Chemistry.Components.Solution.StandardPurityThreshold)
                continue;

            var name = _proto.TryIndex<ReagentPrototype>(prototype, out var proto) ? proto.LocalizedName : prototype;
            if (report.Length > 0)
                report.Append('\n');
            report.Append(Loc.GetString("wh40k-purity-tester-line", ("name", name), ("purity", Wh40kPurityText.Percent(purity))));
        }

        return report.Length == 0 ? Loc.GetString("wh40k-purity-tester-standard") : report.ToString();
    }
}
