using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Controls access to WH40K decorations.
    /// Allowed values: <c>false</c>, <c>admin</c>, and <c>all</c>.
    /// </summary>
    public static readonly CVarDef<string> Wh40kDecorationsMode =
        CVarDef.Create(
            "wh40k.decorations_mode",
            "admin",
            CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     If true, the standard staff OOC colour takes priority over the decoration tint
    ///     of an interactive admin ghost. The selected ghost skin is kept in either mode.
    /// </summary>
    public static readonly CVarDef<bool> Wh40kDecorationsAdminVisualPriority =
        CVarDef.Create(
            "wh40k.decorations_admin_visual_priority",
            true,
            CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Controls extending the selected title effect to the complete OOC/LOOC line.
    ///     Allowed values: <c>false</c>, <c>admin</c>, and <c>all</c>.
    /// </summary>
    public static readonly CVarDef<string> Wh40kDecorationsFullLineMode =
        CVarDef.Create(
            "wh40k.decorations_full_line_mode",
            "false",
            CVar.SERVERONLY | CVar.ARCHIVE);
}
