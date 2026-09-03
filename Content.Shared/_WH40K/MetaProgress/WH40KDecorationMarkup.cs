using System;
using System.Collections.Generic;
using System.Text;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.MetaProgress;

/// <summary>
///     Единый безопасный формат разметки для чата и предпросмотра украшений.
///     Все строковые параметры экранируются до подстановки в rich-text.
/// </summary>
public static class WH40KDecorationMarkup
{
    public const string DefaultColorHex = "#87CEFA";
    public const int MaxPaletteColors = 8;
    public const int MinGradientDurationMs = 400;
    public const int MaxGradientDurationMs = 60_000;
    public const int MinEffectDurationMs = 100;
    public const int MaxEffectDurationMs = 120_000;

    public static string BuildGradientMarkup(
        string text,
        IEnumerable<string> gradientColors,
        string solidColor,
        bool animated,
        int durationMs,
        string auraColorHex,
        int auraRadius,
        int auraAlphaPercent)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var displayText = ToSingleLineText(text);
        var palette = BuildPalette(gradientColors, solidColor);
        var auraColor = Color.White;
        var hasAura = auraRadius > 0 && auraAlphaPercent > 0 && TryResolveColor(auraColorHex, out auraColor);
        if (palette.Count == 0 && !hasAura)
            return string.Empty;

        if (palette.Count == 0)
            palette.Add(DefaultColorHex);

        var builder = new StringBuilder();
        // The text remains a normal rich-text child so the engine can calculate line breaks.
        // The client tag draws the animated overlay above this transparent source text.
        builder.Append("[wh40kgradient text=\"")
            .Append(EscapeParameter(displayText))
            .Append("\" palette=\"")
            .Append(string.Join("|", palette))
            .Append("\" animated=")
            .Append(palette.Count >= 2 && animated ? 1 : 0)
            .Append(" duration=")
            .Append(ClampGradientDuration(durationMs))
            .Append(" overlay=1");

        if (hasAura)
        {
            builder.Append(" aura=1 auracolor=\"")
                .Append(auraColor.ToHex())
                .Append("\" auraradius=")
                .Append(Math.Clamp(auraRadius, 1, 4))
                .Append(" auraalpha=")
                .Append(Math.Clamp(auraAlphaPercent, 1, 100));
        }

