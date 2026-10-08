using System;
using System.Collections.Generic;
using RuntimeHelpers = System.Runtime.CompilerServices.RuntimeHelpers;
using Content.Shared.Localizations;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Network;

namespace Content.Client.Localization;

/// <summary>
/// Receives the server's culture handshake confirmation.
/// The confirmation is intentionally kept in a client system so the UI controller
/// does not need to depend on the entity-system network subscription API.
/// </summary>
public sealed class LocalizationCultureHandshakeSystem : EntitySystem
{
    private static readonly object RegistrationSync = new();
    private static readonly Dictionary<INetManager, LocalizationCultureHandshakeSystem> Instances =
        new(ReferenceComparer.Instance);
    private static readonly HashSet<INetManager> RegisteredManagers = new(ReferenceComparer.Instance);

    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ILocalizationManager _localization = default!;

    public string? RequestedCultureName { get; private set; }
    public string? AcceptedCultureName { get; private set; }
    public event Action? CultureConfirmed;
    private float _retryCooldown;
    public bool IsCultureConfirmed => _netManager.IsConnected && RequestedCultureName != null
        && RequestedCultureName == AcceptedCultureName;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (IsCultureConfirmed || !_netManager.IsConnected || RequestedCultureName == null)
            return;

        _retryCooldown -= frameTime;
        TrySendRequest();
    }

    public override void Initialize()
    {
        base.Initialize();
        RegisterNetworkMessages(_netManager);
        lock (RegistrationSync)
            Instances[_netManager] = this;
        _netManager.Connected += OnConnected;
        _netManager.Disconnect += OnDisconnected;
        
        // Entity systems may initialize after the network connection has already been
        // established in tests and during a reconnect. Do not rely solely on Connected.
        if (_netManager.IsConnected)
            RequestCulture(GetActiveCultureName());
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
        _netManager.Connected -= OnConnected;
        _netManager.Disconnect -= OnDisconnected;
        lock (RegistrationSync)
        {
            if (Instances.TryGetValue(_netManager, out var instance) && ReferenceEquals(instance, this))
                Instances.Remove(_netManager);
        }
        CultureConfirmed = null;
        base.Shutdown();
    }

    public void RequestCulture(string cultureName)
    {
        RequestedCultureName = ContentLocalizationManager.ValidateCultureName(cultureName)
                               ?? ContentLocalizationManager.DefaultCultureName;
        AcceptedCultureName = null;
        // Connected is raised before the server's per-connection string table is
        // guaranteed to be installed on the client. Give that handshake a tick before
        // sending the first content message; retries cover slower connections.
        _retryCooldown = 0.2f;
        TrySendRequest();
    }

    private void OnConnected(object? sender, NetChannelArgs args)
    {
        RequestCulture(GetActiveCultureName());
    }

    private void OnDisconnected(object? sender, NetDisconnectedArgs args)
    {
        AcceptedCultureName = null;
        _retryCooldown = 0;
    }

    private void TrySendRequest()
    {
        if (_retryCooldown > 0 || !_netManager.IsConnected || RequestedCultureName == null)
            return;

        _netManager.ClientSendMessage(new MsgSetClientCulture { CultureName = RequestedCultureName });
        // A connection can exist before its player session does. Retry until the server
        // acknowledges instead of losing the first request during that startup window.
        _retryCooldown = 0.2f;
    }

    private void OnCultureAccepted(MsgSetClientCulture message)
    {
        if (!message.Accepted)
            return;

        var culture = ContentLocalizationManager.ValidateCultureName(message.CultureName);
        if (culture == null)
        {
            Log.Warning("Server acknowledged unsupported localization culture '{CultureName}'.", message.CultureName);
            return;
        }

        // Rapid switches can leave older acknowledgements in flight. Only confirm the latest request.
        if (culture == RequestedCultureName)
        {
            AcceptedCultureName = culture;
            CultureConfirmed?.Invoke();
        }
    }

    private static void Dispatch(INetManager network, MsgSetClientCulture message)
    {
        LocalizationCultureHandshakeSystem? instance;
        lock (RegistrationSync)
            Instances.TryGetValue(network, out instance);

        instance?.OnCultureAccepted(message);
    }

    private sealed class ReferenceComparer : IEqualityComparer<INetManager>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(INetManager? x, INetManager? y) => ReferenceEquals(x, y);

        public int GetHashCode(INetManager obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private string GetActiveCultureName()
    {
        return ContentLocalizationManager.ValidateCultureName(_localization.DefaultCulture?.Name)
               ?? ContentLocalizationManager.DefaultCultureName;
    }
}
