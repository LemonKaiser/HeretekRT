using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>
/// Applies cardiac strain from heavy stimulants or combat drugs on organic bodies as mild asphyxiation and toxin load.
/// Neuroware hosts pay in battery charge and chassis heat instead.
/// </summary>
public sealed partial class Wh40kStrainHeart : EntityEffect
{
    [DataField]
    public float Amount = 0.1f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wh40k-strain-heart", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var intox = args.EntityManager.System<SharedWh40kIntoxicationSystem>();
        if (intox.IsNeurowareHost(args.TargetEntity))
            return;

        var amount = Amount;
        if (args is EntityEffectReagentArgs reagent)
            amount *= reagent.Scale.Float();

        if (amount <= 0f)
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict["Asphyxiation"] = FixedPoint2.New(amount * 6f);
        damage.DamageDict["Poison"] = FixedPoint2.New(amount * 2f);
        args.EntityManager.System<DamageableSystem>().TryChangeDamage(args.TargetEntity, damage,
            ignoreResistances: true, interruptsDoAfters: false, canSever: false);
    }
}
