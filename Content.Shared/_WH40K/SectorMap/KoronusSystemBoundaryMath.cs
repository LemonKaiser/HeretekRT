using System;
using System.Numerics;

namespace Content.Shared._WH40K.SectorMap;

/// <summary>
/// Shared geometry for the outer edge of a Koronus system.
/// Keeping the warning threshold and circle vertices here makes the world overlay and every NAV
/// view describe precisely the same boundary.
/// </summary>
public static class KoronusSystemBoundaryMath
{
    public const int OutlineSegments = 1024;
    public const int OutlineVertexCount = OutlineSegments + 1;
    public const int DangerBandVertexCount = OutlineVertexCount * 2;
    private static readonly Vector2[] OutlineDirections = CreateOutlineDirections();

    public static float GetWarningRadius(float radius, float warningFraction)
    {
        return MathF.Max(0f, radius) * Math.Clamp(warningFraction, 0f, 1f);
    }

    public static bool IsInWarningArea(Vector2 position, Vector2 origin, float radius, float warningFraction)
    {
        var warningRadius = GetWarningRadius(radius, warningFraction);
        return warningRadius > 0f && Vector2.DistanceSquared(position, origin) >= warningRadius * warningRadius;
    }

    /// <summary>
    /// Builds a closed, fixed-detail circle. A fixed vertex count avoids deriving render work from
    /// the system radius: 20 km systems must not produce hundreds of thousands of line segments.
    /// </summary>
    public static bool TryBuildOutlineVertices(Vector2 origin, float radius, Span<Vector2> vertices)
    {
        if (radius <= 0f || vertices.Length != OutlineVertexCount)
            return false;

        for (var i = 0; i < OutlineVertexCount; i++)
            vertices[i] = origin + OutlineDirections[i] * radius;

        return true;
    }

    /// <summary>
    /// Builds the paired vertices for one triangle strip between the system edge and the visible
    /// outer radius. The fixed buffer keeps the red danger area independent of world size.
    /// </summary>
    public static bool TryBuildDangerBandVertices(
        Vector2 origin,
        float innerRadius,
        float outerRadius,
        Span<Vector2> vertices)
    {
        if (innerRadius < 0f ||
            outerRadius <= innerRadius ||
            vertices.Length != DangerBandVertexCount)
        {
            return false;
        }

        for (var i = 0; i < OutlineVertexCount; i++)
        {
            var direction = OutlineDirections[i];
            var vertex = i * 2;
            vertices[vertex] = origin + direction * innerRadius;
            vertices[vertex + 1] = origin + direction * outerRadius;
        }

        return true;
    }

    private static Vector2[] CreateOutlineDirections()
    {
        var directions = new Vector2[OutlineVertexCount];
        for (var i = 0; i < OutlineVertexCount; i++)
        {
            var angle = MathF.Tau * i / OutlineSegments;
            directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        return directions;
    }
}
