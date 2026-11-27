using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.Synthetics;

public static class Wh40kChassisBayLayout
{
    public const float StripTop = 0.795f;
    public const float StripBottom = 0.835f;

    public static UIBox2 Strip(UIBox2 screen) => new(
        screen.Left + screen.Width * 0.28f,
        screen.Top + screen.Height * StripTop,
        screen.Left + screen.Width * 0.72f,
        screen.Top + screen.Height * StripBottom);

    public static UIBox2 Tag(UIBox2 screen, float scale)
    {
        var width = 14f * Wh40kCyberneticHudLayout.CharWidth(scale) + Wh40kCyberneticHudLayout.Padding * 2f;
        var height = Wh40kCyberneticHudLayout.LineHeight(scale) + Wh40kCyberneticHudLayout.Padding;
        var centre = screen.Left + screen.Width * 0.51f;
        var top = screen.Top + Wh40kCyberneticHudLayout.Margin;
        return new UIBox2(centre - width / 2f, top, centre + width / 2f, top + height);
    }
}

/// <summary>
/// Screen-space warning overlay for a Cybernetic (Mechanicus) or IPC player whose service bay is open
/// (pulsing amber viewport border + bottom warning strip) or whose servos are overclocked (top badge).
/// </summary>
public sealed class Wh40kChassisBayOverlay : Overlay
{
    public static readonly Color Amber = Wh40kTerminalStyle.AmberBright;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public float BayAlpha;
    public float OverclockAlpha;
    public float Pulse;
    public string BayText = string.Empty;
    public string OverclockText = string.Empty;

    protected override bool BeforeDraw(in OverlayDrawArgs args) => BayAlpha > 0.004f || OverclockAlpha > 0.004f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var control = args.ViewportControl as Control;
        var screen = Wh40kCyberneticHudLayout.Screen(args);
        if (screen.Width < 160f || screen.Height < 80f)
            return;

        var uiScale = control?.UIScale ?? 1f;
        var scale = Wh40kCyberneticHudLayout.Scale(1f, uiScale);
        var bold = Wh40kTerminalStyle.Mono((int) Wh40kCyberneticHudLayout.Font(scale), true);
        var pulse = 0.65f + 0.35f * MathF.Sin(Pulse);

        if (BayAlpha > 0.004f)
        {
            DrawFrame(handle, screen, Amber.WithAlpha(0.55f * pulse * BayAlpha), MathF.Max(2f, MathF.Round(2f * uiScale)));
            DrawStrip(handle, Wh40kChassisBayLayout.Strip(screen), bold, BayText, pulse * BayAlpha);
        }

        if (OverclockAlpha > 0.004f)
            DrawStrip(handle, Wh40kChassisBayLayout.Tag(screen, scale), bold, OverclockText, OverclockAlpha);
    }

    private static void DrawFrame(DrawingHandleScreen handle, UIBox2 screen, Color colour, float thickness)
    {
        handle.DrawRect(new UIBox2(screen.Left, screen.Top, screen.Right, screen.Top + thickness), colour);
        handle.DrawRect(new UIBox2(screen.Left, screen.Bottom - thickness, screen.Right, screen.Bottom), colour);
        handle.DrawRect(new UIBox2(screen.Left, screen.Top, screen.Left + thickness, screen.Bottom), colour);
        handle.DrawRect(new UIBox2(screen.Right - thickness, screen.Top, screen.Right, screen.Bottom), colour);
    }

    private static void DrawStrip(DrawingHandleScreen handle, UIBox2 box, Font font, string text, float alpha)
    {
        handle.DrawRect(box, Color.FromHex("#160e02").WithAlpha(0.62f * alpha));
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + 1f), Amber.WithAlpha(0.75f * alpha));
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - 1f, box.Right, box.Bottom), Amber.WithAlpha(0.75f * alpha));

        if (text.Length == 0)
            return;

        var size = MathF.Min(1f, (box.Height - 4f) / font.GetLineHeight(1f));
        var width = handle.GetDimensions(font, text, size).X;
        var room = box.Width - 10f;
        if (width > room && width > 0f)
            size *= room / width;

        var dimensions = handle.GetDimensions(font, text, size);
        handle.DrawString(
            font,
            new Vector2(box.Left + (box.Width - dimensions.X) / 2f, box.Top + (box.Height - dimensions.Y) / 2f),
            text,
            size,
            Amber.WithAlpha(alpha));
    }
}
