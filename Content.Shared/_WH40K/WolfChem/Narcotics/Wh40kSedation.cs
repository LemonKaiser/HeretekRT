using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>
/// Opioid respiratory sedation: scales with graded units in the blood divided by family tolerance. Past the 0.6
/// threshold (just under heroin's overdose tier), depresses breathing with reversible asphyxiation and drowsiness.
/// </summary>
public sealed partial class Wh40kSedation : EntityEffect
{
    private const float RespiratoryThreshold = 0.6f;

    [DataField(required: true)]
    public ProtoId<Wh40kDrugProfilePrototype> Profile;

    [DataField(required: true)]
    public float SedationPerUnit;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wh40k-sedation", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs { Reagent: { } reagent } reagentArgs
            || !IoCManager.Resolve<IPrototypeManager>().TryIndex(Profile, out var profile))
            return;

        var intox = args.EntityManager.System<SharedWh40kIntoxicationSystem>();
        if (intox.IsNeurowareHost(args.TargetEntity))
            return;

        var units = reagentArgs.Source?.GetTotalPrototypeQuantity(reagent.ID).Float() ?? reagentArgs.Quantity.Float();
        var grade = reagentArgs.Source?.GetPurity(reagent.ID) ?? 1f;
        var tolerance = intox.ToleranceFactor(args.TargetEntity, profile.Family);
        var sedation = units * grade / Math.Max(0.1f, tolerance) * SedationPerUnit;

        if (sedation < RespiratoryThreshold)
            return;

        var excess = sedation - RespiratoryThreshold;
        var damage = new DamageSpecifier();
        damage.DamageDict["Asphyxiation"] = FixedPoint2.New(Math.Min(3.5f, 0.5f + excess * 3.5f));
        args.EntityManager.System<DamageableSystem>().TryChangeDamage(args.TargetEntity, damage,
            ignoreResistances: true, interruptsDoAfters: false, canSever: false);

        args.EntityManager.System<StatusEffectsSystem>().TryAddStatusEffect(
            args.TargetEntity, "Drowsiness", TimeSpan.FromSeconds(4), true, "Drowsiness");
    }
}
