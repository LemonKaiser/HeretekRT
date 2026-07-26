using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Reproduces the horizontal backdrop shade from the onboarding HTML reference.
/// The ordinary lobby keeps its own asymmetric deck lighting.
/// </summary>
public sealed class OnboardingBackdropShade : Control
{
    private const int GradientSteps = 48;
    private const float MiddleStop = 0.46f;

    private static readonly Color Start = Color.FromHex("#060705F7");
    private static readonly Color Middle = Color.FromHex("#0B0B08E8");
    private static readonly Color End = Color.FromHex("#070806F3");

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        for (var step = 0; step < GradientSteps; step++)
        {
            var start = step / (float) GradientSteps;
            var end = (step + 1) / (float) GradientSteps;
            var progress = (start + end) * 0.5f;
            var color = progress <= MiddleStop
                ? Lerp(Start, Middle, progress / MiddleStop)
                : Lerp(Middle, End, (progress - MiddleStop) / (1f - MiddleStop));

            handle.DrawRect(
                UIBox2.FromDimensions(
                    new Vector2(box.Left + box.Width * start, box.Top),
                    new Vector2(box.Width * (end - start), box.Height)),
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
