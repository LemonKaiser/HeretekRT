using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// The master creator's common content surface.
/// </summary>
internal sealed class OnboardingPageFrameStyleBox : StyleBox
{
    public Color BackgroundColor { get; set; }
    public Color BorderColor { get; set; }
    public float ContentMarginLeftOverride { get; set; }
    public float ContentMarginRightOverride { get; set; }
    public float ContentMarginTopOverride { get; set; }
    public float ContentMarginBottomOverride { get; set; }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        handle.DrawRect(box, BackgroundColor);
        var gradientHeight = box.Height * 0.18f;
        const int gradientSteps = 18;
        for (var step = 0; step < gradientSteps; step++)
        {
            var start = step / (float) gradientSteps;
            var end = (step + 1) / (float) gradientSteps;
            var opacity = 1f - (start + end) * 0.5f;
            handle.DrawRect(
                UIBox2.FromDimensions(
                    new Vector2(box.Left, box.Top + gradientHeight * start),
                    new Vector2(box.Width, gradientHeight * (end - start))),
                Color.FromHex("#24221847").WithAlpha(Color.FromHex("#24221847").A * opacity));
        }

        var line = BorderColor;
        var dimLine = line.WithAlpha(line.A * 0.62f);
        var borderThickness = MathF.Max(1f, uiScale);
        handle.DrawLine(new Vector2(box.Left, box.Top), new Vector2(box.Right, box.Top), line);
        handle.DrawLine(new Vector2(box.Left, box.Bottom - borderThickness), new Vector2(box.Right, box.Bottom - borderThickness), dimLine);
        handle.DrawLine(new Vector2(box.Left, box.Top), new Vector2(box.Left, box.Bottom), dimLine);
        handle.DrawLine(new Vector2(box.Right - borderThickness, box.Top), new Vector2(box.Right - borderThickness, box.Bottom), dimLine);

        var marker = MathF.Min(48f * uiScale, box.Width * 0.12f);
        var markerY = box.Top + 10f * uiScale;
        handle.DrawLine(new Vector2(box.Left + 27f * uiScale, markerY), new Vector2(box.Left + 27f * uiScale + marker, markerY), line.WithAlpha(line.A * 0.62f));
        handle.DrawLine(new Vector2(box.Right - 27f * uiScale - marker, markerY), new Vector2(box.Right - 27f * uiScale, markerY), line.WithAlpha(line.A * 0.62f));
    }

    protected override float GetDefaultContentMargin(Margin margin)
    {
        return margin switch
        {
            Margin.Left => ContentMarginLeftOverride,
            Margin.Right => ContentMarginRightOverride,
            Margin.Top => ContentMarginTopOverride,
            Margin.Bottom => ContentMarginBottomOverride,
            _ => throw new ArgumentOutOfRangeException(nameof(margin), margin, null),
        };
    }
}
