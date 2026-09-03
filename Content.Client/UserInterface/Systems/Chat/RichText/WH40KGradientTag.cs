using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using Content.Shared._WH40K.MetaProgress;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

public sealed partial class WH40KGradientTag : IMarkupTagHandler
{
    public string Name => "wh40kgradient";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        var text = ReadString(node.Attributes, "text", string.Empty);
        if (node.Closing || string.IsNullOrWhiteSpace(text))
        {
            control = null;
            return false;
        }

        var palette = ParsePalette(node.Attributes);
        if (palette.Count == 0)
        {
            control = null;
            return false;
        }

        var animated = ReadBool(node.Attributes, "animated", false);
        var durationMs = Math.Clamp(ReadInt(node.Attributes, "duration", 3500), 400, 60000);
        var phaseMs = ReadInt(node.Attributes, "phase", 0);

        var auraEnabled = ReadBool(node.Attributes, "aura", false);
        var auraRadius = Math.Clamp(ReadInt(node.Attributes, "auraradius", 1), 1, 4);
        var auraAlphaPercent = Math.Clamp(ReadInt(node.Attributes, "auraalpha", 65), 1, 100);
        var auraColor = Color.White;
        if (node.Attributes.TryGetValue("auracolor", out var auraColorParam) &&
            auraColorParam.TryGetString(out var auraColorRaw) &&
            TryResolveColor(auraColorRaw, out var parsedAura))
        {
            auraColor = parsedAura;
            auraEnabled = true;
        }

