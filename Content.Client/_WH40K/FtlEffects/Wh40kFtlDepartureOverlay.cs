using System.Numerics;
using Content.Shared._WH40K.FtlEffects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.FtlEffects;

/// <summary>Draws a deep refractive cone with sparks flowing from its nose toward midship.</summary>
public sealed partial class Wh40kFtlDepartureOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "Wh40kFtlDeparture";
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Wh40kFtlDepartureSystem _system;
    private readonly SharedTransformSystem _transforms;
    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = new();
    private readonly List<EntityUid> _expired = new();
    private readonly List<(EntityUid Uid, Box2 Bounds, float Intensity, float Phase, float Burst, Matrix3x2 Matrix)> _visible = new();
    private readonly DrawVertexUV2D[] _quad = new DrawVertexUV2D[6];

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public Wh40kFtlDepartureOverlay(Wh40kFtlDepartureSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _transforms = _entities.System<SharedTransformSystem>();
        ZIndex = 104;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _visible.Clear();
        _expired.Clear();
        foreach (var uid in _shaders.Keys)
        {
            if (!_entities.HasComponent<Wh40kFtlDepartureComponent>(uid) || _entities.Deleted(uid))
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
        {
            _shaders[uid].Dispose();
            _shaders.Remove(uid);
        }

        if (args.Viewport.Eye == null)
            return false;

        var now = _system.Now;
        var query = _entities.EntityQueryEnumerator<Wh40kFtlDepartureComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var effect, out var grid, out var xform))
        {
            if (xform.MapID != args.MapId || grid.LocalAABB.Width <= 0f || grid.LocalAABB.Height <= 0f)
                continue;
            var intensity = Wh40kFtlDepartureTiming.Intensity(now, effect.Started, effect.Departure, effect.Entered);
            if (intensity <= 0f || effect.Lead != null || _system.Gone(uid, effect))
                continue;
            var bounds = Wh40kFtlConeGeometry.Bounds(grid.LocalAABB);
            var matrix = Wh40kFtlConeGeometry.MotionTransform(grid.LocalAABB, _system.Motion(uid, effect))
                * _transforms.GetWorldMatrix(xform);
            if (!args.WorldAABB.Intersects(matrix.TransformBox(bounds)))
                continue;

            if (!_shaders.ContainsKey(uid))
                _shaders.Add(uid, _prototypes.Index(Shader).Instance().Duplicate());
            var phase = (float) (now - effect.Started).TotalSeconds;
            var distance = MathF.Abs((float) (now - effect.Departure).TotalSeconds);
            var burst = Math.Clamp(1f - distance / 0.18f, 0f, 1f);
            _visible.Add((uid, bounds, intensity, phase, burst * burst, matrix));
        }

        return _visible.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        foreach (var (uid, bounds, intensity, phase, burst, matrix) in _visible)
        {
            var shader = _shaders[uid];
            var origin = args.Viewport.WorldToLocal(Vector2.Transform(Vector2.Zero, matrix));
            var forward = args.Viewport.WorldToLocal(Vector2.Transform(Vector2.UnitY, matrix)) - origin;
            var right = args.Viewport.WorldToLocal(Vector2.Transform(Vector2.UnitX, matrix)) - origin;
            forward.Y = -forward.Y;
            right.Y = -right.Y;
            shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            shader.SetParameter("forwardPixels", forward);
            shader.SetParameter("rightPixels", right);
            shader.SetParameter("intensity", intensity);
            shader.SetParameter("phase", phase);
            shader.SetParameter("burst", burst);
            handle.SetTransform(matrix);
            handle.UseShader(shader);
            _quad[0] = new DrawVertexUV2D(bounds.BottomLeft, new Vector2(-1f, 0f));
            _quad[1] = new DrawVertexUV2D(bounds.BottomRight, new Vector2(1f, 0f));
            _quad[2] = new DrawVertexUV2D(bounds.TopRight, new Vector2(1f, 1f));
            _quad[3] = _quad[0];
            _quad[4] = _quad[2];
            _quad[5] = new DrawVertexUV2D(bounds.TopLeft, new Vector2(-1f, 1f));
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _quad.AsSpan());
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }

    protected override void DisposeBehavior()
    {
        foreach (var shader in _shaders.Values)
            shader.Dispose();
        _shaders.Clear();
        base.DisposeBehavior();
    }
}
