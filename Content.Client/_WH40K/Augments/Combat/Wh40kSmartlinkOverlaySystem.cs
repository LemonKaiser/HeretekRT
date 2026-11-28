using Robust.Client.Graphics;

namespace Content.Client._WH40K.Augments.Combat;

public sealed partial class Wh40kSmartlinkOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new Wh40kSmartlinkOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<Wh40kSmartlinkOverlay>();
    }
}
