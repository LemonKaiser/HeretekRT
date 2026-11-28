using Content.Client._WH40K.Visuals.SpecialScreenAnimation.Overlays;
using Content.Shared._WH40K.Visuals.SpecialScreenAnimation;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.Visuals.SpecialScreenAnimation;

public sealed partial class SpecialScreenAnimationSystem : SharedSpecialScreenAnimationSystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    private SpecialScreenAnimationOverlay _overlay = default!;

    public override void Initialize()
    {
        _overlay = new SpecialScreenAnimationOverlay();
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<PlaySpecialScreenAnimationEvent>(OnPlayAnimation);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_overlay);
    }

    private void OnPlayAnimation(PlaySpecialScreenAnimationEvent ev)
    {
        _overlay.Enqueue(ev.Animation);
    }
}
