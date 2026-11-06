using Robust.Shared.Prototypes;
using static Content.Shared._Mono.Shipyard.SharedPreview;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Mono.Shipyard.UI;

public sealed class ShipyardPreviewBoundUserInterface : BoundUserInterface
{
    private ShipyardPreviewMenu? _menu;
    [Dependency] private ShipyardPreviewSystem _preview = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

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
        _menu.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ShipyardPreviewUserInterfaceState previewState)
            return;

        if (!_prototypes.TryIndex(previewState.Vessel, out var vessel)
            || !_preview.TryPreviewGrid(vessel, EntMan.GetComponent<TransformComponent>(Owner).MapID))
        {
            Close();
            return;
        }

        _menu?.UpdateMenu();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _preview.Dispose();
        _menu?.Dispose();
    }

    private void Exit(ButtonEventArgs args)
    {
        Close();
    }
}
