using System.Numerics;
using Content.Shared._WH40K.FtlEffects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.FtlEffects;

/// <summary>Reprojects the rendered hull for a quarter-second launch or arrival without moving entities.</summary>
public sealed partial class Wh40kFtlMotionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> MotionShader = "Wh40kFtlMotion";
    private static readonly ProtoId<ShaderPrototype> BackgroundShader = "Wh40kFtlBackground";
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    private readonly Wh40kFtlDepartureSystem _system;
    private readonly Wh40kFtlMotionBackgroundOverlay _background;
    private readonly SharedTransformSystem _transforms;
    private readonly SharedMapSystem _maps;
    private readonly ShaderInstance _erase;
    private readonly Dictionary<EntityUid, Hull> _hulls = new();
    private readonly List<EntityUid> _expired = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public Wh40kFtlMotionOverlay(Wh40kFtlDepartureSystem system, Wh40kFtlMotionBackgroundOverlay background)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _background = background;
        _transforms = _entities.System<SharedTransformSystem>();
        _maps = _entities.System<SharedMapSystem>();
        _erase = _prototypes.Index(BackgroundShader).Instance().Duplicate();
        ZIndex = 103;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _expired.Clear();
        foreach (var uid in _hulls.Keys)
        {
            if (_entities.Deleted(uid) || !_entities.HasComponent<Wh40kFtlDepartureComponent>(uid))
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
        {
            _hulls[uid].Shader.Dispose();
            _hulls.Remove(uid);
        }

        return _background.ViewportId == args.Viewport.Id && _background.Background != null &&
            _system.HasMotion(args.MapId, args.WorldAABB);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || _background.Background == null)
            return;

        var handle = args.WorldHandle;
        _erase.SetParameter("background", _background.Background);
        var query = _entities.EntityQueryEnumerator<Wh40kFtlDepartureComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var effect, out var grid, out var xform))
        {
            var motion = _system.Motion(uid, effect);
            var gone = _system.Gone(uid, effect);
            if (xform.MapID != args.MapId || MathF.Abs(motion) < 0.0001f && !gone)
                continue;
            var matrix = _transforms.GetWorldMatrix(xform);
            var moved = _system.Moved(effect, grid, xform, matrix, motion);
            if (!args.WorldAABB.Intersects(matrix.TransformBox(grid.LocalAABB.Enlarged(0.5f))) &&
                !args.WorldAABB.Intersects(moved.TransformBox(grid.LocalAABB.Enlarged(0.5f))))
                continue;
            if (!_hulls.TryGetValue(uid, out var hull))
            {
                hull = new Hull(_prototypes.Index(MotionShader).Instance().Duplicate());
                _hulls.Add(uid, hull);
            }

            if (!hull.Built || hull.Tick != grid.LastTileModifiedTick)
                BuildHull(uid, grid, hull);

            handle.UseShader(_erase);
            handle.SetTransform(matrix);
            DrawHull(handle, hull.Vertices);

            if (gone)
                continue;

            var origin = args.Viewport.WorldToLocal(Vector2.Transform(Vector2.Zero, matrix));
            var right = args.Viewport.WorldToLocal(Vector2.Transform(Vector2.UnitX, matrix)) - origin;
            var forward = args.Viewport.WorldToLocal(Vector2.Transform(Vector2.UnitY, matrix)) - origin;
            origin.Y = args.Viewport.Size.Y - origin.Y;
            right.Y = -right.Y;
            forward.Y = -forward.Y;
            hull.Shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            hull.Shader.SetParameter("background", _background.Background);
            hull.Shader.SetParameter("originPixels", origin);
            hull.Shader.SetParameter("rightPixels", right);
            hull.Shader.SetParameter("forwardPixels", forward);
            handle.UseShader(hull.Shader);
            handle.SetTransform(moved);
            DrawHull(handle, hull.Vertices);
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }

    private static void DrawHull(DrawingHandleWorld handle, DrawVertexUV2D[] vertices)
    {
        for (var offset = 0; offset < vertices.Length; offset += 8190)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
                new ReadOnlySpan<DrawVertexUV2D>(vertices, offset, Math.Min(8190, vertices.Length - offset)));
    }

    private void BuildHull(EntityUid uid, MapGridComponent grid, Hull hull)
    {
        var vertices = new List<DrawVertexUV2D>();
        foreach (var tile in _maps.GetAllTiles(uid, grid))
        {
            var origin = (Vector2) tile.GridIndices * grid.TileSize;
            Add(Vector2.Zero);
            Add(Vector2.UnitX);
            Add(Vector2.One);
            Add(Vector2.Zero);
            Add(Vector2.One);
            Add(Vector2.UnitY);

            void Add(Vector2 corner)
            {
                var point = origin + corner * grid.TileSize + (corner - new Vector2(0.5f)) * 0.8f;
                vertices.Add(new DrawVertexUV2D(point, point));
            }
        }

        hull.Vertices = vertices.ToArray();
        hull.Tick = grid.LastTileModifiedTick;
        hull.Built = true;
    }

    protected override void DisposeBehavior()
    {
        _erase.Dispose();
        foreach (var hull in _hulls.Values)
            hull.Shader.Dispose();
        _hulls.Clear();
        base.DisposeBehavior();
    }

    private sealed class Hull(ShaderInstance shader)
    {
        public readonly ShaderInstance Shader = shader;
        public DrawVertexUV2D[] Vertices = Array.Empty<DrawVertexUV2D>();
        public GameTick Tick;
        public bool Built;
    }
}
