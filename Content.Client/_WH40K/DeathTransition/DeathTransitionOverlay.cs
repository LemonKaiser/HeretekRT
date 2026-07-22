using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.DeathTransition;

/// <summary>
/// Applies the world-space part of the death transition over the last frame of gameplay.
/// </summary>
public sealed class DeathTransitionOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly ShaderInstance _shader;

    public float Progress { get; set; }

    public DeathTransitionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index<ShaderPrototype>("HeretekDeathTransition").Instance().Duplicate();
    }

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public override bool RequestScreenTexture => true;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("progress", Progress);

        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldBounds, Color.White);
        args.WorldHandle.UseShader(null);
    }
}
