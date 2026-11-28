using Content.Shared.Drunk;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class Drunk : EntityEffect
{
    /// <summary>
    ///     BoozePower is how long each metabolism cycle will make the drunk effect last for.
    /// </summary>
    [DataField]
    public float BoozePower = 3f;

    /// <summary>
    ///     Whether speech should be slurred.
    /// </summary>
    [DataField]
    public bool SlurSpeech = true;

    /// <summary>
    ///     When above zero, holds the drunk timer at BoozePower times this per unit left in the blood, instead of
    ///     adding BoozePower each cycle.
    /// </summary>
    [DataField]
    public float PerUnitInBlood;

    /// <summary>
    ///     With <see cref="PerUnitInBlood"/>, units in the blood up to this many don't count, for an overdose effect.
    /// </summary>
    [DataField]
    public float InBloodAbove;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-drunk", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var boozePower = BoozePower;

        if (PerUnitInBlood > 0f && args is EntityEffectReagentArgs { Source: { } blood, Reagent: { } reagent })
        {
            var units = Math.Max(0f, blood.GetTotalPrototypeQuantity(reagent.ID).Float() - InBloodAbove);
            args.EntityManager.System<Content.Server._WH40K.WolfChem.Narcotics.Wh40kAlcoholSystem>()
                .HoldDrunkenness(args.TargetEntity, BoozePower * PerUnitInBlood * units, SlurSpeech);
            return;
        }

        if (args is EntityEffectReagentArgs reagentArgs) {
            boozePower *= reagentArgs.Scale.Float();
        }
        
        var drunkSys = args.EntityManager.EntitySysManager.GetEntitySystem<SharedDrunkSystem>();
        drunkSys.TryApplyDrunkenness(args.TargetEntity, boozePower, SlurSpeech);
    }
}
