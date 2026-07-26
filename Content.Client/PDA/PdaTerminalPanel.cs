using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.PDA;

/// <summary>
/// Local panels for cartridge screens. They let GadOS programs use their own styling
/// without changing shared styles in other game interfaces.
/// </summary>
public sealed class PdaTerminalPanel : PanelContainer
{
    public PdaTerminalPanel()
    {
        PanelOverride = PdaTerminalPalette.CreatePanel(
            PdaTerminalPalette.ScreenPanel,
            PdaTerminalPalette.Rail,
            new Thickness(1));
    }
}

public sealed class PdaTerminalHeaderPanel : PanelContainer
{
    public PdaTerminalHeaderPanel()
    {
        PanelOverride = PdaTerminalPalette.CreatePanel(
            PdaTerminalPalette.RaisedPanel,
            PdaTerminalPalette.AccentMuted,
            new Thickness(0, 0, 0, 1));
    }
}

/// <summary>
/// Lower display utility rail. Its lower bevels align with the dataslate opening,
/// so the decorative backing reaches the casing instead of ending as a rectangle.
/// </summary>
public sealed class PdaTerminalFooterRail : PanelContainer
{
    public PdaTerminalFooterRail()
    {
        PanelOverride = new PdaTerminalFooterRailStyleBox();
    }
}

internal sealed class PdaTerminalFooterRailStyleBox : StyleBox
{
    private static readonly Color Background = Color.FromHex("#171B19");
    private static readonly Color Stripe = Color.FromHex("#090C0AE0");
    private static readonly Color TopEdge = Color.FromHex("#667168");

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        var cornerCut = MathF.Min(26f * uiScale, box.Height);
        var rail = CreateRail(box, cornerCut);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, rail, Color.ToSrgb(Background));

        var stripeWidth = 10f * uiScale;
        var stripeSpacing = 28f * uiScale;
        var stripeShift = box.Height * 0.82f;

        for (var x = box.Left - box.Height; x < box.Right + box.Height; x += stripeSpacing)
        {
            var stripe = new[]
            {
                new Vector2(x, box.Top),
                new Vector2(x + stripeWidth, box.Top),
                new Vector2(x + stripeShift + stripeWidth, box.Bottom),
                new Vector2(x + stripeShift, box.Bottom)
            };

            var clipped = ClipToRail(stripe, box, cornerCut);
            if (clipped.Length >= 3)
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, clipped, Color.ToSrgb(Stripe));
        }

        handle.DrawLine(new Vector2(box.Left, box.Top), new Vector2(box.Right, box.Top), Color.ToSrgb(TopEdge));
    }

    protected override float GetDefaultContentMargin(Margin margin)
    {
        return 0f;
    }

    private static Vector2[] CreateRail(UIBox2 box, float cornerCut)
    {
        return
        [
            new Vector2(box.Left, box.Top),
            new Vector2(box.Right, box.Top),
            new Vector2(box.Right - cornerCut, box.Bottom),
            new Vector2(box.Left + cornerCut, box.Bottom)
        ];
    }

    private static Vector2[] ClipToRail(Vector2[] polygon, UIBox2 box, float cornerCut)
    {
        var height = box.Height;
        var slope = cornerCut / height;

        polygon = Clip(polygon, point => point.Y - box.Top);
        polygon = Clip(polygon, point => box.Bottom - point.Y);
        polygon = Clip(polygon, point => point.X - box.Left - (point.Y - box.Top) * slope);
        return Clip(polygon, point => box.Right - point.X - (point.Y - box.Top) * slope);
    }

    private static Vector2[] Clip(Vector2[] polygon, Func<Vector2, float> signedDistance)
    {
        if (polygon.Length == 0)
            return [];

        var result = new List<Vector2>(polygon.Length + 2);
        var previous = polygon[^1];
        var previousDistance = signedDistance(previous);

        foreach (var current in polygon)
        {
            var currentDistance = signedDistance(current);
            var previousInside = previousDistance >= 0f;
            var currentInside = currentDistance >= 0f;

            if (previousInside != currentInside)
            {
                var progress = previousDistance / (previousDistance - currentDistance);
                result.Add(Vector2.Lerp(previous, current, progress));
            }

            if (currentInside)
                result.Add(current);

            previous = current;
            previousDistance = currentDistance;
        }

        return [.. result];
    }
}
