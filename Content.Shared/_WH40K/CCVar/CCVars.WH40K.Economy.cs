using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> Wh40kMaterialMarketEnabled =
        CVarDef.Create("wh40k.economy.material_valuation", true, CVar.SERVERONLY);

    public static readonly CVarDef<bool> Wh40kMaterialMarketSaturation =
        CVarDef.Create("wh40k.economy.saturation", true, CVar.SERVERONLY);

    public static readonly CVarDef<bool> Wh40kMaterialMarketDemand =
        CVarDef.Create("wh40k.economy.demand_cycles", true, CVar.SERVERONLY);
}
