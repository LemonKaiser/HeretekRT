namespace Content.Shared._WH40K.SectorMap;

/// <summary>
/// Resolves authored localization keys while retaining literal names used by older sector data.
/// </summary>
public static class KoronusLocalization
{
    public static string GetString(string text)
    {
        return !string.IsNullOrEmpty(text) && Loc.TryGetString(text, out var localized) ? localized : text;
    }
}
