using Robust.Shared.Utility;

namespace Content.Shared.Construction.Prototypes;

public sealed partial class ConstructionPrototype
{
    public string LocalizedName => Localize(Name, $"construction-recipe-{CaseConversion.PascalToKebab(ID)}");

    public string LocalizedDescription => Localize(Description, $"construction-recipe-{CaseConversion.PascalToKebab(ID)}-desc");

    private static string Localize(string text, string legacyKey)
    {
        var localization = IoCManager.Resolve<ILocalizationManager>();
        if (!string.IsNullOrEmpty(text) && localization.TryGetString(text, out var localized))
            return localized;

        // Older recipes use literal English text and separate translations keyed by recipe ID.
        return localization.TryGetString(legacyKey, out var legacy) ? legacy : text;
    }
}
