using System;
using Content.Shared.Localizations;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;

namespace Content.Client.UserInterface.Systems.Localization;

/// <summary>
/// Owns the transactional language-selection flow.
///
/// The selected culture is deliberately not applied to the running UI. Fluent bindings and
/// prototype metadata are initialized during client startup, so changing the culture in-place
/// would leave a mixture of old and new strings in already-created controls. The only mutation
/// made by this controller is the persisted cvar after the player confirms the restart prompt.
/// </summary>
[UsedImplicitly]
public sealed class LocalizationUIController : UIController
{
    [Dependency] private IConfigurationManager _config = default!;

    private bool _normalizing;
    private string? _currentCultureName;
    private LanguageRestartPopup? _restartPopup;

    public bool HasPendingCultureChange => _restartPopup is { Disposed: false };

    public override void Initialize()
    {
        base.Initialize();

        // ContentLocalizationManager applies the persisted culture before UI controllers are
        // initialized. Keep it as the committed baseline; controls may still have a pending
        // value which has not been written to the cvar yet.
        _currentCultureName = ContentLocalizationManager.ValidateCultureName(
            _config.GetCVar(CVars.LocCultureName)) ?? ContentLocalizationManager.DefaultCultureName;
        _config.OnValueChanged(CVars.LocCultureName, OnCultureChanged, invokeImmediately: false);
    }

    private void OnCultureChanged(string requestedName)
    {
        if (_normalizing)
            return;

        var canonicalName = ContentLocalizationManager.ValidateCultureName(requestedName)
                            ?? ContentLocalizationManager.DefaultCultureName;
        if (!string.Equals(requestedName, canonicalName, StringComparison.Ordinal))
        {
            _normalizing = true;
            try
            {
                _config.SetCVar(CVars.LocCultureName, canonicalName);
            }
            finally
            {
                _normalizing = false;
            }
        }

        // A cvar changed outside this controller (for example by a console command) is still
        // only a pending startup setting. Never reload an already-running UI here.
        _currentCultureName = canonicalName;
    }

    /// <summary>
    /// Requests a culture change. The cvar is written only after the player presses OK.
    /// </summary>
    public void RequestCultureChange(string requestedName, Action? onCancelled = null, Action? onConfirmed = null)
    {
        var canonicalName = ContentLocalizationManager.ValidateCultureName(requestedName);
        if (canonicalName == null)
            return;

        var currentName = _currentCultureName ??
                          ContentLocalizationManager.ValidateCultureName(_config.GetCVar(CVars.LocCultureName)) ??
                          ContentLocalizationManager.DefaultCultureName;

        if (string.Equals(currentName, canonicalName, StringComparison.OrdinalIgnoreCase))
        {
            onConfirmed?.Invoke();
            return;
        }

        if (_restartPopup is { Disposed: false })
            return;

        var popup = _restartPopup = new LanguageRestartPopup(canonicalName);
        var settled = false;

        void Cancel()
        {
            if (settled)
                return;

            settled = true;
            if (popup is { Disposed: false })
                popup.Close();
            if (ReferenceEquals(_restartPopup, popup))
                _restartPopup = null;
            onCancelled?.Invoke();
        }

        void Confirm()
        {
            if (settled)
                return;

            settled = true;
            _config.SetCVar(CVars.LocCultureName, canonicalName);
            _config.SaveToFile();
            _currentCultureName = canonicalName;
            if (popup is { Disposed: false })
                popup.Close();
            if (ReferenceEquals(_restartPopup, popup))
                _restartPopup = null;
            onConfirmed?.Invoke();
        }

        popup.OnCancelled += Cancel;
        popup.OnConfirmed += Confirm;
        popup.OnClose += () =>
        {
            if (!settled)
                Cancel();
        };
        popup.OpenCentered();
    }
}

/// <summary>
/// Implemented by content controls that keep localized values outside XAML Loc bindings.
/// </summary>
public interface ILocalizationRefreshable
{
    void RefreshLocalization();
}
