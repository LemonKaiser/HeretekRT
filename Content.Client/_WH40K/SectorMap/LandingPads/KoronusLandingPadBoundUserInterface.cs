using Content.Shared._WH40K.SectorMap.LandingPads;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client._WH40K.SectorMap.LandingPads;

[UsedImplicitly]
public sealed class KoronusLandingPadBoundUserInterface : BoundUserInterface
{
    private KoronusLandingPadWindow? _window;

    public KoronusLandingPadBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new KoronusLandingPadWindow();
        _window.OnClose += Close;
        _window.OnSave += (name, parkingTime, publicAccess, enabled) =>
            SendMessage(new KoronusLandingPadConfigureMessage(name, parkingTime, publicAccess, enabled));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is KoronusLandingPadBoundUserInterfaceState padState)
            _window?.UpdateState(padState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
