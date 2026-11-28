using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>
/// Lowers every profile of a family, for antagonists such as Naloxone, Haloperidol, Psicodine and Dylovene.
/// </summary>
public sealed partial class Wh40kClearIntoxication : EntityEffect
{
    [DataField(required: true)]
    public Wh40kDrugFamily Family;

    /// <summary>Level removed; inside a reagent, scaled by the metabolism scale.</summary>
    [DataField]
    public float Amount = 2f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wh40k-clear-intoxication",
            ("chance", Probability),
            ("family", Loc.GetString($"wh40k-drug-family-{Family.ToString().ToLowerInvariant()}")));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var amount = Amount;
        if (args is EntityEffectReagentArgs reagent)
            amount *= reagent.Scale.Float();

        args.EntityManager.System<SharedWh40kIntoxicationSystem>().ReduceFamily(args.TargetEntity, Family, amount);
    }
}
