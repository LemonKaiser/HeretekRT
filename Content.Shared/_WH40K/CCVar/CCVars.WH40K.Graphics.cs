using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> WH40KWorldGlow =
        CVarDef.Create("wh40k.graphics.world_glow", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> WH40KContactShadows =
        CVarDef.Create("wh40k.graphics.contact_shadows", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
