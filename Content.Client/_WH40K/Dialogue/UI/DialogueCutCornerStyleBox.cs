using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Dialogue.UI;

/// <summary>
/// Flat dialogue surface with chamfered corners. It gives dialogue controls a
/// distinct noospheric-panel silhouette without relying on raster assets.
/// </summary>
internal sealed class DialogueCutCornerStyleBox : StyleBox
{
    public Color BackgroundColor { get; set; }
    public Color BorderColor { get; set; }
    public Thickness BorderThickness { get; set; }
    public float CornerCut { get; set; } = 10f;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var thickness = BorderThickness.Scale(uiScale);
        var cut = MathF.Min(CornerCut * uiScale, MathF.Min(box.Width, box.Height) * 0.45f);
        var outer = CreateVertices(box, cut);

        // DrawPrimitives internally converts supplied colours from sRGB to
        // linear space. Dialogue palettes are authored like ordinary UI
        // StyleBox/DrawRect colours, which travel through the non-linear path.
        // Pre-convert here so both rendering paths have identical perceived
        // brightness; without this, e.g. #B17C35 became roughly #703309.
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, outer, Color.ToSrgb(BackgroundColor));

        if (thickness.Left <= 0f
            && thickness.Top <= 0f
            && thickness.Right <= 0f
            && thickness.Bottom <= 0f)
        {
            return;
        }

        var innerBox = thickness.Deflate(box);
        if (innerBox.Width <= 0f || innerBox.Height <= 0f)
            return;

        var innerCut = MathF.Max(0f, cut - MathF.Max(MathF.Max(thickness.Left, thickness.Right), MathF.Max(thickness.Top, thickness.Bottom)));
        var inner = CreateVertices(innerBox, innerCut);
        var borderVertices = new Vector2[(outer.Length + 1) * 2];

        for (var i = 0; i < outer.Length; i++)
        {
            borderVertices[i * 2] = outer[i];
            borderVertices[i * 2 + 1] = inner[i];
        }

        borderVertices[^2] = outer[0];
        borderVertices[^1] = inner[0];
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, borderVertices, Color.ToSrgb(BorderColor));
    }

    protected override float GetDefaultContentMargin(Margin margin)
    {
        return margin switch
        {
            Margin.Top => BorderThickness.Top,
            Margin.Bottom => BorderThickness.Bottom,
            Margin.Left => BorderThickness.Left,
            Margin.Right => BorderThickness.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(margin), margin, null)
        };
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
