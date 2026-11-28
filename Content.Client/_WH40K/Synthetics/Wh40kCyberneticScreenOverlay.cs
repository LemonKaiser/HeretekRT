using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Synthetics;

/// <summary>
/// World-space shader pass beneath <see cref="Wh40kCyberneticHudOverlay"/>: red phosphor vignette rim,
/// horizontal glitch slice tear under heavy strain, and desaturated blue-grey standby drain.
/// </summary>
public sealed partial class Wh40kCyberneticScreenOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "Wh40kCyberneticShader";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    public float Tint;
    public float Glitch;
    public float Slice;
    public float Standby;

    public Wh40kCyberneticScreenOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index(ShaderId).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return (Tint > 0.004f || Glitch > 0.004f || Standby > 0.004f) &&
               _entities.TryGetComponent<EyeComponent>(_player.LocalEntity, out var eye) &&
               args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("tint", Tint);
        _shader.SetParameter("glitch", Glitch);
        _shader.SetParameter("slice", Slice);
        _shader.SetParameter("standby", Standby);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
