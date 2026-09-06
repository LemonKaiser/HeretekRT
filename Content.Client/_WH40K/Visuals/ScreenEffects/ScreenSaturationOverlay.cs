using System.Numerics;
using Content.Shared._WH40K.Visuals.ScreenEffects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Visuals.ScreenEffects;

public sealed partial class ScreenSaturationOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly ShaderInstance _shader;
    private float _currentSaturation = 1f;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ScreenSaturationOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index<ShaderPrototype>("HeretekScreenSaturation").Instance().Duplicate();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _players.LocalEntity is { } player &&
               _entities.HasComponent<ScreenSaturationComponent>(player) &&
               base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("saturation", _currentSaturation);
        args.WorldHandle.SetTransform(Matrix3x2.Identity);
        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldBounds, Color.White);
        args.WorldHandle.UseShader(null);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        if (_players.LocalEntity is not { } player ||
            !_entities.TryGetComponent(player, out ScreenSaturationComponent? component))
        {
            _currentSaturation = 1f;
            return;
        }

        var target = Math.Clamp(component.Saturation, 0f, 2f);
        var step = Math.Max(component.FadeRate, 0.001f) * args.DeltaSeconds;
        _currentSaturation = MathHelper.CloseTo(_currentSaturation, target, step)
            ? target
            : MathHelper.Lerp(_currentSaturation, target, Math.Clamp(step, 0f, 1f));
    }
}
