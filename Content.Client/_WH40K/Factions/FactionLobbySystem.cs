using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Network;

namespace Content.Client._WH40K.Factions;

public sealed class FactionLobbySystem : EntitySystem
{
    [Dependency] private IClientNetManager _net = default!;
    public Wh40kFactionUiSnapshot? Snapshot { get; private set; }
    public bool Pending { get; private set; }
    private float _pendingSeconds;
    public event Action<Wh40kFactionUiOperationStatus>? Updated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<FactionLobbyStateEvent>(OnState);
        _net.Disconnect += OnDisconnect;
    }

    public override void Shutdown()
    {
        _net.Disconnect -= OnDisconnect;
        base.Shutdown();
    }

    public void Request(Wh40kFactionUiAction action = Wh40kFactionUiAction.Refresh, string factionId = "", Guid targetUserId = default,
        FactionProfile? profile = null, FactionBankPayout? payout = null, FactionSocialRequest? social = null)
    {
        if (Pending || !_net.IsConnected)
            return;
        Pending = true;
        _pendingSeconds = 0;
        Updated?.Invoke(Wh40kFactionUiOperationStatus.None);
        RaiseNetworkEvent(new FactionLobbyRequestEvent(action, factionId, targetUserId, profile, payout, social));
    }

    private void OnState(FactionLobbyStateEvent state)
    {
        Snapshot = state.Snapshot;
        Pending = false;
        Updated?.Invoke(state.Status);
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        Snapshot = null;
        Pending = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!Pending)
            return;
        _pendingSeconds += frameTime;
        if (_pendingSeconds < 12)
            return;
        Pending = false;
        Updated?.Invoke(Wh40kFactionUiOperationStatus.Failed);
    }
}
