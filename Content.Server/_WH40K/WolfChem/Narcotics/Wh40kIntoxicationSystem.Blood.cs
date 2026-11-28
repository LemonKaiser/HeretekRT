using Content.Server.Body.Components;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// Level from the blood: a reagent with a <see cref="Wh40kDose"/> gives its profile the units left in the chemical stream
/// times their grade times its level per unit, so the high lasts as long as the drug.
/// </summary>
public sealed partial class Wh40kIntoxicationSystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    private Dictionary<string, List<(ProtoId<Wh40kDrugProfilePrototype> Profile, float PerUnit)>>? _bloodDrugs;
    private readonly Dictionary<ProtoId<Wh40kDrugProfilePrototype>, float> _blood = new();
    private readonly List<(ProtoId<Wh40kDrugProfilePrototype> Profile, ReagentId Reagent, float PerUnit)> _bloodEntries = new();

    private void InitializeBlood()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(args =>
        {
            if (args.WasModified<ReagentPrototype>())
                _bloodDrugs = null;
        });
    }

    private Dictionary<string, List<(ProtoId<Wh40kDrugProfilePrototype> Profile, float PerUnit)>> BloodDrugs()
    {
        if (_bloodDrugs != null)
            return _bloodDrugs;

        _bloodDrugs = new();
        foreach (var reagent in Proto.EnumeratePrototypes<ReagentPrototype>())
        {
            if (reagent.Metabolisms == null)
                continue;

            foreach (var entry in reagent.Metabolisms.Values)
            {
                foreach (var effect in entry.Effects)
                {
                    if (effect is not Wh40kDose { DependenceOnly: false } dose)
                        continue;

                    if (!_bloodDrugs.TryGetValue(reagent.ID, out var doses))
                        _bloodDrugs[reagent.ID] = doses = new();

                    doses.Add((dose.Profile, dose.PerUnit));
                }
            }
        }

        return _bloodDrugs;
    }

    /// <summary>True when a reagent in the blood raises a profile's level.</summary>
    public bool DosesInBlood(string reagent, ProtoId<Wh40kDrugProfilePrototype> profile)
    {
        return BloodDrugs().TryGetValue(reagent, out var doses) && doses.Exists(dose => dose.Profile == profile);
    }

    /// <summary>The profiles a reagent doses in the blood, with the level a unit gives each.</summary>
    public IReadOnlyList<(ProtoId<Wh40kDrugProfilePrototype> Profile, float PerUnit)> Doses(string reagent)
    {
        return BloodDrugs().TryGetValue(reagent, out var doses) ? doses : Array.Empty<(ProtoId<Wh40kDrugProfilePrototype>, float)>();
    }

    private Entity<SolutionComponent>? ReadBlood(EntityUid uid)
    {
        _blood.Clear();
        _bloodEntries.Clear();
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream)
            || !_solutions.TryGetSolution(uid, bloodstream.ChemicalSolutionName, out var soln, out var solution)
            || IsNeurowareHost(uid))
        {
            return null;
        }

        var drugs = BloodDrugs();
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (!drugs.TryGetValue(reagent.Prototype, out var doses))
                continue;

            var units = quantity.Float() * solution.GetPurity(reagent.Prototype);
            foreach (var (profile, perUnit) in doses)
            {
                _blood[profile] = _blood.GetValueOrDefault(profile) + units * perUnit;
                _bloodEntries.Add((profile, reagent, perUnit));
            }
        }

        return soln;
    }

    private void Flush(Entity<SolutionComponent> soln, ProtoId<Wh40kDrugProfilePrototype> profile,
        Wh40kDrugProfilePrototype proto, float level, float blood, float seconds)
    {
        if (proto.OverdoseHalfLife <= 0f || proto.Tiers.Count < 2 || blood <= 0f)
            return;

        var excess = level - proto.Tiers[^1].Min;
        if (excess <= 0f)
            return;

        var share = Math.Min(1f, excess / blood) * (1f - MathF.Pow(2f, -seconds / proto.OverdoseHalfLife));
        foreach (var (entryProfile, reagent, _) in _bloodEntries)
        {
            if (entryProfile != profile)
                continue;

            var amount = FixedPoint2.New(soln.Comp.Solution.GetReagentQuantity(reagent).Float() * share);
            if (amount > FixedPoint2.Zero)
                _solutions.RemoveReagent(soln, reagent, amount);
        }
    }

    public override void ReduceFamily(EntityUid uid, Wh40kDrugFamily family, float amount)
    {
        base.ReduceFamily(uid, family, amount);
        if (amount <= 0f || ReadBlood(uid) is not { } soln)
            return;

        foreach (var (profile, reagent, perUnit) in _bloodEntries)
        {
            if (perUnit > 0f && Proto.TryIndex(profile, out var proto) && proto.Family == family)
                _solutions.RemoveReagent(soln, reagent, FixedPoint2.New(amount / perUnit));
        }
    }
}
