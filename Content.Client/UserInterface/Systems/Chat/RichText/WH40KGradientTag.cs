using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Content.Shared._WH40K.MetaProgress;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

public sealed partial class WH40KGradientTag : IMarkupTagHandler
{
    public string Name => "wh40kgradient";

    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        var font = context.Font.Peek();
        var text = ReadString(node.Attributes, "text", string.Empty);
        var palette = ParsePalette(node.Attributes);
        if (!string.IsNullOrEmpty(text) && palette.Count > 0)
        {
            var auraEnabled = ReadBool(node.Attributes, "aura", false);
            var auraColor = Color.White;
            if (node.Attributes.TryGetValue("auracolor", out var parameter) &&
                parameter.TryGetString(out var raw) && TryResolveColor(raw, out var parsed))
            {
                auraColor = parsed;
                auraEnabled = true;
            }
            font = new WH40KGradientFont(font, text, palette,
                ReadBool(node.Attributes, "animated", false),
                Math.Clamp(ReadInt(node.Attributes, "duration", 3500), 400, 60000),
                ReadInt(node.Attributes, "phase", 0), auraEnabled, auraColor,
                Math.Clamp(ReadInt(node.Attributes, "auraradius", 1), 1, 4),
                Math.Clamp(ReadInt(node.Attributes, "auraalpha", 65), 1, 100));
        }
        context.Font.Push(font);
    }

    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context) => context.Font.Pop();

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

internal sealed partial class WH40KGradientFont : ChatFont
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly int _runeCount;
    private readonly List<Color> _palette;
    private readonly bool _animated;
    private readonly int _durationMs;
    private readonly int _phaseMs;
    private readonly bool _auraEnabled;
    private readonly Color _auraColor;
    private readonly int _auraRadius;
    private readonly int _auraAlphaPercent;
    private int _index;

    public WH40KGradientFont(Font baseFont, string text, List<Color> palette, bool animated, int durationMs,
        int phaseMs, bool auraEnabled, Color auraColor, int auraRadius, int auraAlphaPercent) : base(baseFont)
    {
        IoCManager.InjectDependencies(this);
        foreach (var _ in text.EnumerateRunes())
            _runeCount++;
        _palette = palette;
        _animated = animated;
        _durationMs = durationMs;
        _phaseMs = phaseMs;
        _auraEnabled = auraEnabled;
        _auraColor = auraColor;
        _auraRadius = auraRadius;
        _auraAlphaPercent = auraAlphaPercent;
    }

    public override float DrawChar(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale,
        Color color, bool fallback = true)
    {
        var elapsedMs = _timing.RealTime.TotalMilliseconds + _phaseMs;
        var shift = _animated ? (float) ((elapsedMs % _durationMs) / _durationMs) : 0f;
        var t = (_runeCount <= 1 ? 0f : _index / (float) (_runeCount - 1)) + shift;
        _index++;
        t -= MathF.Floor(t);
        var gradient = SampleGradient(_palette, t);
        gradient = gradient.WithAlpha(gradient.A * color.A);

        if (_auraEnabled && !Rune.IsWhiteSpace(rune))
        {
            var offsets = _auraRadius switch
            {
                1 => AuraOffsetsR1,
                2 => AuraOffsetsR2,
                3 => AuraOffsetsR3,
                _ => AuraOffsetsR4,
            };
            var aura = _auraColor.WithAlpha(Math.Clamp(_auraAlphaPercent / 100f, 0.05f, 1f) * color.A);
            foreach (var offset in offsets)
                BaseFont.DrawChar(handle, rune, baseline + offset, scale, aura, fallback);
        }
        return BaseFont.DrawChar(handle, rune, baseline, scale, gradient, fallback);
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
