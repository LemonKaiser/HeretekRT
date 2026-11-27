using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.Synthetics;

[CVarDefs]
public sealed class Wh40kSyntheticsCVars
{
    public static readonly CVarDef<bool> CyberneticHud =
        CVarDef.Create("wh40k.cybernetic_hud", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> CyberneticHudScale =
        CVarDef.Create("wh40k.cybernetic_hud_scale", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
