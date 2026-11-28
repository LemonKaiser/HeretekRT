using Content.Server._WH40K.WolfChem.Purity;
using Content.Server.Botany;
using Content.Shared.Chemistry.Components;

namespace Content.Server.Botany.Systems;

public sealed partial class BotanySystem
{
    /// <summary>Grades a grown produce's reagents by its plant's potency, if the produce opts in.</summary>
    private void Wh40kGradeProduce(EntityUid uid, Solution solution, SeedData seed)
    {
        if (!TryComp<Wh40kProducePurityComponent>(uid, out var comp))
            return;

        var grade = MathHelper.Lerp(comp.MinPurity, comp.MaxPurity, Math.Clamp(seed.Potency / 100f, 0f, 1f));
        foreach (var (reagent, _) in solution.Contents)
        {
            solution.SetPurity(reagent.Prototype, grade);
        }
    }
}
