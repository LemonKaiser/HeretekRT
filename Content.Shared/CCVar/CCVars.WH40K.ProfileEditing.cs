using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Controls changes to an already created WH40K character profile.
    /// Allowed values: <c>false</c>, <c>appearance</c>, and <c>full</c>.
    /// </summary>
    public static readonly CVarDef<string> Wh40kProfileEditMode =
        CVarDef.Create(
            "wh40k.profile_edit_mode",
            "false",
            CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Allows active administrators and moderators to ignore <see cref="Wh40kProfileEditMode"/>.
    /// </summary>
    public static readonly CVarDef<bool> Wh40kProfileEditAdminBypass =
        CVarDef.Create(
            "wh40k.profile_edit_admin_bypass",
            true,
            CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);
}
