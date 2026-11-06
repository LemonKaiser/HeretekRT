using System.Numerics;
using System.Text;
using Robust.Client.Graphics;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

/// <summary>
/// Changes glyph rendering while preserving the active font's measurements and native rich-text wrapping.
/// </summary>
internal abstract class ChatFont : Font
{
    protected readonly Font BaseFont;

    protected ChatFont(Font baseFont)
    {
        BaseFont = baseFont;
    }

    public override int GetAscent(float scale) => BaseFont.GetAscent(scale);
    public override int GetHeight(float scale) => BaseFont.GetHeight(scale);
    public override int GetDescent(float scale) => BaseFont.GetDescent(scale);
    public override int GetLineHeight(float scale) => BaseFont.GetLineHeight(scale);
    public override CharMetrics? GetCharMetrics(Rune rune, float scale, bool fallback = true)
        => BaseFont.GetCharMetrics(rune, scale, fallback);
}

internal sealed class ChatImageFont : ChatFont
{
    // Object replacement character: rich text measures and wraps the image as a normal glyph.
    public const string Glyph = "\uFFFC";

    private readonly Texture _texture;
    private readonly bool _hidden;

    public ChatImageFont(Font baseFont, Texture texture, bool hidden = false) : base(baseFont)
    {
        _texture = texture;
        _hidden = hidden;
    }

    public override CharMetrics? GetCharMetrics(Rune rune, float scale, bool fallback = true)
    {
        if (rune.Value != 0xFFFC)
            return base.GetCharMetrics(rune, scale, fallback);

        var size = GetHeight(scale);
        return new CharMetrics(0, GetAscent(scale), size + (int) MathF.Ceiling(2 * scale), size, size);
    }

    public override float DrawChar(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale,
        Color color, bool fallback = true)
    {
        if (rune.Value != 0xFFFC)
            return BaseFont.DrawChar(handle, rune, baseline, scale, color, fallback);

        var size = GetHeight(scale);
        if (!_hidden)
        {
            var position = baseline + new Vector2(scale, -GetAscent(scale));
            var tint = Color.White.WithAlpha(color.A);
            if (handle is DrawingHandleScreen screen)
                screen.DrawTextureRect(_texture, UIBox2.FromDimensions(position, new Vector2(size)), tint);
            else if (handle is DrawingHandleWorld world)
                world.DrawTextureRect(_texture, Box2.FromDimensions(position, new Vector2(size)), tint);
        }

        return size + (int) MathF.Ceiling(2 * scale);
    }
}
