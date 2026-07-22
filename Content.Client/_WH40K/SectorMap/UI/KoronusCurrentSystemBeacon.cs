using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.SectorMap.UI;

public sealed class KoronusCurrentSystemBeacon
{
    private readonly List<Vector2> _vertices = new(6);

    public void Draw(
        DrawingHandleScreen handle,
        Vector2 position,
        float radius,
        Color color,
        float pulseTime,
        float compassTime,
        float uiScale,
        float scaleFactor = 1f)
    {
        var unit = uiScale * scaleFactor;
        var pulse = (MathF.Sin(pulseTime * 2.7f) + 1f) * 0.5f;
        var frameRadius = radius + 11f * unit;
        var compassRadius = (frameRadius + 4f * unit) * 1.5f;
        var signalRadius = compassRadius + (2f + pulse * 4f) * unit;
        var compassAngle = GetCompassHeading(compassTime);
        var compassDirection = new Vector2(MathF.Cos(compassAngle), MathF.Sin(compassAngle));

        handle.DrawCircle(position, signalRadius, color.WithAlpha((1f - pulse) * 0.16f), filled: false);
        handle.DrawCircle(position, frameRadius + 4f * unit, color.WithAlpha(0.12f + pulse * 0.08f));
        handle.DrawCircle(position, frameRadius + 2.2f * unit, Color.Black.WithAlpha(0.94f));
        DrawBeaconWings(handle, position, radius + 2f * unit, frameRadius + 4f * unit, 3f * unit,
            color.WithAlpha(0.82f));
        handle.DrawCircle(position, frameRadius, color.WithAlpha(0.96f), filled: false);
        DrawBeaconTicks(handle, position, frameRadius, unit, color.WithAlpha(0.80f));

        DrawCompassNeedle(handle, position, compassDirection, compassRadius, unit, color);

        handle.DrawCircle(position, radius + 2.4f * unit, Color.Black.WithAlpha(0.96f));
        handle.DrawCircle(position, radius + 0.8f * unit, color.WithAlpha(0.96f), filled: false);
        handle.DrawCircle(position, radius - 1.05f * unit, Color.FromHex("#191605"));
        handle.DrawCircle(position, MathF.Max(1.55f * unit, radius * 0.38f), Color.InterpolateBetween(color, Color.White, 0.74f));
    }

    private void DrawBeaconWings(
        DrawingHandleScreen handle,
        Vector2 position,
        float innerRadius,
        float outerRadius,
        float halfWidth,
        Color color)
    {
        for (var i = 0; i < 4; i++)
        {
            var angle = MathF.PI / 4f + i * MathF.PI / 2f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var normal = new Vector2(-direction.Y, direction.X);
            var basePoint = position + direction * innerRadius;

            _vertices.Clear();
            _vertices.Add(basePoint + normal * halfWidth);
            _vertices.Add(position + direction * outerRadius);
            _vertices.Add(basePoint - normal * halfWidth);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _vertices, color);
        }
    }

    private void DrawBeaconTicks(DrawingHandleScreen handle, Vector2 position, float radius, float unit, Color color)
    {
        for (var i = 0; i < 4; i++)
        {
            var angle = i * MathF.PI / 2f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            DrawThickSegment(
                handle,
                position + direction * (radius - 2f * unit),
                position + direction * (radius + 2.5f * unit),
                1.15f * unit,
                color);
        }
    }

    private void DrawCompassNeedle(
        DrawingHandleScreen handle,
        Vector2 position,
        Vector2 direction,
        float radius,
        float unit,
        Color color)
    {
        var normal = new Vector2(-direction.Y, direction.X);
        var needleLength = radius - 5f * unit;
        DrawCompassNeedleHead(handle, position, direction, normal, needleLength, 3.75f * unit, Color.Black.WithAlpha(0.94f), unit);
        DrawCompassNeedleHead(handle, position, -direction, -normal, needleLength, 3.2f * unit, Color.Black.WithAlpha(0.94f), unit);
        DrawCompassNeedleHead(handle, position, direction, normal, needleLength, 2.48f * unit,
            Color.InterpolateBetween(color, Color.White, 0.54f), unit);
        DrawCompassNeedleHead(handle, position, -direction, -normal, needleLength, 2.03f * unit,
            color.WithAlpha(0.72f), unit);
    }

    private void DrawCompassNeedleHead(
        DrawingHandleScreen handle,
        Vector2 position,
        Vector2 direction,
        Vector2 normal,
        float length,
        float halfWidth,
        Color color,
        float unit)
    {
        var baseCenter = position + direction * (1.5f * unit);

        _vertices.Clear();
        _vertices.Add(baseCenter + normal * halfWidth);
        _vertices.Add(position + direction * length);
        _vertices.Add(baseCenter - normal * halfWidth);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _vertices, color);
    }

    private void DrawThickSegment(DrawingHandleScreen handle, Vector2 start, Vector2 end, float width, Color color)
    {
        var direction = end - start;
        var length = direction.Length();
        if (length < 0.001f)
            return;

        var normal = new Vector2(-direction.Y, direction.X) / length * (width * 0.5f);
        _vertices.Clear();
        _vertices.Add(start + normal);
        _vertices.Add(end + normal);
        _vertices.Add(end - normal);
        _vertices.Add(start + normal);
        _vertices.Add(end - normal);
        _vertices.Add(start - normal);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _vertices, color);
    }

    private static float GetCompassHeading(float animationTime)
    {
        const float headingInterval = 5f;
        const float turnDuration = 1.25f;
        var currentInterval = (int) MathF.Floor(animationTime / headingInterval);
        var intervalProgress = animationTime % headingInterval;
        var turnProgress = Math.Clamp(intervalProgress / turnDuration, 0f, 1f);
        turnProgress = turnProgress * turnProgress * (3f - 2f * turnProgress);
        return LerpAngle(GetCompassHeadingForInterval(currentInterval - 1),
            GetCompassHeadingForInterval(currentInterval), turnProgress);
    }

    private static float GetCompassHeadingForInterval(int interval)
    {
        uint value = unchecked((uint) interval) * 747796405u + 2891336453u;
        value = (value >> ((int) (value >> 28) + 4)) ^ value;
        value *= 277803737u;
        value = (value >> 22) ^ value;
        return (value & 0xFFFF) / 65535f * MathF.PI * 2f;
    }

    private static float LerpAngle(float from, float to, float amount)
    {
        var delta = (to - from + MathF.PI) % (MathF.PI * 2f);
        if (delta < 0f)
            delta += MathF.PI * 2f;

        return from + (delta - MathF.PI) * amount;
    }
}
