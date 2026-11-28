using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.MarkingGlow;

/// <summary>
/// Server settings controlling whether player-chosen markings, eyes, and hair can glow unshaded.
/// </summary>
[CVarDefs]
public sealed class Wh40kMarkingGlowCVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wh40k.marking_glow.enabled", true, CVar.SERVER | CVar.REPLICATED);
}
