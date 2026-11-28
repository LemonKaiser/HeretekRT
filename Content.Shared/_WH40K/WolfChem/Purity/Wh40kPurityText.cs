using Content.Shared.Chemistry.Components;

namespace Content.Shared._WH40K.WolfChem.Purity;

/// <summary>
/// Formats a reagent name with its grade for UIs; standard grade leaves the name as it is.
/// </summary>
public static class Wh40kPurityText
{
    /// <summary>The name with a percentage beside it when the grade is below standard.</summary>
    public static string Name(string name, float purity)
    {
        return purity >= Solution.StandardPurityThreshold
            ? name
            : Loc.GetString("wh40k-purity-reagent-name", ("name", name), ("purity", Percent(purity)));
    }

    /// <summary>The name with the grade looked up in a solution's purity table.</summary>
    public static string Name(string name, Dictionary<string, float>? purity, string prototype)
    {
        return purity != null && purity.TryGetValue(prototype, out var value) ? Name(name, value) : name;
    }

    /// <summary>A grade as a whole percentage.</summary>
    public static int Percent(float purity)
    {
        return (int) MathF.Round(purity * 100f);
    }
}
