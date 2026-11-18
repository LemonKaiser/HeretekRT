using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Weapons;
using Content.Shared.CCVar;
using Content.Shared.Projectiles;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>Draws the highlighted pellets from each buckshot spread in one batch.</summary>
internal sealed class WH40KPelletTracerRenderer
{
    private const int MaxPellets = 128;
    private const int VerticesPerPellet = 12;
    private const float TrailSeconds = 0.006f;
    private const float MaxTrailLength = 0.19f;
    private const float TrailHalfWidth = 0.022f;
    private static readonly ProtoId<ShaderPrototype> Unshaded = "unshaded";
    private static readonly Color PelletHead = Color.FromHex("#FFF2C7");
    private static readonly Color PelletTrail = Color.FromHex("#FFE0AC").WithAlpha(0.80f);
    private static readonly ushort[] Indices = Enumerable.Range(0, MaxPellets * VerticesPerPellet)
        .Select(index => (ushort) index).ToArray();

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IConfigurationManager _config = IoCManager.Resolve<IConfigurationManager>();
    private readonly ShaderInstance _shader = IoCManager.Resolve<IPrototypeManager>().Index(Unshaded).Instance();
    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[MaxPellets * VerticesPerPellet];
    private int _vertexCount;

    public WH40KPelletTracerRenderer(IEntityManager entities, SharedTransformSystem transform)
    {
        _entities = entities;
        _transform = transform;
    }

    public void Draw(in OverlayDrawArgs args)
    {
        if (_config.GetCVar(CCVars.ParticleQuality) <= 0)
            return;

        _vertexCount = 0;
        var remaining = MaxPellets;
        var bounds = args.WorldAABB.Enlarged(MaxTrailLength);
        var query = _entities.EntityQueryEnumerator<ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (remaining > 0 && query.MoveNext(out _, out var projectile, out var body, out var xform))
        {
            if (projectile.PelletVisual != WH40KPelletVisual.Tracer ||
                projectile.ProjectileSpent || xform.MapID != args.MapId)
                continue;

            var head = _transform.GetWorldPosition(xform);
            var speed = body.LinearVelocity.Length();
            if (!bounds.Contains(head) || !float.IsFinite(speed) || speed < 0.1f)
                continue;

            var forward = _transform.GetWorldRotation(xform.ParentUid).RotateVec(body.LinearVelocity / speed);
            var side = new Vector2(-forward.Y, forward.X);
            var length = MathF.Min(MaxTrailLength, speed * TrailSeconds);
            if (projectile.ShotOrigin is { } origin && origin.MapId == xform.MapID)
                length = Math.Clamp(Vector2.Dot(head - origin.Position, forward), 0f, length);

            Ribbon(head, head - forward * length, side * TrailHalfWidth, PelletTrail);
            Ribbon(head + forward * 0.015f, head - forward * MathF.Min(length, 0.09f),
                side * (TrailHalfWidth * 0.67f), PelletHead);
            remaining--;
        }

        if (_vertexCount == 0)
            return;

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            Indices.AsSpan(0, _vertexCount), _vertices.AsSpan(0, _vertexCount));
        handle.UseShader(null);
    }

    private void Ribbon(Vector2 front, Vector2 back, Vector2 width, Color color)
    {
        var leading = width;
        var trailing = width * 0.12f;
        var clear = color.WithAlpha(0f);
        Triangle(front - leading, front + leading, back + trailing, color, color, clear);
        Triangle(front - leading, back + trailing, back - trailing, color, clear, clear);
    }

    private void Triangle(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc)
    {
        _vertices[_vertexCount++] = new(a, Vector2.Zero, ca);
        _vertices[_vertexCount++] = new(b, Vector2.Zero, cb);
        _vertices[_vertexCount++] = new(c, Vector2.Zero, cc);
    }
}
