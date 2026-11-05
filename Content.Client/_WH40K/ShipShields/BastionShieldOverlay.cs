using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WH40K.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.ShipShields;

public sealed class BastionShieldOverlay : Overlay
{
    private const float Extent = 1f;
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KBastionShield";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SharedTransformSystem _transform;
    private readonly FixtureSystem _fixtures;
    private readonly BastionShieldSystem _shields;
    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = new();
    private readonly List<Source> _sources = new();
    private readonly DrawVertexUV2D[] _vertices = new DrawVertexUV2D[6];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;
    public override bool RequestScreenTexture => true;

    public BastionShieldOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        _fixtures = _entities.System<FixtureSystem>();
        _shields = _entities.System<BastionShieldSystem>();
        ZIndex = 8;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _sources.Clear();
        if (args.MapId == MapId.Nullspace)
            return false;

        var query = _entities.EntityQueryEnumerator<BastionShieldComponent, ShipShieldVisualsComponent, FixturesComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visual, out var shieldVisuals, out var fixtures, out var xform))
        {
            if (xform.MapID != args.MapId ||
                _fixtures.GetFixtureOrNull(uid, "shield", fixtures) is not { Shape: ChainShape chain } ||
                chain.Count < 3)
                continue;

            var min = chain.Vertices[0];
            var max = min;
            for (var i = 1; i < chain.Count; i++)
            {
                min = Vector2.Min(min, chain.Vertices[i]);
                max = Vector2.Max(max, chain.Vertices[i]);
            }

            var center = (min + max) * 0.5f;
            var radii = (max - min) * 0.5f;
            if (radii.X <= 0f || radii.Y <= 0f)
                continue;

            var bounds = new Box2(center - radii * Extent, center + radii * Extent);
            var matrix = _transform.GetWorldMatrix(uid);
            if (!matrix.TransformBox(bounds).Intersects(args.WorldAABB))
                continue;

            _sources.Add(new Source(uid, bounds, center, radii, matrix, chain.Count, visual.Load, shieldVisuals.ShieldColor));
        }

        // An empty/off-map shield pass must not copy the viewport's screen texture.
        return _sources.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        foreach (var source in _sources)
        {
            if (!_shaders.TryGetValue(source.Uid, out var shader))
            {
                shader = _prototypes.Index(Shader).InstanceUnique();
                _shaders.Add(source.Uid, shader);
            }

            shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            shader.SetParameter("FieldTime", (float) (_timing.CurTime.TotalSeconds % 6.4));
            shader.SetParameter("Segments", (float) source.Segments);
            shader.SetParameter("Load", source.Load);
            shader.SetParameter("ShieldColor", source.ShieldColor);
            shader.SetParameter("Impact0", _shields.GetImpact(source.Uid, 0, source.Center, source.Radii));
            shader.SetParameter("Impact1", _shields.GetImpact(source.Uid, 1, source.Center, source.Radii));
            shader.SetParameter("Impact2", _shields.GetImpact(source.Uid, 2, source.Center, source.Radii));
            shader.SetParameter("Impact3", _shields.GetImpact(source.Uid, 3, source.Center, source.Radii));

            var bounds = source.Bounds;
            _vertices[0] = new DrawVertexUV2D(bounds.BottomLeft, new Vector2(-Extent, -Extent));
            _vertices[1] = new DrawVertexUV2D(bounds.BottomRight, new Vector2(Extent, -Extent));
            _vertices[2] = new DrawVertexUV2D(bounds.TopLeft, new Vector2(-Extent, Extent));
            _vertices[3] = _vertices[2];
            _vertices[4] = _vertices[1];
            _vertices[5] = new DrawVertexUV2D(bounds.TopRight, new Vector2(Extent, Extent));
            handle.SetTransform(source.Matrix);
            handle.UseShader(shader);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _vertices, Color.White);
        }
        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }

    public void RemoveShield(EntityUid uid)
    {
        if (_shaders.Remove(uid, out var shader))
            shader.Dispose();
    }

    protected override void DisposeBehavior()
    {
        foreach (var shader in _shaders.Values)
            shader.Dispose();
        _shaders.Clear();
        base.DisposeBehavior();
    }

    private readonly record struct Source(EntityUid Uid, Box2 Bounds, Vector2 Center, Vector2 Radii,
        Matrix3x2 Matrix, int Segments, float Load, Color ShieldColor);
}
