using System.Numerics;
using Robust.Client.Graphics;
using Content.Client._WH40K.Visuals.WorldEffects;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._WH40K.Administration;

internal static class UndeadEyeGeometry
{
    public static EyeAnchor Resolve(
        RsiDirection direction,
        EyeFrameGeometry currentEyes,
        EyeFrameGeometry frontEyes,
        EyeFrameGeometry rightEyes,
        OpaqueGeometry head)
    {
        var farSide = direction is RsiDirection.North or RsiDirection.West
            or RsiDirection.NorthWest or RsiDirection.NorthEast;
        var trail = direction is RsiDirection.North or RsiDirection.East
            or RsiDirection.NorthEast or RsiDirection.SouthEast ? 1f : -1f;
        var scale = head.Visible ? Math.Clamp(head.Bounds.Height / 0.35f, 0.7f, 1.8f) : 1f;
        Vector2 point;

        // Facing the viewer, the character's anatomical right eye is on screen-left.
        if (direction is RsiDirection.South or RsiDirection.SouthWest && currentEyes.Visible)
        {
            point = currentEyes.LeftCenter;
        }
        else if (direction == RsiDirection.North && frontEyes.Visible)
        {
            point = frontEyes.LeftCenter * new Vector2(-1f, 1f);
        }
        else if (direction is RsiDirection.West or RsiDirection.NorthWest && rightEyes.Visible)
        {
            point = rightEyes.RightCenter * new Vector2(-1f, 1f);
        }
        else if (currentEyes.Visible)
        {
            point = currentEyes.RightCenter;
        }
        else if (frontEyes.Visible)
        {
            point = frontEyes.LeftCenter * new Vector2(trail < 0f ? 1f : -1f, 1f);
        }
        else if (head.Visible)
        {
            // Visors and eyeless species use their actual head silhouette instead of a human pixel offset.
            point = new Vector2(head.Bounds.Center.X + trail * head.Bounds.Width * 0.22f,
                head.Bounds.Bottom + head.Bounds.Height * 0.48f);
        }
        else
        {
            return default;
        }

        // Keep the plume visible from behind without painting a second burning eye on the far side of the head.
        return new EyeAnchor(true, point, scale, trail, farSide);
    }

    public static bool IsEyeMask(OpaqueGeometry frame, OpaqueGeometry head)
    {
        // Diona's "eyes" state contains lights down the entire body rather than anatomical eyes.
        return frame.Visible && (!head.Visible || frame.Bounds.Height <= head.Bounds.Height * 1.5f);
    }

    public static EyeFrameGeometry AnalyzeFrame(int width, int height, OpaqueGeometry frame, Func<int, int, bool> opaque)
    {
        if (!frame.Visible)
            return default;

        var pixels = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = opaque(x, y);
        }

        Vector2? FindCluster(int start, int end)
        {
            var pending = new Stack<int>();
            var count = 0;
            var sum = Vector2.Zero;
            for (var y = 0; y < height; y++)
            for (var x = start; x < end; x++)
            {
                if (!pixels[y * width + x])
                    continue;

                pending.Push(y * width + x);
                pixels[y * width + x] = false;
                while (pending.TryPop(out var index))
                {
                    var px = index % width;
                    var py = index / width;
                    sum += new Vector2(px + 0.5f, py + 0.5f);
                    count++;
                    Visit(px - 1, py);
                    Visit(px + 1, py);
                    Visit(px, py - 1);
                    Visit(px, py + 1);
                }

                var centre = sum / count;
                return new Vector2(centre.X - width / 2f, height / 2f - centre.Y) / EyeManager.PixelsPerMeter;
            }

            return null;

            void Visit(int x, int y)
            {
                if (x < start || x >= end || y < 0 || y >= height || !pixels[y * width + x])
                    return;
                pixels[y * width + x] = false;
                pending.Push(y * width + x);
            }
        }

        // A single central eye is one connected island, rather than two artificial half-eyes.
        for (var y = 0; width > 1 && y < height; y++)
        {
            if (pixels[y * width + width / 2 - 1] && pixels[y * width + width / 2])
            {
                var centre = FindCluster(0, width) ?? frame.Bounds.Center;
                return new EyeFrameGeometry(true, frame.Bounds, centre, centre);
            }
        }

        var left = FindCluster(0, width / 2);
        var right = FindCluster(width / 2, width);
        return left == null && right == null
            ? default
            : new EyeFrameGeometry(true, frame.Bounds, left ?? frame.Bounds.Center, right ?? frame.Bounds.Center);
    }
}

internal readonly record struct EyeFrameGeometry(bool Visible, Box2 Bounds, Vector2 LeftCenter, Vector2 RightCenter);

internal readonly record struct EyeAnchor(bool Visible, Vector2 Position, float Scale, float TrailDirection, bool BehindCharacter)
{
    public float CoreVisibility => BehindCharacter ? 0f : 1f;
}
