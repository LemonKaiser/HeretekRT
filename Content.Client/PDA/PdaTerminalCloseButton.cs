using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.PDA;

public sealed class PdaTerminalCloseButton : BaseButton
{
    private static readonly Color NormalFill = Color.FromHex("#171B18");
    private static readonly Color HoverFill = Color.FromHex("#314237");
    private static readonly Color PressedFill = Color.FromHex("#233128");
    private static readonly Color NormalBorder = Color.FromHex("#82795F");
    private static readonly Color ActiveBorder = Color.FromHex("#88D2A5");
    private static readonly Color NormalGlyph = Color.FromHex("#A8B0A1");
    private static readonly Color ActiveGlyph = Color.FromHex("#D7E0D7");

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        var scale = UIScale;
        var border = MathF.Max(1f, scale);
        var cut = MathF.Min(3f * scale, MathF.Min(box.Width, box.Height) * 0.3f);
        var isActive = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;
        var fill = DrawMode switch
        {
            DrawModeEnum.Hover => HoverFill,
            DrawModeEnum.Pressed => PressedFill,
            _ => NormalFill
        };

        DrawFacet(handle, box, cut, fill, isActive ? ActiveBorder : NormalBorder, border);

        var center = box.Center;
        var arm = MathF.Min(box.Width, box.Height) * 0.22f;
        var glyph = isActive ? ActiveGlyph : NormalGlyph;
        handle.DrawLine(center + new Vector2(-arm, -arm), center + new Vector2(arm, arm), glyph);
        handle.DrawLine(center + new Vector2(-arm, arm), center + new Vector2(arm, -arm), glyph);
    }

    private static void DrawFacet(
        DrawingHandleScreen handle,
        UIBox2 box,
        float cut,
        Color fill,
        Color border,
        float thickness)
    {
        var outer = CreateVertices(box, cut);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, outer, Color.ToSrgb(fill));

        var innerBox = new UIBox2(
            box.Left + thickness,
            box.Top + thickness,
            box.Right - thickness,
            box.Bottom - thickness);
        if (innerBox.Width <= 0f || innerBox.Height <= 0f)
            return;

        var inner = CreateVertices(innerBox, MathF.Max(0f, cut - thickness));
        var vertices = new Vector2[(outer.Length + 1) * 2];
        for (var index = 0; index < outer.Length; index++)
        {
            vertices[index * 2] = outer[index];
            vertices[index * 2 + 1] = inner[index];
        }

        vertices[^2] = outer[0];
        vertices[^1] = inner[0];
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, vertices, Color.ToSrgb(border));
    }

    private static Vector2[] CreateVertices(UIBox2 box, float cut)
    {
        return
        [
            new Vector2(box.Left + cut, box.Top),
            new Vector2(box.Right - cut, box.Top),
            new Vector2(box.Right, box.Top + cut),
            new Vector2(box.Right, box.Bottom - cut),
            new Vector2(box.Right - cut, box.Bottom),
            new Vector2(box.Left + cut, box.Bottom),
            new Vector2(box.Left, box.Bottom - cut),
            new Vector2(box.Left, box.Top + cut)
        ];
    }
}
