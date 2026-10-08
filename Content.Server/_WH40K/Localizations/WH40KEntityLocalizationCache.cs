using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Content.Shared.Localizations;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Localizations;

/// <summary>
/// Caches localized entity prototype data per culture without reaching into the engine's private cache.
/// </summary>
public sealed class WH40KEntityLocalizationCache : EntitySystem
{
    [Dependency] private ILocalizationManager _localization = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly object _sync = new();
    private Dictionary<(string Culture, string Prototype), EntityLocData> _cache = new();
    private static readonly EntityLocData Empty = new(
        string.Empty,
        string.Empty,
        null,
        ImmutableDictionary<string, string>.Empty);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        RebuildSnapshots();
    }

    /// <summary>
    /// Gets immutable localized data for a prototype in the requested player culture.
    /// </summary>
    public EntityLocData Get(string? cultureName, string prototypeId)
    {
        var culture = ContentLocalizationManager.ValidateCultureName(cultureName)
                      ?? ContentLocalizationManager.DefaultCultureName;
        var key = (culture, prototypeId);

        lock (_sync)
            return _cache.TryGetValue(key, out var cached) ? cached : Empty;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>())
            return;

        RebuildSnapshots();
    }

    /// <summary>
    /// Builds all supported cultures once while the server is in a controlled startup/reload
    /// path. Requests only read the resulting immutable-by-convention snapshot and never mutate
    /// the global localization manager.
    /// </summary>
    private void RebuildSnapshots()
    {
        var previousCulture = ContentLocalizationManager.ValidateCultureName(_localization.DefaultCulture?.Name)
                               ?? ContentLocalizationManager.DefaultCultureName;
        var snapshot = new Dictionary<(string Culture, string Prototype), EntityLocData>();

        try
        {
            foreach (var culture in ContentLocalizationManager.SupportedCultureNames)
            {
                using var scope = new LocalizationCultureScope(_localization, culture);
                // ReloadLocalizations also flushes Robust's global entity cache. This is deliberately
                // done only while rebuilding, never from Get().
                _localization.ReloadLocalizations();

                foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
                    snapshot[(culture, prototype.ID)] = _localization.GetEntityData(prototype.ID);
            }
        }
        finally
        {
            using var restore = new LocalizationCultureScope(_localization, previousCulture);
            _localization.ReloadLocalizations();
        }

        lock (_sync)
            _cache = snapshot;
    }
}