        control = new WH40KGradientNameControl(
            text,
            palette,
            animated,
            durationMs,
            phaseMs,
            auraEnabled,
            auraColor,
            auraRadius,
            auraAlphaPercent);
        return true;
    }

    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        if (ReadBool(node.Attributes, "overlay", false))
            context.Color.Push(Color.Transparent);
    }

    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        if (ReadBool(node.Attributes, "overlay", false))
            context.Color.Pop();
    }

    private static List<Color> ParsePalette(IReadOnlyDictionary<string, MarkupParameter> attributes)
    {
        if (attributes.TryGetValue("palette", out var paletteParameter) &&
            paletteParameter.TryGetString(out var paletteRaw))
        {
            return ParsePaletteString(paletteRaw);
        }

        if (attributes.TryGetValue("color", out var colorParameter) &&
            colorParameter.TryGetString(out var colorRaw) &&
            TryResolveColor(colorRaw, out var color))
        {
            return new List<Color> { color };
        }

        return new List<Color>();
    }

    private static List<Color> ParsePaletteString(string paletteRaw)
    {
        var result = new List<Color>();
        var parts = paletteRaw.Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (TryResolveColor(token, out var color))
                result.Add(color);

            if (result.Count >= WH40KDecorationMarkup.MaxPaletteColors)
                break;
        }

        return result;
    }

    private static bool TryResolveColor(string source, out Color color)
    {
        return WH40KDecorationMarkup.TryResolveColor(source, out color);
    }

    private static int ReadInt(IReadOnlyDictionary<string, MarkupParameter> attrs, string key, int fallback)
    {
        if (!attrs.TryGetValue(key, out var parameter))
            return fallback;

        if (parameter.TryGetLong(out var longValue) && longValue != null)
            return (int) longValue.Value;

        if (parameter.TryGetString(out var stringValue) && int.TryParse(stringValue, out var parsed))
            return parsed;

        return fallback;
    }

    private static string ReadString(IReadOnlyDictionary<string, MarkupParameter> attrs, string key, string fallback)
    {
        return attrs.TryGetValue(key, out var parameter) && parameter.TryGetString(out var value)
            ? value
            : fallback;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, MarkupParameter> attrs, string key, bool fallback)
    {
        if (!attrs.TryGetValue(key, out var parameter))
            return fallback;

        if (parameter.TryGetLong(out var longValue) && longValue != null)
            return longValue.Value != 0;

        if (!parameter.TryGetString(out var stringValue))
            return fallback;

        return stringValue.Equals("1", StringComparison.Ordinal) ||
               stringValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               stringValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed partial class WH40KGradientNameControl : Control
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly List<Rune> _runes;
    private readonly List<Color> _palette;
    private readonly bool _animated;
    private readonly int _durationMs;
    private readonly int _phaseMs;

    private readonly bool _auraEnabled;
    private readonly Color _auraColor;
    private readonly int _auraRadius;
    private readonly int _auraAlphaPercent;
    private float _availableWidthPixels;

    public WH40KGradientNameControl(
        string text,
        List<Color> palette,
        bool animated,
        int durationMs,
        int phaseMs,
        bool auraEnabled,
        Color auraColor,
        int auraRadius,
        int auraAlphaPercent)
    {
        IoCManager.InjectDependencies(this);

        _palette = palette;
        _animated = animated;
        _durationMs = durationMs;
        _phaseMs = phaseMs;

        _auraEnabled = auraEnabled;
        _auraColor = auraColor;
        _auraRadius = auraRadius;
        _auraAlphaPercent = auraAlphaPercent;

        _runes = new List<Rune>();
        foreach (var rune in text.EnumerateRunes())
        {
            _runes.Add(rune);
        }
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // The actual, transparent text is measured by RichTextEntry and provides normal word wrapping.
        // This control is an overlay and must not participate in that layout as one huge inline glyph.
        _availableWidthPixels = availableSize.X;
        return Vector2.Zero;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_runes.Count == 0)
            return;

        var font = ResolveFont();
        var elapsedMs = _timing.RealTime.TotalMilliseconds + _phaseMs;

        var shift = 0f;
        if (_animated)
        {
            var wrapped = elapsedMs % _durationMs;
            if (wrapped < 0)
                wrapped += _durationMs;

            shift = (float) (wrapped / _durationMs);
        }

        var auraColor = _auraColor.WithAlpha(Math.Clamp(_auraAlphaPercent / 100f, 0.05f, 1f));
        DrawWrapped(font, (sourceRune, index, baseline) =>
        {
            var t = _runes.Count == 1
                ? shift
                : (index / (float) Math.Max(1, _runes.Count - 1)) + shift;
            t -= MathF.Floor(t);

            var color = SampleGradient(_palette, t);
            DrawRune(handle, font, sourceRune, baseline, color, auraColor);
        });
    }

    private void DrawWrapped(Font font, Action<Rune, int, Vector2> drawRune)
    {
        var lineLeft = Parent?.PixelSizeBox.Left ?? SizeBox.Left;
        var lineRight = lineLeft + Math.Max(1f, _availableWidthPixels);
        var baseline = SizeBox.TopLeft + new Vector2(0f, font.GetAscent(UIScale));
        var lineHeight = font.GetLineHeight(UIScale);
        var index = 0;

        while (index < _runes.Count)
        {
            var rune = _runes[index];
            if (rune.Value == '\n')
            {
                baseline = new Vector2(lineLeft, baseline.Y + lineHeight);
                index++;
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                var advance = GetRuneAdvance(font, rune);
                if (baseline.X > lineLeft && baseline.X + advance <= lineRight)
                    baseline += new Vector2(advance, 0f);
                else if (baseline.X > lineLeft)
                    baseline = new Vector2(lineLeft, baseline.Y + lineHeight);

                index++;
                continue;
            }

            var wordEnd = index;
            var wordWidth = 0f;
            while (wordEnd < _runes.Count && !Rune.IsWhiteSpace(_runes[wordEnd]) && _runes[wordEnd].Value != '\n')
            {
                wordWidth += GetRuneAdvance(font, _runes[wordEnd]);
                wordEnd++;
            }

            if (baseline.X > lineLeft && baseline.X + wordWidth > lineRight)
                baseline = new Vector2(lineLeft, baseline.Y + lineHeight);

            while (index < wordEnd)
            {
                rune = _runes[index];
                var advance = GetRuneAdvance(font, rune);
                if (baseline.X > lineLeft && baseline.X + advance > lineRight)
                    baseline = new Vector2(lineLeft, baseline.Y + lineHeight);

                drawRune(rune, index, baseline);
                baseline += new Vector2(advance, 0f);
                index++;
            }
        }
    }

    private void DrawRune(DrawingHandleScreen handle, Font font, Rune rune, Vector2 baseline, Color color, Color auraColor)
    {
        if (_auraEnabled)
        {
            var offsets = _auraRadius switch
            {
                1 => AuraOffsetsR1,
                2 => AuraOffsetsR2,
                3 => AuraOffsetsR3,
                _ => AuraOffsetsR4,
            };

            foreach (var offset in offsets)
            {
                font.DrawChar(handle, rune, baseline + offset, UIScale, auraColor);
            }
        }

        font.DrawChar(handle, rune, baseline, UIScale, color);
    }

    private Font ResolveFont()
    {
        if (TryGetStyleProperty<Font>("font", out var font))
            return font;

        return UserInterfaceManager.ThemeDefaults.LabelFont;
    }

    private float GetRuneAdvance(Font font, Rune rune)
    {
        if (font.TryGetCharMetrics(rune, UIScale, out var metrics))
            return metrics.Advance;

        return font.GetCharMetrics(new Rune('?'), UIScale, fallback: false)?.Advance ?? 0f;
    }

    private static Color SampleGradient(IReadOnlyList<Color> palette, float t)
    {
        if (palette.Count == 1)
            return palette[0];

        var clamped = Math.Clamp(t, 0f, 1f);
        var segments = palette.Count - 1;
        var scaled = clamped * segments;
        var segment = Math.Min(segments - 1, (int) scaled);
        var localT = scaled - segment;
        return Color.InterpolateBetween(palette[segment], palette[segment + 1], localT);
    }

    private static readonly Vector2[] AuraOffsetsR1 =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
    };

    private static readonly Vector2[] AuraOffsetsR2 =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        new(-2, 0), new(2, 0), new(0, -2), new(0, 2),
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
    };

    private static readonly Vector2[] AuraOffsetsR3 =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        new(-2, 0), new(2, 0), new(0, -2), new(0, 2),
        new(-3, 0), new(3, 0), new(0, -3), new(0, 3),
        new(-2, -1), new(2, -1), new(-2, 1), new(2, 1),
        new(-1, -2), new(1, -2), new(-1, 2), new(1, 2),
    };

    private static readonly Vector2[] AuraOffsetsR4 =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        new(-2, 0), new(2, 0), new(0, -2), new(0, 2),
        new(-3, 0), new(3, 0), new(0, -3), new(0, 3),
        new(-4, 0), new(4, 0), new(0, -4), new(0, 4),
        new(-2, -1), new(2, -1), new(-2, 1), new(2, 1),
        new(-1, -2), new(1, -2), new(-1, 2), new(1, 2),
        new(-3, -1), new(3, -1), new(-3, 1), new(3, 1),
        new(-1, -3), new(1, -3), new(-1, 3), new(1, 3),
    };
}
