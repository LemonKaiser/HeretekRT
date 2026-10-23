using Content.Shared._WH40K.FactionStorage;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.FactionStorage;

public sealed class FactionStorageBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FactionStorageWindow? _window;
    private FactionStorageStateMessage? _state;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<FactionStorageWindow>();
        _window.OnTransfer += (slot, revision, deposit) => SendMessage(new FactionStorageTransferMessage(slot, revision, deposit));
        _window.OnRefresh += () => SendMessage(new FactionStorageRefreshMessage());
        if (_state != null)
            _window.UpdateState(_state);
        SendMessage(new FactionStorageRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is not FactionStorageStateMessage state)
            return;
        _state = state;
        _window?.UpdateState(state);
    }
}
