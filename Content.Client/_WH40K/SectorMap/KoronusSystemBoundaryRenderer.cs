using System.Numerics;
using Content.Shared._WH40K.SectorMap;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// One visual representation of a Koronus system edge, shared by world space and both shuttle
/// navigation controls. The warning is a red band outside the permitted system circle.
/// </summary>
public static class KoronusSystemBoundaryRenderer
{
    public const int OutlineSegments = KoronusSystemBoundaryMath.OutlineSegments;
    public const int OutlineVertexCount = KoronusSystemBoundaryMath.OutlineVertexCount;
    public const int DangerBandVertexCount = KoronusSystemBoundaryMath.DangerBandVertexCount;

    public static readonly Color DangerBandColor = Color.FromHex("#4A0606").WithAlpha(0.78f);
    public static readonly Color OutlineColor = Color.FromHex("#E02828").WithAlpha(0.96f);

    public static bool DrawDangerBand(
        DrawingHandleBase handle,
        Vector2 center,
        float innerRadius,
        float outerRadius,
        Span<Vector2> vertices)
    {
        if (!KoronusSystemBoundaryMath.TryBuildDangerBandVertices(
                center,
                innerRadius,
                outerRadius,
                vertices))
        {
            return false;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, vertices, DangerBandColor);
        return true;
    }

    public static bool DrawOutline(
        DrawingHandleBase handle,
        Vector2 center,
        float radius,
        Span<Vector2> vertices)
    {
        if (!KoronusSystemBoundaryMath.TryBuildOutlineVertices(center, radius, vertices))
            return false;

        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, vertices, OutlineColor);
        return true;
    }
}
