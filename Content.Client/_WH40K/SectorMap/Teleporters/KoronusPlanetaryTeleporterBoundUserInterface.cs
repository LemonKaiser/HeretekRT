using Content.Shared._WH40K.SectorMap.Teleporters;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client._WH40K.SectorMap.Teleporters;

[UsedImplicitly]
public sealed class KoronusPlanetaryTeleporterBoundUserInterface : BoundUserInterface
{
    private KoronusPlanetaryTeleporterWindow? _window;

    public KoronusPlanetaryTeleporterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new KoronusPlanetaryTeleporterWindow();
        _window.OnClose += Close;
        _window.OnTargetSelected += id => SendMessage(new KoronusPlanetaryTeleporterSelectMessage(id));
        _window.OnAccessChanged += publicAccess =>
            SendMessage(new KoronusPlanetaryTeleporterAccessMessage(publicAccess));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is KoronusPlanetaryTeleporterState teleporterState)
            _window?.UpdateState(teleporterState);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is KoronusPlanetaryTeleporterTargetsMessage targets)
            _window?.SetTargets(targets.Targets);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
