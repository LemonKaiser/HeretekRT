using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>Narcotics market settings: seller heat, tips and the stock signature. Values are credits of street value.</summary>
[CVarDefs]
public sealed class Wh40kNarcoticsCVars
{
    /// <summary>Heat up to which a seller is paid in full: the most one sale is worth to a seller who has cooled off.</summary>
    public static readonly CVarDef<float> HeatFree =
        CVarDef.Create("wh40k.narcotics.heat_free", 500000f, CVar.SERVERONLY);

    /// <summary>
    /// How far a seller's own line strays from <see cref="HeatFree"/> either way, as a share of it; 0 gives everyone
    /// the same line. Nobody is shown theirs: the buyer's manner and the console's price are the only read.
    /// </summary>
    public static readonly CVarDef<float> HeatFuzz =
        CVarDef.Create("wh40k.narcotics.heat_fuzz", 0.15f, CVar.SERVERONLY);

    /// <summary>Seconds a seller keeps from selling before their line is drawn anew.</summary>
    public static readonly CVarDef<float> HeatFuzzTime =
        CVarDef.Create("wh40k.narcotics.heat_fuzz_time", 600f, CVar.SERVERONLY);

    /// <summary>Heat above <see cref="HeatFree"/> over which the price falls by a factor of e.</summary>
    public static readonly CVarDef<float> HeatScale =
        CVarDef.Create("wh40k.narcotics.heat_scale", 250000f, CVar.SERVERONLY);

    /// <summary>The least share of its value a buyer pays below <see cref="HeatCap"/>.</summary>
    public static readonly CVarDef<float> HeatFloor =
        CVarDef.Create("wh40k.narcotics.heat_floor", 0.15f, CVar.SERVERONLY);

    /// <summary>Heat lost each second, which is also the most a seller earns at full price over time.</summary>
    public static readonly CVarDef<float> HeatCooling =
        CVarDef.Create("wh40k.narcotics.heat_cooling", 150f, CVar.SERVERONLY);

    /// <summary>The most heat a seller holds, and the heat past which the market is saturated.</summary>
    public static readonly CVarDef<float> HeatCap =
        CVarDef.Create("wh40k.narcotics.heat_cap", 1000000f, CVar.SERVERONLY);

    /// <summary>The share of its value a buyer pays for what is sold past <see cref="HeatCap"/>.</summary>
    public static readonly CVarDef<float> HeatGlutFloor =
        CVarDef.Create("wh40k.narcotics.heat_glut_floor", 0.02f, CVar.SERVERONLY);

    /// <summary>Heat past which a tip describes the seller.</summary>
    public static readonly CVarDef<float> HeatHot =
        CVarDef.Create("wh40k.narcotics.heat_hot", 650000f, CVar.SERVERONLY);

    /// <summary>Seconds before security hears of the same seller again, unless they have grown hotter.</summary>
    public static readonly CVarDef<float> TipCooldown =
        CVarDef.Create("wh40k.narcotics.tip_cooldown", 180f, CVar.SERVERONLY);

    /// <summary>Street value aboard a ship at which its stock shows on radar; 0 turns the signature off.</summary>
    public static readonly CVarDef<float> StockSignature =
        CVarDef.Create("wh40k.narcotics.stock_signature", 200000f, CVar.SERVERONLY);

    /// <summary>Street value that adds one point to a narcotic's contraband hand-in reward; 0 leaves the flat reward.</summary>
    public static readonly CVarDef<float> TurnInValue =
        CVarDef.Create("wh40k.narcotics.turn_in_value", 15000f, CVar.SERVERONLY);
}
