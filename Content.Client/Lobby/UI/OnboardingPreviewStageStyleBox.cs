using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Vertical stage treatment for the onboarding preview panel.
/// </summary>
internal sealed class OnboardingPreviewStageStyleBox : StyleBox
{
    private const int GradientSteps = 20;
    private static readonly Color TopColor = Color.FromHex("#1C1E1778");
    private static readonly Color BottomColor = Color.FromHex("#080907C7");
    private static readonly Color BorderColor = Color.FromHex("#B697543B");

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        for (var step = 0; step < GradientSteps; step++)
        {
            var start = step / (float) GradientSteps;
            var end = (step + 1) / (float) GradientSteps;
            handle.DrawRect(
                UIBox2.FromDimensions(
                    new Vector2(box.Left, box.Top + box.Height * start),
                    new Vector2(box.Width, box.Height * (end - start))),
                Lerp(TopColor, BottomColor, (start + end) * 0.5f));
        }

        var borderThickness = MathF.Max(1f, uiScale);
        handle.DrawLine(new Vector2(box.Left, box.Top), new Vector2(box.Right, box.Top), BorderColor);
        handle.DrawLine(
            new Vector2(box.Left, box.Bottom - borderThickness),
            new Vector2(box.Right, box.Bottom - borderThickness),
            BorderColor);
        handle.DrawLine(new Vector2(box.Left, box.Top), new Vector2(box.Left, box.Bottom), BorderColor);
        handle.DrawLine(
            new Vector2(box.Right - borderThickness, box.Top),
            new Vector2(box.Right - borderThickness, box.Bottom),
            BorderColor);
    }

    protected override float GetDefaultContentMargin(Margin margin) => 0f;

    private static Color Lerp(Color from, Color to, float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        return new Color(
            from.R + (to.R - from.R) * progress,
            from.G + (to.G - from.G) * progress,
            from.B + (to.B - from.B) * progress,
            from.A + (to.A - from.A) * progress);
    }
}
