using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Content.Shared.IdentityManagement;
using Content.Shared.Localizations;
using Robust.Client.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Client.Localization;

/// <summary>
/// Resolves entity names for the local player without trusting a name that was
/// serialized by the server in a different interface culture.
/// </summary>
public sealed class WH40KEntityNameLocalizer : EntitySystem
{
    [Dependency] private ILocalizationManager _localization = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IClientGameStateManager _gameStateManager = default!;

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
        EntityManager.EntityInitialized += OnEntityInitialized;
        _gameStateManager.GameStateApplied += OnGameStateApplied;
        RebuildSnapshots();
    }

    public override void Shutdown()
    {
        _gameStateManager.GameStateApplied -= OnGameStateApplied;
        EntityManager.EntityInitialized -= OnEntityInitialized;
        base.Shutdown();
    }

    /// <summary>
    /// Returns a display name in the current client culture. Runtime names (player
    /// names, renamed entities, station names, and similar values) are preserved.
    /// </summary>
    public string GetName(EntityUid entity, EntityUid? examiner = null)
    {
        var metadata = MetaData(entity);
        var name = examiner is { } user
            ? Identity.Name(entity, EntityManager, user)
            : Identity.Name(entity, EntityManager);

        try
        {
            if (metadata.EntityPrototype is not { } prototype)
                return name;

            var currentCulture = ContentLocalizationManager.ValidateCultureName(_localization.DefaultCulture?.Name)
                                 ?? ContentLocalizationManager.DefaultCultureName;
            var current = GetEntityData(currentCulture, prototype.ID).Name;
            if (string.IsNullOrWhiteSpace(current))
                return name;

            if (string.Equals(name, current, StringComparison.Ordinal))
                return name;

            // The server may have populated metadata in either supported culture.
            // Only replace it when it matches a known prototype translation, so a
            // custom runtime name is never overwritten.
            var russian = GetEntityData(ContentLocalizationManager.DefaultCultureName, prototype.ID).Name;
            var english = GetEntityData(ContentLocalizationManager.FallbackCultureName, prototype.ID).Name;
            return string.Equals(name, russian, StringComparison.Ordinal) ||
                   string.Equals(name, english, StringComparison.Ordinal)
                ? current
                : name;
        }
        catch (Exception e)
        {
            Log.Debug($"Unable to resolve localized name for {entity}: {e}");
            return name;
        }
    }

    /// <summary>
    /// Returns the prototype description when the synchronized metadata contains
    /// a default translation from another supported culture.
    /// </summary>
    public string GetDescription(EntityUid entity)
    {
        var metadata = MetaData(entity);
        try
        {
            if (metadata.EntityPrototype is not { } prototype)
                return metadata.EntityDescription;

            var currentCulture = ContentLocalizationManager.ValidateCultureName(_localization.DefaultCulture?.Name)
                                 ?? ContentLocalizationManager.DefaultCultureName;
            var current = GetEntityData(currentCulture, prototype.ID).Desc;
            if (string.IsNullOrWhiteSpace(current))
                return metadata.EntityDescription;
            if (string.Equals(metadata.EntityDescription, current, StringComparison.Ordinal))
                return current;

            var russian = GetEntityData(ContentLocalizationManager.DefaultCultureName, prototype.ID).Desc;
            var english = GetEntityData(ContentLocalizationManager.FallbackCultureName, prototype.ID).Desc;
            return string.Equals(metadata.EntityDescription, russian, StringComparison.Ordinal) ||
                   string.Equals(metadata.EntityDescription, english, StringComparison.Ordinal)
                ? current
                : metadata.EntityDescription;
        }
        catch (Exception e)
        {
            Log.Debug($"Unable to resolve localized description for {entity}: {e}");
            return metadata.EntityDescription;
        }
    }

    /// <summary>
    /// Normalizes prototype-backed metadata after a state update. This keeps controls that read
    /// <see cref="MetaDataComponent.EntityName"/> directly consistent with controls that use
    /// <see cref="GetName"/> without overwriting runtime names.
    /// </summary>
    public void RefreshEntityMetadata()
    {
        try
        {
            foreach (var entity in EntityManager.AllEntities<MetaDataComponent>())
            {
                try
                {
                    RefreshEntityMetadata(entity.Owner, entity.Comp);
                }
                catch (Exception e)
                {
                    Log.Debug($"Skipped localized metadata refresh for {entity.Owner}: {e}");
                }
            }
        }
        catch (Exception e)
        {
            // A culture cvar may be applied by the command line before the
            // entity manager has completed its first initialization pass.
            Log.Debug($"Localized metadata refresh is not available yet: {e}");
        }
    }

    private void OnEntityInitialized(Entity<MetaDataComponent> entity)
    {
        RefreshEntityMetadata(entity.Owner, entity.Comp);
    }

    private void OnGameStateApplied(GameStateAppliedArgs args)
    {
        if (!args.AppliedState.EntityStates.HasContents)
            return;

        if (args.AppliedState.EntityStates.Value is not { } entityStates)
            return;

        foreach (var entityState in entityStates)
        {
            if (entityState.ComponentChanges.Value is not { } componentChanges)
                continue;

            var hasMetadataChange = false;
            foreach (var change in componentChanges)
            {
                if (change.State is MetaDataComponentState)
                {
                    hasMetadataChange = true;
                    break;
                }
            }

            if (!hasMetadataChange || !EntityManager.TryGetEntity(entityState.NetEntity, out var uid) || uid is not { } entity)
                continue;

            if (TryComp(entity, out MetaDataComponent? metadata))
                RefreshEntityMetadata(entity, metadata);
        }
    }

    private void RefreshEntityMetadata(EntityUid entity, MetaDataComponent metadata)
    {
        if (metadata.EntityPrototype == null)
            return;

        var name = GetName(entity);
        if (!string.Equals(name, metadata.EntityName, StringComparison.Ordinal))
            _metadata.SetEntityName(entity, name, metadata, raiseEvents: false);

        var description = GetDescription(entity);
        if (!string.Equals(description, metadata.EntityDescription, StringComparison.Ordinal))
            _metadata.SetEntityDescription(entity, description, metadata);
    }

    private EntityLocData GetEntityData(string cultureName, string prototypeId)
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
