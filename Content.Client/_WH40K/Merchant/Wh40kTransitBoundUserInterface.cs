using Content.Client._WH40K.SectorMap.UI;
using Content.Shared._WH40K.Merchant;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WH40K.Merchant;

public sealed class Wh40kTransitBoundUserInterface(EntityUid owner, Enum key) : BoundUserInterface(owner, key)
{
    private DefaultWindow? _window;
    private KoronusSectorMapControl? _map;
    public Wh40kTransitState? Snapshot { get; private set; }
    protected override void Open()
    {
        base.Open();
        _window = new DefaultWindow { Title = Loc.GetString("wh40k-service-transit-title"), MinSize = new(800, 580) };
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        column.AddChild(new Label { Text = Loc.GetString("wh40k-service-transit-info") });
        _map = new KoronusSectorMapControl();
        column.AddChild(_map);
        var refresh = new Button { Text = Loc.GetString("wh40k-merchant-refresh-button") };
        refresh.OnPressed += _ => SendMessage(new Wh40kTransitRefresh());
        column.AddChild(refresh);
        _window.Contents.AddChild(column);
        _window.OnClose += Close;
        _window.OpenCentered();
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    { base.UpdateState(state); if (state is Wh40kTransitState transit) { Snapshot = transit; _map?.UpdateState(transit.Sector); } }
    protected override void Dispose(bool disposing)
    { base.Dispose(disposing); if (disposing) _window?.Dispose(); }
}
