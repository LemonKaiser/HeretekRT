using Content.Shared._WH40K.ShuttleStorage;
using Robust.Client.GameObjects;

namespace Content.Client._WH40K.ShuttleStorage;

public sealed class ShuttleStorageBoundUserInterface(EntityUid owner, Enum key) : BoundUserInterface(owner, key)
{
    private ShuttleStorageWindow? _window;
    private ShuttleStorageSnapshot? _snapshot;

    protected override void Open()
    {
        base.Open();
        _window = new ShuttleStorageWindow();
        _window.OnClose += Close;
        _window.OnTransfer += SendRequest;
        if (_snapshot != null)
            _window.Apply(_snapshot);
        _window.OpenCentered();
        Refresh();
    }

    private void Refresh() => SendMessage(new ShuttleStorageRefresh());
    private void SendRequest(ShuttleStorageRequest request) => SendMessage(request);

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is not ShuttleStorageSnapshot snapshot || _snapshot != null && snapshot.Sequence <= _snapshot.Sequence)
            return;
        _snapshot = snapshot;
        _window?.Apply(snapshot);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null)
        {
            _window.OnTransfer -= SendRequest;
            _window.OnClose -= Close;
            _window.Dispose();
            _window = null;
            _snapshot = null;
        }
        base.Dispose(disposing);
    }
}
