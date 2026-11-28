using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Act1;

/// <summary>
/// Breaks the last frame of the white room before the map transfer. It deliberately remains black until the server
/// acknowledges the transfer with the fade-from-black cue, preventing a one-frame Footfall flash.
/// </summary>
public sealed partial class Act1PrologueShatterOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShatterShader = "HeretekAct1Shatter";

    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly ShaderInstance _shader;

    public float Fracture { get; set; }
    public float Blackout { get; set; }

    public Act1PrologueShatterOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index(ShatterShader).Instance().Duplicate();
    }

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("fracture", Fracture);
        _shader.SetParameter("blackout", Blackout);
        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldBounds, Color.White);
        args.WorldHandle.UseShader(null);
    }
}
