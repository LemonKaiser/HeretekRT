using System.Numerics;

namespace Content.Client._WH40K.Bosses;

internal static class Wh40kBossPointerGeometry
{
    public static UIBox2 VisibleViewport(UIBox2 control, Vector2 renderSize, Matrix3x2 localToScreen)
    {
        var a = Vector2.Transform(Vector2.Zero, localToScreen);
        var b = Vector2.Transform(renderSize, localToScreen);
        var min = Vector2.Max(control.TopLeft, Vector2.Min(a, b));
        var max = Vector2.Min(control.BottomRight, Vector2.Max(a, b));
        return new UIBox2(min, Vector2.Max(min, max));
    }

    public static UIBox2 Project(Box2Rotated bounds, Matrix3x2 worldToScreen)
    {
        var a = Vector2.Transform(bounds.BottomLeft, worldToScreen);
        var b = Vector2.Transform(bounds.BottomRight, worldToScreen);
        var c = Vector2.Transform(bounds.TopLeft, worldToScreen);
        var d = Vector2.Transform(bounds.TopRight, worldToScreen);
        return new UIBox2(Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)),
            Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)));
    }

    public static bool ShowPointer(UIBox2 viewport, UIBox2 boss) =>
        viewport.Right > viewport.Left && viewport.Bottom > viewport.Top && !viewport.Intersects(boss);
}
