using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.Bosses;

public static class Wh40kBossRadarDrawing
{
    // Local +X is the head, matching the organism's fixtures and bite direction.
    private static readonly Vector2[] Carapace =
    {
        new(-0.95f, 0), new(-0.6f, -0.48f), new(-0.12f, -0.62f), new(0.38f, -0.48f),
        new(0.65f, -0.22f), new(0.8f, 0), new(0.65f, 0.22f), new(0.38f, 0.48f),
        new(-0.12f, 0.62f), new(-0.6f, 0.48f), new(-0.95f, 0),
    };

    private static readonly Vector2[] Mandible =
    {
        new(-0.22f, 0.58f), new(0.1f, 0.92f), new(0.62f, 1), new(1, 0.55f), new(0.72f, 0.46f),
    };

    public static void Draw(DrawingHandleScreen handle, Vector2 position, Box2Rotated bounds, Color color, float scale)
    {
        var halfWidth = Math.Clamp(bounds.Box.Width * 0.5f, 14f * scale, 64f * scale);
        var halfHeight = Math.Clamp(bounds.Box.Height * 0.5f, 8f * scale, 32f * scale);
        var matrix = Matrix3x2.CreateScale(halfWidth, halfHeight) *
                     Matrix3x2.CreateRotation((float) bounds.Rotation.Theta) *
                     Matrix3x2.CreateTranslation(position);
        var body = new Vector2[Carapace.Length];
        for (var i = 0; i < body.Length; i++)
            body[i] = Vector2.Transform(Carapace[i], matrix);

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, body, color.WithAlpha(0.22f));
        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, body, color.WithAlpha(1f));
        var claw = new Vector2[Mandible.Length];
        for (var side = -1; side <= 1; side += 2)
        {
            for (var i = 0; i < claw.Length; i++)
                claw[i] = Vector2.Transform(Mandible[i] * new Vector2(1, side), matrix);
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, claw, color.WithAlpha(1f));
        }

        handle.DrawLine(Vector2.Transform(new Vector2(-0.65f, 0), matrix),
            Vector2.Transform(new Vector2(0.5f, 0), matrix), color.WithAlpha(0.65f));
        for (var i = 0; i < 3; i++)
        {
            var x = -0.45f + i * 0.28f;
            handle.DrawLine(Vector2.Transform(new Vector2(x, -0.3f), matrix),
                Vector2.Transform(new Vector2(x + 0.15f, 0), matrix), color.WithAlpha(0.7f));
            handle.DrawLine(Vector2.Transform(new Vector2(x, 0.3f), matrix),
                Vector2.Transform(new Vector2(x + 0.15f, 0), matrix), color.WithAlpha(0.7f));
        }
        handle.DrawCircle(Vector2.Transform(new Vector2(0.48f, 0), matrix), 2f * scale, Color.Orange);
    }
}
