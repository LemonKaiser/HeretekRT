using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Cosmetic particle quality: 0 = disabled, 1 = low, 2 = medium, 3 = high.
    /// </summary>
    public static readonly CVarDef<int> ParticleQuality =
        CVarDef.Create("particles.quality", 3, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Upper bound for the global particle budget. Quality presets may impose a lower bound.
    /// </summary>
    public static readonly CVarDef<int> ParticleGlobalBudget =
        CVarDef.Create("particles.global_budget", 10000, CVar.CLIENTONLY | CVar.ARCHIVE);
}
