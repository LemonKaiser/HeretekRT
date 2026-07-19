using Robust.Shared.Configuration;

namespace Content.Shared.Durability;

/// <summary>
/// Client preferences for displaying item durability.
/// </summary>
[CVarDefs]
public sealed class DurabilityCVars
{
    public static readonly CVarDef<int> BarVisibility = CVarDef.Create(
        "durability.bar_visibility",
        (int) DurabilityBarVisibility.HeldOnly,
        CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> BarColor = CVarDef.Create(
        "durability.bar_color",
        (int) DurabilityBarColor.Gradient,
        CVar.CLIENTONLY | CVar.ARCHIVE);
}

public enum DurabilityBarVisibility
{
    Always,
    HeldOnly,
    Never,
}

public enum DurabilityBarColor
{
    White,
    Gradient,
}
