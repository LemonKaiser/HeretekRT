using Content.Shared._WH40K.Administration;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.Administration;

public sealed partial class UndeadSystem : SharedUndeadSystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    private UndeadEyeOverlay _eyes = default!;
    private UndeadEyeBehindOverlay _eyesBehind = default!;

    public override void Initialize()
    {
        base.Initialize();
        _eyes = new UndeadEyeOverlay();
        _eyesBehind = new UndeadEyeBehindOverlay();
        _overlays.AddOverlay(_eyes);
        _overlays.AddOverlay(_eyesBehind);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_eyes);
        _overlays.RemoveOverlay(_eyesBehind);
        _eyes.Dispose();
        _eyesBehind.Dispose();
        base.Shutdown();
    }
}
