using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>
/// A thin, fading command-deck row surface. It avoids generic rectangular button chrome
/// while keeping the whole row, including its index, visually coherent.
/// </summary>
internal sealed class LobbyMenuRowStyleBox : StyleBox
{
    private const int GradientSteps = 32;
    private static readonly ProtoId<ShaderPrototype> DividerShaderId = "HeretekLobbyMenuDivider";

    private readonly ShaderInstance _dividerShader;

    public Color LeftColor { get; set; }
    public Color MiddleColor { get; set; }
    public Color RightColor { get; set; }
    /// <summary>Where the primary colour transitions into the final transparent tail.</summary>
    public float MiddleStop { get; set; }
    public Color AccentColor { get; set; }
    public bool DrawAccent { get; set; }
    public bool DrawDivider { get; set; } = true;

    public LobbyMenuRowStyleBox()
    {
        _dividerShader = IoCManager.Resolve<IPrototypeManager>().Index(DividerShaderId).Instance();
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var width = box.Width;
        if (width <= 0f || box.Height <= 0f)
            return;

        // Most rows are fully transparent while idle. Drawing their invisible
        // gradient still created hundreds of batched quads per frame.
        if (LeftColor.A > 0f || MiddleColor.A > 0f || RightColor.A > 0f)
        {
            for (var step = 0; step < GradientSteps; step++)
            {
                var start = step / (float) GradientSteps;
                var end = (step + 1) / (float) GradientSteps;
                var progress = (start + end) * 0.5f;
                var color = MiddleStop is > 0f and < 1f
                    ? progress <= MiddleStop
                        ? Lerp(LeftColor, MiddleColor, progress / MiddleStop)
                        : Lerp(MiddleColor, RightColor, (progress - MiddleStop) / (1f - MiddleStop))
                    : Lerp(LeftColor, RightColor, progress);
                handle.DrawRect(
                    UIBox2.FromDimensions(
                        new Vector2(box.Left + width * start, box.Top),
                        new Vector2(width * (end - start), box.Height)),
                    color);
            }
        }

        if (DrawAccent)
        {
            var x = box.Left + 7f * uiScale;
            var top = box.Top + 10f * uiScale;
            var height = MathF.Max(0f, box.Height - 20f * uiScale);
            handle.DrawRect(
                UIBox2.FromDimensions(new Vector2(x, top), new Vector2(MathF.Max(1f, uiScale), height)),
                AccentColor);
            handle.DrawRect(
                UIBox2.FromDimensions(new Vector2(x - uiScale, top), new Vector2(3f * uiScale, height)),
                AccentColor.WithAlpha(AccentColor.A * 0.16f));
        }

        if (DrawDivider)
            DrawDividerGradient(handle, box, uiScale);
    }

    protected override float GetDefaultContentMargin(Margin margin) => 0f;

    private void DrawDividerGradient(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var height = MathF.Max(1f, uiScale);
        var previousShader = handle.GetShader();
        handle.UseShader(_dividerShader);
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(box.Left, box.Bottom - height), new Vector2(box.Width, height)),
            Color.White);
        handle.UseShader(previousShader);
    }

    private static Color Lerp(Color from, Color to, float progress)
    {
        return new Color(
            from.R + (to.R - from.R) * progress,
            from.G + (to.G - from.G) * progress,
            from.B + (to.B - from.B) * progress,
            from.A + (to.A - from.A) * progress);
    }
}
