using System;
using System.Globalization;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Shared.Localizations;

/// <summary>
/// Temporarily selects a player's culture while a server-side localized value is built.
/// </summary>
/// <remarks>
/// The localization manager stores the selected culture globally. Keep this scope short, on the
/// game thread, and never cross an await or start background work while it is active.
/// </remarks>
public readonly struct LocalizationCultureScope : IDisposable
{
    private readonly ILocalizationManager _localization;
    private readonly CultureInfo? _previousCulture;
    private readonly bool _restoreCulture;

    public LocalizationCultureScope(ILocalizationManager localization, string? cultureName)
    {
        _localization = localization;
        _previousCulture = localization.DefaultCulture;

        var canonicalName = ContentLocalizationManager.ValidateCultureName(cultureName)
                            ?? ContentLocalizationManager.DefaultCultureName;
        var culture = CultureInfo.GetCultureInfo(canonicalName, predefinedOnly: true);

        if (_previousCulture?.NameEquals(culture) == true)
        {
            _restoreCulture = false;
            return;
        }

        localization.SetCulture(culture);
        _restoreCulture = true;
    }

    public void Dispose()
    {
        if (_restoreCulture && _previousCulture != null)
            _localization.SetCulture(_previousCulture);
    }
}
