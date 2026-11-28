using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> WH40KUiFrostedGlass =
        CVarDef.Create("wh40k.graphics.ui_frosted_glass", true, CVar.CLIENTONLY | CVar.ARCHIVE);
    public static readonly CVarDef<float> WH40KUiGlassTransparency =
        CVarDef.Create("wh40k.graphics.ui_glass_transparency", 0.4f, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> WH40KWorldGlow =
        CVarDef.Create("wh40k.graphics.world_glow", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> WH40KContactShadows =
        CVarDef.Create("wh40k.graphics.contact_shadows", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> WH40KPuddleReflections =
        CVarDef.Create("wh40k.graphics.puddle_reflections", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> WH40KBloodTrails =
        CVarDef.Create("wh40k.graphics.blood_trails", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
