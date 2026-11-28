using Content.Shared._WH40K.WolfChem.Purity;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;

namespace Content.Shared.Chemistry.Reaction;

public sealed partial class ChemicalReactionSystem
{
    /// <summary>The grade of the reaction being performed, for reaction effects that spawn graded solids.</summary>
    public float Wh40kLastReactionGrade { get; private set; } = 1f;

    /// <summary>Kelvin each reaction's optimum is moved by this round, which the server rolls.</summary>
    public Dictionary<string, float> Wh40kOptimumShift { get; } = new();

    /// <summary>
    /// The grade of a reaction's product: the mean grade of the graded reactants it consumes, times the reaction's
    /// process factor at the solution's temperature. Standard-grade reactants don't raise it. A reaction with fillers
    /// is a cut instead: the mean runs over every reactant, fillers counting as zero.
    /// </summary>
    public float Wh40kReactionGrade(Solution solution, ReactionPrototype reaction)
    {
        _prototypeManager.TryIndex<Wh40kReactionPurityPrototype>(reaction.ID, out var purity);
        var input = 1f;
        var cut = purity is { Fillers.Count: > 0 };
        if (solution.Purity != null || cut)
        {
            var total = 0f;
            var weighted = 0f;
            foreach (var (reagent, reactant) in reaction.Reactants)
            {
                if (reactant.Catalyst)
                    continue;

                if (!cut && !solution.Purity!.ContainsKey(reagent))
                    continue;

                var amount = reactant.Amount.Float();
                total += amount;
                if (!cut || !purity!.Fillers.Contains(reagent))
                    weighted += amount * solution.GetPurity(reagent);
            }

            if (total > 0f)
                input = weighted / total;
        }

        Wh40kLastReactionGrade = purity != null ? input * purity.Factor(solution.Temperature, Wh40kOptimumShift.GetValueOrDefault(reaction.ID)) : input;
        return Wh40kLastReactionGrade;
    }

    /// <summary>Re-grades a product that was just added at standard grade.</summary>
    private static void Wh40kGradeProduct(Solution solution, string product, FixedPoint2 added, float grade)
    {
        if (grade < Solution.StandardPurityThreshold)
            solution.RegradeAdded(product, added, grade);
    }
}