        return builder.Append(']')
            .Append(FormattedMessage.EscapeText(displayText))
            .Append("[/wh40kgradient]")
            .ToString();
    }

    public static string BuildTitleMarkup(
        string text,
        IEnumerable<string> gradientColors,
        string solidColor,
        bool animated,
        int gradientDurationMs,
        string effect,
        int revealMs,
        int holdMs,
        int dissolveMs,
        string outlineColorHex,
        int outlineWidth,
        int outlineAlphaPercent,
        IEnumerable<string>? fallbackGradientColors = null,
        string fallbackSolidColor = "",
        string auraColorHex = "",
        int auraRadius = 0,
        int auraAlphaPercent = 0,
        bool fallbackAnimated = false,
        int fallbackGradientDurationMs = 3500,
        string fallbackAuraColorHex = "",
        int fallbackAuraRadius = 0,
        int fallbackAuraAlphaPercent = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var displayText = ToSingleLineText(text);
        var palette = BuildPalette(gradientColors, solidColor);
        var usesFallbackStyle = palette.Count == 0 && fallbackGradientColors != null;
        if (usesFallbackStyle)
            palette = BuildPalette(fallbackGradientColors ?? [], fallbackSolidColor);
        if (palette.Count == 0)
            palette.Add(DefaultColorHex);

        var effectiveAnimated = usesFallbackStyle ? fallbackAnimated : animated;
        var effectiveDurationMs = usesFallbackStyle ? fallbackGradientDurationMs : gradientDurationMs;
        var effectiveAuraHex = usesFallbackStyle ? fallbackAuraColorHex : auraColorHex;
        var effectiveAuraRadius = usesFallbackStyle ? fallbackAuraRadius : auraRadius;
        var effectiveAuraAlphaPercent = usesFallbackStyle ? fallbackAuraAlphaPercent : auraAlphaPercent;

        var auraColor = Color.White;
        var hasAura = effectiveAuraRadius > 0 && effectiveAuraAlphaPercent > 0 &&
                      TryResolveColor(effectiveAuraHex, out auraColor);

        var outlineColor = Color.White;
        var hasOutline = outlineWidth > 0 && outlineAlphaPercent > 0 && TryResolveColor(outlineColorHex, out outlineColor);
        var normalizedEffect = NormalizeTitleEffect(effect);

        // The custom title control is only necessary for an actual title effect or an outline.
        // Keeping ordinary titles in the stock rich-text path avoids inline-control layout failures
        // for long localized titles and lets every simple title render reliably in the catalogue.
        if (string.IsNullOrEmpty(normalizedEffect) && !hasOutline && !hasAura)
        {
            if (palette.Count == 1 && !effectiveAnimated)
                return $"[color={palette[0]}]{FormattedMessage.EscapeText(displayText)}[/color]";

            return BuildGradientMarkup(
                displayText,
                palette,
                string.Empty,
                effectiveAnimated,
                effectiveDurationMs,
                effectiveAuraHex,
                effectiveAuraRadius,
                effectiveAuraAlphaPercent);
        }

        var builder = new StringBuilder();
        builder.Append("[wh40ktitlefx text=\"")
            .Append(EscapeParameter(displayText))
            .Append("\" palette=\"")
            .Append(string.Join("|", palette))
            .Append("\" animated=")
            .Append(palette.Count >= 2 && effectiveAnimated ? 1 : 0)
            .Append(" duration=")
            .Append(ClampGradientDuration(effectiveDurationMs))
            .Append(" reveal=")
            .Append(ClampEffectDuration(revealMs))
            .Append(" hold=")
            .Append(ClampEffectDuration(holdMs))
            .Append(" dissolve=")
            .Append(ClampEffectDuration(dissolveMs))
            .Append(" cursor=1 overlay=1");

        if (!string.IsNullOrWhiteSpace(normalizedEffect))
            builder.Append(" effect=\"").Append(normalizedEffect).Append('"');

        if (hasOutline)
        {
            builder.Append(" outline=1 outlinecolor=\"")
                .Append(outlineColor.ToHex())
                .Append("\" outlinewidth=")
                .Append(Math.Clamp(outlineWidth, 1, 3))
                .Append(" outlinealpha=")
                .Append(Math.Clamp(outlineAlphaPercent, 1, 100));
        }

        if (hasAura)
        {
            builder.Append(" aura=1 auracolor=\"")
                .Append(auraColor.ToHex())
                .Append("\" auraradius=")
                .Append(Math.Clamp(effectiveAuraRadius, 1, 4))
                .Append(" auraalpha=")
                .Append(Math.Clamp(effectiveAuraAlphaPercent, 1, 100));
        }

        return builder.Append(']')
            .Append(FormattedMessage.EscapeText(displayText))
            .Append("[/wh40ktitlefx]")
            .ToString();
    }

    public static List<string> BuildPalette(IEnumerable<string> gradientColors, string solidColor)
    {
        var palette = new List<string>(MaxPaletteColors);
        foreach (var source in gradientColors)
        {
            if (palette.Count >= MaxPaletteColors)
                break;

            if (TryResolveColor(source, out var color))
                palette.Add(color.ToHex());
        }

        if (palette.Count == 0 && TryResolveColor(solidColor, out var solid))
            palette.Add(solid.ToHex());

        return palette;
    }

    public static bool TryResolveColor(string source, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var trimmed = source.Trim();
        if (Color.TryFromHex(trimmed) is { } hex)
        {
            color = hex;
            return true;
        }

        return Color.TryFromName(trimmed, out color);
    }

    public static string NormalizeTitleEffect(string source)
    {
        return source.Trim().ToLowerInvariant() switch
        {
            "binary" => "binary",
            "scan" => "scan",
            "fish" or "fish-swim" => "fish",
            "scramble-decode" or "scramble" => "scramble-decode",
            "typewriter-cursor" or "typewriter" => "typewriter-cursor",
            "wave" => "wave",
            "glitch-slice" or "glitch" => "glitch-slice",
            "noise-dissolve" or "dissolve-noise" or "noise" => "noise-dissolve",
            "scanline" => "scanline",
            "flip" or "discord-flip" => "flip",
            _ => string.Empty,
        };
    }

    public static int ClampGradientDuration(int durationMs)
        => Math.Clamp(durationMs, MinGradientDurationMs, MaxGradientDurationMs);

    public static int ClampEffectDuration(int durationMs)
        => Math.Clamp(durationMs, MinEffectDurationMs, MaxEffectDurationMs);

    /// <summary>
    ///     Экранирует именно строковое значение параметра rich-text, а не заменяет символы пользовательским текстом.
    /// </summary>
    public static string EscapeParameter(string value)
    {
        return FormattedMessage.EscapeStringParameter(ToSingleLineText(value));
    }

    private static string ToSingleLineText(string value)
        => value.Replace("\r", " ").Replace("\n", " ");
}
