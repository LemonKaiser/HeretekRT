using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.GroundCover;

/// <summary>
/// Puddle reflections, drawn after entities and before weather and FOV, so the puddles can mirror what stands above them.
/// Runs only for viewports whose ground pass kept its floor this frame.
/// </summary>
public sealed class Wh40kGroundReflectionOverlay : Overlay
{
    private readonly Wh40kGroundCoverOverlay _ground;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public override bool RequestScreenTexture => true;

    public Wh40kGroundReflectionOverlay(Wh40kGroundCoverOverlay ground)
    {
        _ground = ground;
        // Before the weather layers, so rain streaks are not mirrored.
        ZIndex = -100;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _ground.WantsReflections(args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture != null)
            _ground.DrawReflections(args, ScreenTexture);
    }
}
