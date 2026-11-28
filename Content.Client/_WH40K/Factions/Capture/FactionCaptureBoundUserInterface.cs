using Content.Shared._WH40K.Factions.Capture;
using Robust.Client.GameObjects;

namespace Content.Client._WH40K.Factions.Capture;

public sealed class FactionCaptureBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FactionCaptureWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = new FactionCaptureWindow();
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is FactionCaptureUiState capture)
            _window?.ShowCaptureState(capture);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _window != null)
        {
            _window.OnClose -= Close;
            _window.Close();
            _window = null;
        }
    }
}
