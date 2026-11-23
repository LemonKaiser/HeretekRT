using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Explosion;

/// <summary>
/// Draws every live explosion shockwave as a screen space displacement ring.
/// </summary>
public sealed partial class Wh40kExplosionShockwaveOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShockwaveShaderProto = "WH40KExplosionShockwave";

    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Waves that can distort the screen at once. The shader's array lengths match this.</summary>
    public const int MaxCount = 4;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly Wh40kExplosionShockwaveSystem _system;
    private readonly ShaderInstance _shader;

    private readonly Vector2[] _positions = new Vector2[MaxCount];
    private readonly float[] _radii = new float[MaxCount];
    private readonly float[] _widths = new float[MaxCount];
    private readonly float[] _strengths = new float[MaxCount];
    private int _count;

    public Wh40kExplosionShockwaveOverlay(Wh40kExplosionShockwaveSystem system)
    {
        IoCManager.InjectDependencies(this);

        _system = system;
        _shader = _proto.Index<ShaderPrototype>(ShockwaveShaderProto).Instance().Duplicate();
        ZIndex = 102; // After the singularity's own distortion, which sits at 101.
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _count = 0;

        if (args.Viewport.Eye == null)
            return false;

        // Metres to fragment pixels.
        var scale = EyeManager.PixelsPerMeter * (args.Viewport.RenderScale * args.Viewport.Eye.Scale).X;

        if (scale <= 0f)
            return false;

        var bounds = args.WorldAABB;

        foreach (var wave in _system.Shockwaves)
        {
            if (wave.Position.MapId != args.MapId || wave.Strength <= 0f)
                continue;

            var centre = wave.Position.Position;

            // The band the shader really touches, allowing for the one pixel floor the width gets below.
            var width = MathF.Max(wave.Width, 1f / scale);

            // Slots are few and taken in order, so drop waves that cannot touch a visible pixel: ones still too
            // small to have reached the viewport, and ones that have already swept past its far corner.
            var nearest = (centre - bounds.ClosestPoint(centre)).Length();

            if (nearest > wave.Radius + width || FarthestDistance(centre, bounds) < wave.Radius - width)
                continue;

            // Inside-viewport pixels, so specifically not IViewportControl.WorldToScreen.
            var coords = args.Viewport.WorldToLocal(centre);
            coords.Y = args.Viewport.Size.Y - coords.Y; // Local space to fragment space.

            _positions[_count] = coords;
            _radii[_count] = wave.Radius * scale;
            _widths[_count] = width * scale;
            _strengths[_count] = wave.Strength * scale;
            _count++;

            if (_count == MaxCount)
                break;
        }

        return _count > 0;
    }

    /// <summary>Distance from a point to the furthest corner of a box.</summary>
    private static float FarthestDistance(Vector2 point, Box2 box)
    {
        var x = MathF.Max(MathF.Abs(point.X - box.Left), MathF.Abs(point.X - box.Right));
        var y = MathF.Max(MathF.Abs(point.Y - box.Bottom), MathF.Abs(point.Y - box.Top));

        return MathF.Sqrt(x * x + y * y);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || _count == 0)
            return;

        _shader.SetParameter("count", _count);
        _shader.SetParameter("position", _positions);
        _shader.SetParameter("radius", _radii);
        _shader.SetParameter("width", _widths);
        _shader.SetParameter("strength", _strengths);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldAABB, Color.White);
        handle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _shader.Dispose();
        base.DisposeBehavior();
    }
}
