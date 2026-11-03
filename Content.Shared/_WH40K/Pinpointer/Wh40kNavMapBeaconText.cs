using Content.Shared.Pinpointer;

namespace Content.Shared._WH40K.Pinpointer;

public static class Wh40kNavMapBeaconText
{
    public static string GetText(NavMapBeaconComponent beacon)
    {
        return GetText(beacon.Text, beacon.TextEnglish, beacon.DefaultText);
    }

    public static string GetText(string? text, string? textEnglish, LocId? defaultText, string? culture = null)
    {
        culture ??= IoCManager.Resolve<ILocalizationManager>().DefaultCulture?.Name;
        if (culture?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true &&
            !string.IsNullOrWhiteSpace(textEnglish))
            return textEnglish;

        if (!string.IsNullOrWhiteSpace(text))
            return text;

        if (!string.IsNullOrWhiteSpace(textEnglish))
            return textEnglish;

        if (defaultText is { } locId && Loc.TryGetString(locId, out var localized))
            return localized;

        return string.Empty;
    }
}
