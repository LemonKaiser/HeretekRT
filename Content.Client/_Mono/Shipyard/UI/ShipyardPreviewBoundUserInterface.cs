using static Content.Shared._Mono.Shipyard.SharedPreview;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Mono.Shipyard.UI;

public sealed class ShipyardPreviewBoundUserInterface : BoundUserInterface
{
    private ShipyardPreviewMenu? _menu;
    private bool _exitRequested;

    public ShipyardPreviewBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {

    }

    protected override void Open()
    {
        base.Open();

        _menu = new ShipyardPreviewMenu();
        _menu.OpenCentered();
        _menu.UpdateMenu();

        _menu.OnExit += Exit;
        _menu.OnClose += RequestExit;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        if (_menu != null)
        {
            _menu.OnExit -= Exit;
            _menu.OnClose -= RequestExit;
        }

        _menu?.Dispose();
        _menu = null;
    }

    private void Exit(ButtonEventArgs args)
    {
        RequestExit();
    }

    private void RequestExit()
    {
        if (_exitRequested)
            return;

        _exitRequested = true;
        SendMessage(new ShipyardPreviewExitMessage());
    }
}
