using Content.Shared._WH40K.WolfChem.Purity;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>
/// Applies what a reagent's grade does to the body that metabolises it.
/// </summary>
public sealed class Wh40kPuritySystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly ProtoId<DamageTypePrototype> Poison = "Poison";

    private const string NarcoticGroup = "Narcotics";

    private float _impurityDamage;
    private float _narcoticImpurityDamage;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, Wh40kPurityCVars.ImpurityDamage, v => _impurityDamage = v, true);
        Subs.CVar(_cfg, Wh40kPurityCVars.NarcoticImpurityDamage, v => _narcoticImpurityDamage = v, true);
    }

    /// <summary>Deals Poison for the impure fraction of a metabolised amount of a reagent in <paramref name="group"/>.</summary>
    public void ApplyImpurity(EntityUid body, FixedPoint2 units, float purity, string group)
    {
        if (purity >= Solution.StandardPurityThreshold)
            return;

        var perUnit = group == NarcoticGroup ? _narcoticImpurityDamage : _impurityDamage;
        var amount = units.Float() * (1f - purity) * perUnit;
        if (amount <= 0f)
            return;

        _damageable.TryChangeDamage(body, new DamageSpecifier { DamageDict = { [Poison.Id] = FixedPoint2.New(amount) } },
            ignoreResistances: true, interruptsDoAfters: false, canSever: false);
    }
}
