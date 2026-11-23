using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.Explosion;

/// <summary>
/// Tuning for the distortion ring explosions throw out across the screen.
/// </summary>
[CVarDefs]
public sealed class Wh40kShockwaveCVars
{
    /// <summary>
    /// Whether explosions push a distortion ring out across the screen.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wh40k.shockwave.enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Multiplier on how far the ring displaces the world. 0 is flat, 1 is the tuned default.
    /// </summary>
    public static readonly CVarDef<float> Strength =
        CVarDef.Create("wh40k.shockwave.strength", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// How far past the edge of the blast the wave reaches, in tiles.
    /// </summary>
    public static readonly CVarDef<float> Overshoot =
        CVarDef.Create("wh40k.shockwave.overshoot", 3f, CVar.SERVER | CVar.REPLICATED);
}
