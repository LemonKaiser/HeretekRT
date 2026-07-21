using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Compact checkbox-and-result glyph for the lobby vote button.
/// </summary>
internal sealed class LobbyVoteIcon : Control
{
    private static readonly Color Ink = Color.FromHex("#E7E0D0");

    public Color Tint { get; set; } = Ink;

    public LobbyVoteIcon()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(26f, 21f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        var boxSize = 9f * scale;
        var markWidth = 12f * scale;
        var totalWidth = boxSize + 4f * scale + markWidth;
        var x = (PixelSize.X - totalWidth) / 2f;
        var y = (PixelSize.Y - (boxSize * 2f + 3f * scale)) / 2f;

        var first = UIBox2.FromDimensions(new Vector2(x, y), new Vector2(boxSize));
        var second = UIBox2.FromDimensions(new Vector2(x, y + boxSize + 3f * scale), new Vector2(boxSize));
        handle.DrawRect(first, Tint, filled: false);
        handle.DrawRect(second, Tint, filled: false);

        var markX = x + boxSize + 4f * scale;
        var tickA = new Vector2(markX, y + boxSize * 0.48f);
        var tickB = new Vector2(markX + 3.5f * scale, y + boxSize);
        var tickC = new Vector2(markX + markWidth, y);
        DrawBoldLine(handle, tickA, tickB, Tint, scale);
        DrawBoldLine(handle, tickB, tickC, Tint, scale);

        var crossTop = y + boxSize + 3f * scale;
        DrawBoldLine(handle, new Vector2(markX, crossTop), new Vector2(markX + markWidth, crossTop + boxSize), Tint, scale);
        DrawBoldLine(handle, new Vector2(markX + markWidth, crossTop), new Vector2(markX, crossTop + boxSize), Tint, scale);
    }

    private static void DrawBoldLine(DrawingHandleScreen handle, Vector2 from, Vector2 to, Color color, float scale)
    {
        handle.DrawLine(from, to, color);
        handle.DrawLine(from + new Vector2(0f, MathF.Max(1f, scale)), to + new Vector2(0f, MathF.Max(1f, scale)), color.WithAlpha(color.A * 0.78f));
    }
}
