using System;
using System.Collections.Generic;
using RuntimeHelpers = System.Runtime.CompilerServices.RuntimeHelpers;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Content.Shared.Localizations;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Localizations;

/// <summary>
/// Stores each connected client's interface culture and formats personal server responses.
/// Automatic chat translation is intentionally not part of this manager.
/// </summary>
public sealed class WH40KPlayerCultureManager : EntitySystem
{
    private static readonly object RegistrationSync = new();
    private static readonly Dictionary<INetManager, WH40KPlayerCultureManager> Instances =
        new(ReferenceComparer.Instance);
    private static readonly HashSet<INetManager> RegisteredManagers = new(ReferenceComparer.Instance);

    [Dependency] private ILocalizationManager _localization = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private WH40KEntityLocalizationCache _entityLocalization = default!;

    private readonly Dictionary<NetUserId, string> _cultures = new();

    /// <summary>
    /// Returns whether this session has completed the culture handshake.
    /// Until then callers should treat its culture as the server default.
    /// </summary>
    public bool IsCultureReady(ICommonSession session)
    {
        return _cultures.ContainsKey(session.UserId);
    }

    public override void Initialize()
    {
        base.Initialize();
        RegisterNetworkMessages(_netManager);
        lock (RegistrationSync)
            Instances[_netManager] = this;
        
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public static void RegisterNetworkMessages(INetManager network)
    {
        lock (RegistrationSync)
        {
            if (RegisteredManagers.Add(network))
            {
                network.RegisterNetMessage<MsgSetClientCulture>(message => Dispatch(network, message));
            }
        }
    }

    public override void Shutdown()
    {
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        lock (RegistrationSync)
        {
            if (Instances.TryGetValue(_netManager, out var instance) && ReferenceEquals(instance, this))
                Instances.Remove(_netManager);
        }
        _cultures.Clear();
        base.Shutdown();
    }

    public string GetCulture(ICommonSession session)
    {
        return _cultures.TryGetValue(session.UserId, out var culture)
            ? culture
            : ContentLocalizationManager.DefaultCultureName;
    }

    public string GetCulture(EntityUid player)
    {
        return TryComp<ActorComponent>(player, out var actor)
            ? GetCulture(actor.PlayerSession)
            : ContentLocalizationManager.DefaultCultureName;
    }

    public LocalizationCultureScope CreateScope(ICommonSession session)
    {
        return new LocalizationCultureScope(_localization, GetCulture(session));
    }

    /// <summary>
    /// Creates a response scope using the culture stored for the session. The request's
    /// culture field is intentionally ignored; it is an echo for stale-response filtering,
    /// not an authority supplied by the client.
    /// </summary>
    public LocalizationCultureScope CreateScope(ICommonSession session, string? requestedCulture)
    {
        var storedCulture = GetCulture(session);
        var requested = ContentLocalizationManager.ValidateCultureName(requestedCulture);
        if (requested != null && !string.Equals(requested, storedCulture, StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("Ignoring culture '{RequestedCulture}' from {UserId}; stored culture is '{StoredCulture}'.",
                requested, session.UserId, storedCulture);
        }

        return new LocalizationCultureScope(_localization, storedCulture);
    }

    public LocalizationCultureScope CreateScope(EntityUid player)
    {
        return new LocalizationCultureScope(_localization, GetCulture(player));
    }

    public string GetPlayerString(EntityUid player, string messageId, params (string, object)[] args)
    {
        return TryComp<ActorComponent>(player, out var actor)
            ? GetPlayerString(actor.PlayerSession, messageId, args)
            : Loc.GetString(messageId, args);
    }

    public string GetPlayerString(ICommonSession session, string messageId, params (string, object)[] args)
    {
        using var scope = CreateScope(session);
        return Loc.GetString(messageId, LocalizeArguments(session, args));
    }

    public string Format(ICommonSession session, string messageId, params (string, object)[] args)
    {
        return GetPlayerString(session, messageId, args);
    }

    public T WithCulture<T>(ICommonSession session, Func<T> callback)
    {
        using var scope = CreateScope(session);
        return callback();
    }

    public void WithCulture(ICommonSession session, Action callback)
    {
        using var scope = CreateScope(session);
        callback();
    }

    public T WithCulture<T>(EntityUid player, Func<T> callback)
    {
        using var scope = CreateScope(player);
        return callback();
    }

    public void WithCulture(EntityUid player, Action callback)
    {
        using var scope = CreateScope(player);
        callback();
    }

    private (string, object)[] LocalizeArguments(ICommonSession session, (string, object)[] args)
    {
        if (args.Length == 0)
            return args;

        var localized = new (string, object)[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            var value = args[i].Item2;
            localized[i] = (args[i].Item1, value is EntityUid entity
                ? GetPlayerEntityName(session, entity)
                : value);
        }

        return localized;
    }

    private string GetPlayerEntityName(ICommonSession session, EntityUid entity)
    {
        var name = Identity.Name(entity, EntityManager, session.AttachedEntity);
        if (!TryComp(entity, out MetaDataComponent? metadata) || metadata.EntityPrototype is not { } prototype)
            return name;

        var culture = GetCulture(session);
        var localized = _entityLocalization.Get(culture, prototype.ID).Name;
        var russian = _entityLocalization.Get(ContentLocalizationManager.DefaultCultureName, prototype.ID).Name;
        var english = _entityLocalization.Get(ContentLocalizationManager.FallbackCultureName, prototype.ID).Name;

        return !string.IsNullOrWhiteSpace(localized) &&
               (string.Equals(name, russian, StringComparison.Ordinal) ||
                string.Equals(name, english, StringComparison.Ordinal))
            ? localized
            : name;
    }

    private void OnSetClientCulture(MsgSetClientCulture message)
    {
        if (message.Accepted)
            return;


        if (!_playerManager.TryGetSessionById(message.MsgChannel.UserId, out var session))
        {
            return;
        }

        var culture = ContentLocalizationManager.ValidateCultureName(message.CultureName);
        if (culture == null)
        {
            culture = ContentLocalizationManager.DefaultCultureName;
            Log.Warning("Client {UserId} sent unsupported culture '{CultureName}', using {Fallback}.",
                session.UserId, message.CultureName, culture);
        }

        _cultures[session.UserId] = culture;
        _netManager.ServerSendMessage(new MsgSetClientCulture { CultureName = culture, Accepted = true }, message.MsgChannel);
        RaiseLocalEvent(new PlayerCultureChangedEvent(session));
    }

    private static void Dispatch(INetManager network, MsgSetClientCulture message)
    {
        WH40KPlayerCultureManager? instance;
        lock (RegistrationSync)
            Instances.TryGetValue(network, out instance);

        instance?.OnSetClientCulture(message);
    }

    private sealed class ReferenceComparer : IEqualityComparer<INetManager>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(INetManager? x, INetManager? y) => ReferenceEquals(x, y);

        public int GetHashCode(INetManager obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _cultures.Remove(args.Session.UserId);
    }
}
