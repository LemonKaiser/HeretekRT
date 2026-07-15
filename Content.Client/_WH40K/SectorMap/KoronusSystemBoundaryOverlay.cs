using System.Numerics;
using Content.Shared._WH40K.SectorMap.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// Draws the red danger band at the edge of an authored Koronus system.
/// </summary>
public sealed class KoronusSystemBoundaryOverlay : Overlay
{
    private const int BandSegments = 128;

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IMapManager _maps = default!;
    private static readonly Vector2[] BandDirections = CreateBandDirections();
    private readonly Vector2[] _dangerBandVertices = new Vector2[(BandSegments + 1) * 2];

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public KoronusSystemBoundaryOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var mapUid = _maps.GetMapEntityId(args.MapId);
        if (args.Viewport.Eye is not { } eye ||
            !_entities.TryGetComponent<KoronusSystemBoundaryComponent>(mapUid, out var boundary))
        {
            return false;
        }

        var warningRadius = boundary.Radius * boundary.WarningFraction;
        return Vector2.DistanceSquared(eye.Position.Position, boundary.Origin) >= warningRadius * warningRadius;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mapUid = _maps.GetMapEntityId(args.MapId);
        if (!_entities.TryGetComponent<KoronusSystemBoundaryComponent>(mapUid, out var boundary))
            return;

        var handle = args.WorldHandle;
        var outerRadius = MathF.Max(
                              MathF.Max(
                                  Vector2.Distance(boundary.Origin, args.WorldAABB.BottomLeft),
                                  Vector2.Distance(boundary.Origin, args.WorldAABB.BottomRight)),
                              MathF.Max(
                                  Vector2.Distance(boundary.Origin, args.WorldAABB.TopLeft),
                                  Vector2.Distance(boundary.Origin, args.WorldAABB.TopRight))) + 1f;

        DrawDangerBand(handle, boundary.Origin, boundary.Radius, outerRadius);
        handle.DrawCircle(boundary.Origin, boundary.Radius, Color.FromHex("#E02828").WithAlpha(0.96f), filled: false);
    }

    private void DrawDangerBand(DrawingHandleWorld handle, Vector2 origin, float innerRadius, float outerRadius)
    {
        if (innerRadius <= 0f || outerRadius <= innerRadius)
            return;

        for (var i = 0; i <= BandSegments; i++)
        {
            var direction = BandDirections[i];
            var vertex = i * 2;
            _dangerBandVertices[vertex] = origin + direction * innerRadius;
            _dangerBandVertices[vertex + 1] = origin + direction * outerRadius;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _dangerBandVertices, Color.FromHex("#4A0606").WithAlpha(0.78f));
    }

    private static Vector2[] CreateBandDirections()
    {
        var directions = new Vector2[BandSegments + 1];
        for (var i = 0; i <= BandSegments; i++)
        {
            var angle = MathF.Tau * i / BandSegments;
            directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        return directions;
    }
}
