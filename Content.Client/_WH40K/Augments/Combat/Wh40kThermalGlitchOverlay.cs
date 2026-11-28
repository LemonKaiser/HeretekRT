using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Augments.Combat;

/// <summary>
/// Brief quarter-second static tear glitch when cycling Tapetum ocular bands.
/// </summary>
public sealed partial class Wh40kThermalGlitchOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const float Duration = 0.25f;
    private const int Bands = 14;
    private const int ScanlineGap = 3;

    private TimeSpan? _start;
    private Color _color;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public Wh40kThermalGlitchOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 6;
    }

    public void Start(Color color)
    {
        _start = _timing.RealTime;
        _color = color;
    }

    private float Progress => _start is { } start ? (float) (_timing.RealTime - start).TotalSeconds / Duration : 1f;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (Progress >= 1f)
        {
            _start = null;
            return false;
        }

        return _player.LocalEntity is { } player &&
               _entities.TryGetComponent<EyeComponent>(player, out var eye) &&
               eye.Eye != null &&
               args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var bounds = args.ViewportBounds;
        var fade = 1f - Progress;

        handle.DrawRect(bounds, _color.WithAlpha(0.3f * fade * fade));

        var scanline = Color.Black.WithAlpha(0.3f * fade);
        for (var y = bounds.Top; y < bounds.Bottom; y += ScanlineGap)
            handle.DrawRect(new UIBox2(bounds.Left, y, bounds.Right, y + 1), scanline);

        var height = bounds.Height;
        var width = bounds.Width;
        var bands = (int) MathF.Ceiling(Bands * fade);
        for (var i = 0; i < bands; i++)
        {
            var top = bounds.Top + _random.NextFloat() * height;
            var thick = 2f + _random.NextFloat() * height * 0.03f;
            var start = bounds.Left + _random.NextFloat(-0.2f, 0.6f) * width;
            var run = _random.NextFloat(0.2f, 0.9f) * width;
            var color = _random.Prob(0.6f)
                ? _color.WithAlpha(_random.NextFloat(0.35f, 0.7f) * fade)
                : Color.Black.WithAlpha(_random.NextFloat(0.5f, 0.8f) * fade);
            handle.DrawRect(new UIBox2(start, top, start + run, top + thick), color);

            for (var s = 0; s < 6; s++)
            {
                var x = bounds.Left + _random.NextFloat() * width;
                var y = top + _random.NextFloat() * thick;
                handle.DrawRect(new UIBox2(x, y, x + 2f, y + 2f), Color.White.WithAlpha(0.6f * fade));
            }
        }
    }
}
