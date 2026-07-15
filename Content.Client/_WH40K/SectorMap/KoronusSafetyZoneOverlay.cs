using System.Numerics;
using Content.Shared._WH40K.SectorMap.Components;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// Draws only dynamic station safe zones. Planetary profiles intentionally have no circle to draw.
/// </summary>
public sealed class KoronusSafetyZoneOverlay : Overlay
{
    private const int BandSegments = 128;
    private const float BandWidth = 1.5f;

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly Vector2[] _vertices = new Vector2[(BandSegments + 1) * 2];

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public KoronusSafetyZoneOverlay(IEntityManager entities)
    {
        _entities = entities;
        _transform = entities.System<SharedTransformSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var query = _entities.EntityQueryEnumerator<KoronusSafetyZoneComponent, TransformComponent>();
        while (query.MoveNext(out _, out var zone, out var transform))
        {
            if (zone.ShowBoundary && zone.Radius > 0f && transform.MapID == args.MapId)
                return true;
        }

        return false;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = _entities.EntityQueryEnumerator<KoronusSafetyZoneComponent, TransformComponent>();

        while (query.MoveNext(out _, out var zone, out var transform))
        {
            if (!zone.ShowBoundary || zone.Radius <= 0f || transform.MapID != args.MapId)
                continue;

            var origin = _transform.GetWorldPosition(transform);
            var innerRadius = MathF.Max(0f, zone.Radius - BandWidth);
            DrawBand(handle, origin, innerRadius, zone.Radius);
            handle.DrawCircle(origin, zone.Radius, Color.FromHex("#35D07F").WithAlpha(0.95f), filled: false);
        }
    }

    private void DrawBand(DrawingHandleWorld handle, Vector2 origin, float innerRadius, float outerRadius)
    {
        if (outerRadius <= innerRadius)
            return;

        for (var i = 0; i <= BandSegments; i++)
        {
            var angle = MathF.Tau * i / BandSegments;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var vertex = i * 2;
            _vertices[vertex] = origin + direction * innerRadius;
            _vertices[vertex + 1] = origin + direction * outerRadius;
        }

        handle.DrawPrimitives(
            DrawPrimitiveTopology.TriangleStrip,
            _vertices,
            Color.FromHex("#35D07F").WithAlpha(0.28f));
    }
}
