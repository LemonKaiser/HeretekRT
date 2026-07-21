using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// The logo is drawn letter by letter so it can retain the prototype's generous
/// tracking and gold first letter without embedding spaces into the actual word.
/// </summary>
public sealed class LobbyLogoControl : Control
{
    private const string Word = "HERETEK";
    private const float LogoAreaHeight = 84f;
    private const float SubtitleCenterY = 99f;
    private const float SubtitleLineLength = 32f;
    private const float SubtitleLineGap = 8f;

    private static readonly string[] Glyphs = { "H", "E", "R", "E", "T", "E", "K" };

    private static readonly Color Paper = Color.FromHex("#F0EADC");
    private static readonly Color Gold = Color.FromHex("#E5C879");
    private static readonly Color SubtitleColor = Color.FromHex("#C2B9A7");
    private static readonly Color SubtitleLineColor = Color.FromHex("#8D7E50");

    public Font? FontOverride { get; set; }
    public Font? SubtitleFontOverride { get; set; }
    public string Subtitle { get; set; } = string.Empty;
    public float Pulse { get; set; }
    public float LetterSpacing { get; set; } = 4.6f;

    private readonly float[] _glyphWidths = new float[Word.Length];
    private Font? _layoutFont;
    private float _layoutScale = float.NaN;
    private float _layoutTotalWidth;
    private float _layoutGlyphHeight;
    private Font? _subtitleLayoutFont;
    private string? _subtitleLayoutText;
    private float _subtitleLayoutScale = float.NaN;
    private Vector2 _subtitleDimensions;

    public LobbyLogoControl()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // The parent is a fixed-height lockup.  A stable desired size keeps this
        // decorative control from changing the menu's measured geometry.
        return new Vector2(292f, 60f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var font = FontOverride;
        if (font == null || PixelSize.X <= 1f || PixelSize.Y <= 1f)
            return;

        var scale = UIScale;
        var spacing = LetterSpacing * scale;
        EnsureWordLayout(handle, font, scale);
        var totalWidth = _layoutTotalWidth + spacing * (Word.Length - 1);
        var origin = new Vector2(
            MathF.Max(0f, (PixelSize.X - totalWidth) * 0.5f),
            MathF.Max(0f, (MathF.Min(PixelSize.Y, LogoAreaHeight * scale) - _layoutGlyphHeight) * 0.5f));

        // The dark offset preserves the type's edge on bright portions of the art.
        DrawWord(handle, font, origin + new Vector2(0f, 2f * scale), scale, Color.Black.WithAlpha(0.66f));

        // Several low-alpha passes make a visible, soft gold breathing halo rather
        // than tinting the glyphs themselves yellow.
        var pulse = Math.Clamp(Pulse, 0f, 1f);
        DrawGlow(handle, font, origin, scale, 1.2f, Gold.WithAlpha(0.020f + pulse * 0.024f));
        DrawGlow(handle, font, origin, scale, 2.8f, Gold.WithAlpha(0.010f + pulse * 0.018f));

        var cursor = origin.X;
        for (var i = 0; i < Word.Length; i++)
        {
            var color = i == 0 ? Gold : Paper;
            handle.DrawString(font, new Vector2(cursor, origin.Y), Glyphs[i], scale, color);
            cursor += _glyphWidths[i] + spacing;
        }

        DrawSubtitle(handle, scale);
    }

    private void DrawGlow(
        DrawingHandleScreen handle,
        Font font,
        Vector2 origin,
        float scale,
        float radius,
        Color color)
    {
        var offset = radius * scale;
        var diagonal = offset * 0.7f;
        DrawWord(handle, font, origin + new Vector2(-offset, 0f), scale, color);
        DrawWord(handle, font, origin + new Vector2(offset, 0f), scale, color);
        DrawWord(handle, font, origin + new Vector2(0f, -offset), scale, color);
        DrawWord(handle, font, origin + new Vector2(0f, offset), scale, color);
        DrawWord(handle, font, origin + new Vector2(-diagonal, -diagonal), scale, color);
        DrawWord(handle, font, origin + new Vector2(diagonal, -diagonal), scale, color);
        DrawWord(handle, font, origin + new Vector2(-diagonal, diagonal), scale, color);
        DrawWord(handle, font, origin + new Vector2(diagonal, diagonal), scale, color);
    }

    private void DrawWord(
        DrawingHandleScreen handle,
        Font font,
        Vector2 origin,
        float scale,
        Color color)
    {
        var cursor = origin.X;
        var spacing = LetterSpacing * scale;
        for (var i = 0; i < Word.Length; i++)
        {
            handle.DrawString(font, new Vector2(cursor, origin.Y), Glyphs[i], scale, color);
            cursor += _glyphWidths[i] + spacing;
        }
    }

    private void DrawSubtitle(DrawingHandleScreen handle, float scale)
    {
        var font = SubtitleFontOverride;
        if (font == null || string.IsNullOrEmpty(Subtitle))
            return;

        EnsureSubtitleLayout(handle, font, scale);
        var dimensions = _subtitleDimensions;
        var textPosition = new Vector2(
            (PixelSize.X - dimensions.X) * 0.5f,
            SubtitleCenterY * scale - dimensions.Y * 0.5f);
        var lineThickness = MathF.Max(1f, scale);
        var lineY = textPosition.Y + (dimensions.Y - lineThickness) * 0.5f;
        var lineLength = SubtitleLineLength * scale;
        var lineGap = SubtitleLineGap * scale;

        handle.DrawRect(
            UIBox2.FromDimensions(
                new Vector2(textPosition.X - lineGap - lineLength, lineY),
                new Vector2(lineLength, lineThickness)),
            SubtitleLineColor);
        handle.DrawString(font, textPosition, Subtitle, scale, SubtitleColor);
        handle.DrawRect(
            UIBox2.FromDimensions(
                new Vector2(textPosition.X + dimensions.X + lineGap, lineY),
                new Vector2(lineLength, lineThickness)),
            SubtitleLineColor);
    }

    private void EnsureWordLayout(DrawingHandleScreen handle, Font font, float scale)
    {
        if (ReferenceEquals(_layoutFont, font) && MathF.Abs(_layoutScale - scale) < 0.001f)
            return;

        _layoutFont = font;
        _layoutScale = scale;
        _layoutTotalWidth = 0f;
        _layoutGlyphHeight = 0f;
        for (var i = 0; i < Glyphs.Length; i++)
        {
            var dimensions = handle.GetDimensions(font, Glyphs[i], scale);
            _glyphWidths[i] = dimensions.X;
            _layoutTotalWidth += dimensions.X;
            _layoutGlyphHeight = MathF.Max(_layoutGlyphHeight, dimensions.Y);
        }
    }

    private void EnsureSubtitleLayout(DrawingHandleScreen handle, Font font, float scale)
    {
        if (ReferenceEquals(_subtitleLayoutFont, font)
            && _subtitleLayoutText == Subtitle
            && MathF.Abs(_subtitleLayoutScale - scale) < 0.001f)
        {
            return;
        }

        _subtitleLayoutFont = font;
        _subtitleLayoutText = Subtitle;
        _subtitleLayoutScale = scale;
        _subtitleDimensions = handle.GetDimensions(font, Subtitle, scale);
    }
}
