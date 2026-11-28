using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>Applies or restores stamina via a metabolised drug profile tier.</summary>
public sealed partial class Wh40kStaminaChange : EntityEffect
{
    [DataField]
    public float Amount = 10f;

    [DataField]
    public bool Immediate;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wh40k-stamina-change",
            ("chance", Probability), ("amount", Amount));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out StaminaComponent? stamina))
            return;

        if (args is EntityEffectReagentArgs reagent && reagent.Scale != FixedPoint2.New(1))
            return;

        args.EntityManager.System<StaminaSystem>()
            .TakeStaminaDamage(args.TargetEntity, Amount, stamina, visual: false, immediate: Immediate);
    }
}
