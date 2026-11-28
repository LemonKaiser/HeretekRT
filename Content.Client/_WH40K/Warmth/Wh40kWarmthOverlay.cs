using Content.Shared._WH40K.Warmth;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Warmth;

/// <summary>
/// Fullscreen edge frost and heat shimmer overlay driven by the local player's thermal strain.
/// </summary>
public sealed partial class Wh40kWarmthOverlay : Overlay
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private const float BaseStrength = 0.35f;
    private const float FadeRate = 0.25f;

    private static readonly ProtoId<ShaderPrototype> WarmthShader = "Wh40kWarmthScreen";

    private readonly ShaderInstance _shader;
    private float _frost;
    private float _heat;

    public Wh40kWarmthOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(WarmthShader).InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var frost = 0f;
        var heat = 0f;
        if (_cfg.GetCVar(Wh40kWarmthCVars.OverlayEnabled)
            && _entMan.TryGetComponent(_player.LocalEntity, out Wh40kWarmthComponent? warmth))
        {
            var strength = BaseStrength + (1f - BaseStrength) * warmth.Severity;
            if (warmth.Strain == Wh40kWarmthStrain.Cold)
                frost = strength;
            else if (warmth.Strain == Wh40kWarmthStrain.Hot)
                heat = strength;
        }

        var step = FadeRate * args.DeltaSeconds;
        _frost += Math.Clamp(frost - _frost, -step, step);
        _heat += Math.Clamp(heat - _heat, -step, step);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) || args.Viewport.Eye != eye.Eye)
            return false;

        return _frost > 0.001f || _heat > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("frost", _frost);
        _shader.SetParameter("heat", _heat);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
