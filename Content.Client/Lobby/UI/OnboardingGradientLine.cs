using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Draws the one-pixel onboarding rule used by the HTML reference:
/// bright gold at the start, a dim middle stop, and a transparent tail.
/// </summary>
public sealed class OnboardingGradientLine : Control
{
    private const int GradientSteps = 32;

    public Color StartColor { get; set; } = Color.FromHex("#B69754");
    public Color MiddleColor { get; set; } = Color.FromHex("#B697543B");
    public Color EndColor { get; set; } = Color.Transparent;
    public float MiddleStop { get; set; } = 0.7f;
    public float Thickness { get; set; } = 1f;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        var height = MathF.Max(1f, Thickness * UIScale);
        var top = box.Top + (box.Height - height) * 0.5f;
        for (var step = 0; step < GradientSteps; step++)
        {
            var start = step / (float) GradientSteps;
            var end = (step + 1) / (float) GradientSteps;
            var progress = (start + end) * 0.5f;
            var color = progress <= MiddleStop
                ? Lerp(StartColor, MiddleColor, progress / MiddleStop)
                : Lerp(MiddleColor, EndColor, (progress - MiddleStop) / (1f - MiddleStop));

            handle.DrawRect(
                UIBox2.FromDimensions(
                    new Vector2(box.Left + box.Width * start, top),
                    new Vector2(box.Width * (end - start), height)),
                color);
        }
    }

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
