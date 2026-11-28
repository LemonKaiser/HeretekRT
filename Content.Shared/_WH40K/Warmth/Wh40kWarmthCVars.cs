using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.Warmth;

/// <summary>
/// Configuration variables for body warmth, thermal warnings, wind chill, wetness, and screen effects.
/// </summary>
[CVarDefs]
public sealed class Wh40kWarmthCVars
{
    public static readonly CVarDef<float> WarningShare =
        CVarDef.Create("wh40k.warmth.warning_share", 0.5f, CVar.SERVERONLY);

    public static readonly CVarDef<float> WindChillPerSpeed =
        CVarDef.Create("wh40k.warmth.wind_chill_per_speed", 0.13f, CVar.SERVERONLY);

    public static readonly CVarDef<float> WindChillMax =
        CVarDef.Create("wh40k.warmth.wind_chill_max", 2f, CVar.SERVERONLY);

    public static readonly CVarDef<float> WetCooling =
        CVarDef.Create("wh40k.warmth.wet_cooling", 0.5f, CVar.SERVERONLY);

    public static readonly CVarDef<float> DrySeconds =
        CVarDef.Create("wh40k.warmth.dry_seconds", 600f, CVar.SERVERONLY);

    public static readonly CVarDef<float> ColdSlowdown =
        CVarDef.Create("wh40k.warmth.cold_slowdown", 0.9f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> HeatSlowdown =
        CVarDef.Create("wh40k.warmth.heat_slowdown", 0.95f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<bool> OverlayEnabled =
        CVarDef.Create("wh40k.warmth.overlay_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
