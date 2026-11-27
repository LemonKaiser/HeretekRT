using System.Numerics;
using Content.Shared._WH40K.Synthetics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Synthetics;

public static class Wh40kCyberneticHudLayout
{
    public const float Margin = 16f;
    public const float Padding = 6f;
    public const float BaseFont = 11f;
    public const int MaxSystemGauges = 7;
    public const int MaxAdviceRows = 2;
    public const int SystemColumns = 28;
    public const int BarColumn = 10;
    public const float PanelTop = 0.40f;
    public const float BottomBand = 0.78f;
    public const float MinScale = 0.6f;
    public const float MaxScale = 2.5f;

    public static UIBox2 Screen(in OverlayDrawArgs args) =>
        Screen(args.ViewportBounds, (args.ViewportControl as Control)?.GlobalPixelRect);

    public static UIBox2 Screen(UIBox2i bounds, UIBox2i? shown) =>
        shown is { } control ? bounds.Intersection(control) ?? bounds : bounds;

    public static float Scale(float setting, float uiScale) =>
        Math.Clamp(setting, MinScale, MaxScale) * MathF.Max(0.1f, uiScale);

    public static float Font(float scale) => MathF.Max(6f, MathF.Round(BaseFont * scale));
    public static float LineHeight(float scale) => Font(scale) * 1.4f + 4f;
    public static float CharWidth(float scale) => Font(scale) * 0.62f * 1.4f;

    public static float FitScale(UIBox2 screen, float requested)
    {
        var scale = requested;
        while (scale > 0.1f &&
               (SystemHeight(scale, MaxSystemGauges, MaxAdviceRows) > screen.Height * (BottomBand - PanelTop) ||
                SystemWidth(scale) > screen.Width * 0.27f ||
                42f * CharWidth(scale) + Padding * 2f > screen.Width * 0.31f))
        {
            scale = MathF.Max(0.1f, scale - 0.05f);
        }

        return scale;
    }

    public static float SystemWidth(float scale) => SystemColumns * CharWidth(scale) + Padding * 2f;

    public static float SystemHeight(float scale, int gauges, int adviceRows) =>
        (1f + gauges + 0.5f + 1f + Math.Clamp(adviceRows, 1, MaxAdviceRows)) * LineHeight(scale) + Padding * 2f;

    public static Wh40kSystemPanel SystemPanel(UIBox2 screen, float scale, int gauges, int adviceRows)
    {
        gauges = Math.Clamp(gauges, 0, MaxSystemGauges);
        adviceRows = Math.Clamp(adviceRows, 1, MaxAdviceRows);

        var line = LineHeight(scale);
        var width = MathF.Min(SystemWidth(scale), screen.Width * 0.27f);
        var left = screen.Left + screen.Width * 0.14f;
        var top = screen.Top + screen.Height * PanelTop;
        var box = new UIBox2(left, top, left + width, top + SystemHeight(scale, gauges, adviceRows));

        var y = top + Padding;
        UIBox2 Next(float rows = 1f)
        {
            var row = new UIBox2(left + Padding, y, left + width - Padding, y + line * rows);
            y += line * rows;
            return row;
        }

        var title = Next();
        var rows = new UIBox2[gauges];
        for (var i = 0; i < gauges; i++)
            rows[i] = Next();

        y += line * 0.5f;
        var faults = Next();
        var advice = new UIBox2[adviceRows];
        for (var i = 0; i < adviceRows; i++)
            advice[i] = Next();

        return new Wh40kSystemPanel(box, title, rows, faults, advice, line);
    }

    public static List<string> WrapAdvice(string text, int prefixLength, int columns = SystemColumns)
    {
        var rows = new List<string>();
        var indent = new string(' ', Math.Clamp(prefixLength, 0, columns / 2));
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? (rows.Count == 0 ? word : indent + word) : line + " " + word;
            if (candidate.Length <= columns)
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0)
                rows.Add(line);
            line = rows.Count == 0 ? word : indent + word;
            if (line.Length > columns)
                line = line[..columns];
        }

        if (line.Length > 0)
            rows.Add(line);
        if (rows.Count == 0)
            rows.Add(string.Empty);

        if (rows.Count <= MaxAdviceRows)
            return rows;

        var last = rows[MaxAdviceRows - 1];
        rows.RemoveRange(MaxAdviceRows - 1, rows.Count - MaxAdviceRows + 1);
        rows.Add((last.Length + 3 > columns ? last[..(columns - 3)] : last) + "...");
        return rows;
    }

    public static (bool System, bool Diagnostics) Visible(Wh40kCyberneticHudTier tier, bool standby, bool dead, int faults) =>
        (tier >= Wh40kCyberneticHudTier.Light && !standby && !dead,
            tier >= Wh40kCyberneticHudTier.Moderate && faults > 0 && !standby && !dead);

    public static UIBox2 Diagnostics(UIBox2 screen, float scale, int lines)
    {
        var width = MathF.Min(42f * CharWidth(scale) + Padding * 2f, screen.Width * 0.31f);
        var height = (1 + Math.Max(lines, 1)) * LineHeight(scale) + Padding * 2f;
        var top = screen.Top + screen.Height * PanelTop;
        return new UIBox2(
            screen.Left + screen.Width * 0.90f - width,
            top,
            screen.Left + screen.Width * 0.90f,
            top + height);
    }

    public static UIBox2 Row(UIBox2 block, int row, float line) => new(
        block.Left + Padding,
        block.Top + Padding + row * line,
        block.Right - Padding,
        block.Top + Padding + (row + 1) * line);

    public static int MaxLines(UIBox2 screen, float scale, int cap)
    {
        var room = screen.Height * (BottomBand - PanelTop) - Padding * 2f - LineHeight(scale);
        return Math.Clamp((int) MathF.Floor(room / LineHeight(scale)), 1, cap);
    }

    public static UIBox2 Banner(UIBox2 screen, float scale)
    {
        var height = LineHeight(scale) + Padding * 2f;
        var top = screen.Top + screen.Height * 0.335f;
        return new UIBox2(screen.Left + screen.Width * 0.30f, top, screen.Left + screen.Width * 0.70f, top + height);
    }
}

public readonly record struct Wh40kSystemPanel(
    UIBox2 Box,
    UIBox2 Title,
    UIBox2[] Gauges,
    UIBox2 Faults,
    UIBox2[] Advice,
    float Line);
