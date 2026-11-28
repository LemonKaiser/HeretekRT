using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.RadarLabels;

/// <summary>
/// Client preferences controlling tactical radar contact tags and icons.
/// </summary>
[CVarDefs]
public sealed class Wh40kRadarCVars
{
    /// <summary>
    /// When enabled, only priority/nearby contacts display names everywhere; overlapping tags stack cleanly,
    /// and hovering reveals full details.
    /// </summary>
    public static readonly CVarDef<bool> RadarSmartTags =
        CVarDef.Create("wh40k.radar.smart_tags", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Hides contact names and crowd counts until the cursor hovers directly over a contact.
    /// </summary>
    public static readonly CVarDef<bool> RadarIconsOnly =
        CVarDef.Create("wh40k.radar.icons_only", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Reveals contact names and details under the cursor lens.
    /// </summary>
    public static readonly CVarDef<bool> RadarHoverReveal =
        CVarDef.Create("wh40k.radar.hover_reveal", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Persisted toggle for the radar scope's "all contact names" mode button.
    /// </summary>
    public static readonly CVarDef<bool> RadarAllNames =
        CVarDef.Create("wh40k.radar.all_names", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
