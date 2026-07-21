using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Single, legible speech-bubble glyph for the lobby drawer toggle.
/// </summary>
internal sealed class LobbyChatIcon : Control
{
    private static readonly Color Ink = Color.FromHex("#E7E0D0");

    public Color Tint { get; set; } = Ink;

    public LobbyChatIcon()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(24f, 20f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        var width = 22f * scale;
        var height = 15f * scale;
        var left = (PixelSize.X - width) / 2f;
        var top = (PixelSize.Y - height) / 2f - scale;
        var corner = 2f * scale;

        // A rectangle with clipped corners is deliberately used here instead of an
        // un-centred triangle fan: the old fan produced diagonal artefacts in some
        // renderer backends.  The tail is a separate, correctly centred fan.
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(left + corner, top), new Vector2(width - corner * 2f, height)),
            Tint);
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(left, top + corner), new Vector2(width, height - corner * 2f)),
            Tint);

        var tailTop = new Vector2(left + width * 0.42f, top + height - corner);
        var tailBottom = new Vector2(left + width * 0.32f, top + height + 4f * scale);
        var tailRight = new Vector2(left + width * 0.55f, top + height - corner);
        var tailCenter = (tailTop + tailBottom + tailRight) / 3f;
        var tail = new[] { tailCenter, tailTop, tailBottom, tailRight, tailTop };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, tail, Tint);
    }
}
