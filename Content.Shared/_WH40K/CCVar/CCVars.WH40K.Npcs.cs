using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> Wh40kAmbientNpcsEnabled =
        CVarDef.Create("wh40k.npcs.ambient_enabled", true, CVar.SERVERONLY);
    public static readonly CVarDef<int> Wh40kAmbientNpcLimit =
        CVarDef.Create("wh40k.npcs.ambient_limit", 163, CVar.SERVERONLY);
    public static readonly CVarDef<string> Wh40kAmbientDisabledDepartments =
        CVarDef.Create("wh40k.npcs.ambient_disabled_departments", "", CVar.SERVERONLY);
    public static readonly CVarDef<bool> Wh40kAmbientChatterEnabled =
        CVarDef.Create("wh40k.npcs.chatter_enabled", true, CVar.SERVERONLY);
    public static readonly CVarDef<float> Wh40kAmbientChatterGlobalDelay =
        CVarDef.Create("wh40k.npcs.chatter_global_delay", 6f, CVar.SERVERONLY);
    public static readonly CVarDef<float> Wh40kAmbientChatterDepartmentDelay =
        CVarDef.Create("wh40k.npcs.chatter_department_delay", 20f, CVar.SERVERONLY);
}
