using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.WolfChem.Purity;

/// <summary>Reagent purity settings.</summary>
[CVarDefs]
public sealed class Wh40kPurityCVars
{
    /// <summary>Poison dealt per metabolised unit of impurity, so 10 u at 60% deals 4 times this.</summary>
    public static readonly CVarDef<float> ImpurityDamage =
        CVarDef.Create("wh40k.purity.impurity_damage", 1f, CVar.SERVERONLY);

    /// <summary>The same for reagents in the Narcotics group, where a bad cut is meant to hurt.</summary>
    public static readonly CVarDef<float> NarcoticImpurityDamage =
        CVarDef.Create("wh40k.purity.narcotic_impurity_damage", 6f, CVar.SERVERONLY);
}
