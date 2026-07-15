using Robust.Client.Graphics;

namespace Content.Client._WH40K.SectorMap;

public sealed class KoronusSystemBoundaryOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new KoronusSystemBoundaryOverlay());
        _overlays.AddOverlay(new KoronusPlanetSurfaceBoundaryOverlay());
        _overlays.AddOverlay(new KoronusSafetyZoneOverlay(IoCManager.Resolve<IEntityManager>()));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<KoronusSystemBoundaryOverlay>();
        _overlays.RemoveOverlay<KoronusPlanetSurfaceBoundaryOverlay>();
        _overlays.RemoveOverlay<KoronusSafetyZoneOverlay>();
    }
}
