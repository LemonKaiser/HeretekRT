using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Lightweight rounded surface used by the lobby utility controls.
/// It intentionally lives with the lobby rather than borrowing an unrelated UI implementation.
/// </summary>
internal sealed class LobbyRoundedStyleBox : StyleBox
{
    private const int ArcSegments = 12;
    private const int ContourVertexCount = (ArcSegments + 1) * 4;
    private static readonly Vector2[] UnitContour = CreateUnitContour();

    // These buffers are reused by this stylebox's serial UI draw calls. Keeping
    // them on the instance avoids per-frame allocations without using Span/
    // stackalloc instructions that the client's IL verifier rejects.
    private readonly Vector2[] _outerContour = new Vector2[ContourVertexCount];
    private readonly Vector2[] _innerContour = new Vector2[ContourVertexCount];
    private readonly Vector2[] _borderVertices = new Vector2[(ContourVertexCount + 1) * 2];
    private readonly Vector2[] _fillVertices = new Vector2[ContourVertexCount + 2];
    private readonly Vector2[] _shadowContour = new Vector2[ContourVertexCount];
    private readonly Vector2[] _shadowFillVertices = new Vector2[ContourVertexCount + 2];

    public Color BackgroundColor { get; set; }
    public Color BorderColor { get; set; }
    /// <summary>Subtle lighting and depth for utility buttons without changing their bounds.</summary>
    public Color HighlightColor { get; set; } = Color.Transparent;
    public Color ShadowColor { get; set; } = Color.Transparent;
    public Thickness BorderThickness { get; set; }
    public float CornerRadius { get; set; }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        // Allow the footer links to be true circles while keeping ordinary utility
        // controls at their explicitly requested rounded-square radius.
        var radius = MathF.Min(CornerRadius * uiScale, MathF.Min(box.Width, box.Height) * 0.5f);
        var thickness = BorderThickness.Scale(uiScale);

        if (radius <= 0f)
        {
            DrawRect(handle, box, thickness);
            return;
        }

        DrawShadow(handle, box, radius, uiScale);

        FillVertices(box, radius, _outerContour);
        DrawRoundedFill(handle, box.Center, _outerContour, _fillVertices, BackgroundColor);

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

        var innerRadius = MathF.Max(0f, radius - MathF.Max(thickness.Left, thickness.Top));
        FillVertices(innerBox, innerRadius, _innerContour);

        for (var i = 0; i < _outerContour.Length; i++)
        {
            _borderVertices[i * 2] = _outerContour[i];
            _borderVertices[i * 2 + 1] = _innerContour[i];
        }

        _borderVertices[^2] = _outerContour[0];
        _borderVertices[^1] = _innerContour[0];
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _borderVertices, BorderColor);

        DrawHighlight(handle, box, radius, uiScale);
    }

    private void DrawRect(DrawingHandleScreen handle, UIBox2 box, Thickness thickness)
    {
        if (thickness.Left > 0f)
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + thickness.Left, box.Bottom), BorderColor);
        if (thickness.Top > 0f)
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + thickness.Top), BorderColor);
        if (thickness.Right > 0f)
            handle.DrawRect(new UIBox2(box.Right - thickness.Right, box.Top, box.Right, box.Bottom), BorderColor);
        if (thickness.Bottom > 0f)
            handle.DrawRect(new UIBox2(box.Left, box.Bottom - thickness.Bottom, box.Right, box.Bottom), BorderColor);

        handle.DrawRect(thickness.Deflate(box), BackgroundColor);
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

    private void DrawShadow(DrawingHandleScreen handle, UIBox2 box, float radius, float uiScale)
    {
        if (ShadowColor.A <= 0f)
            return;

        // A stack of low-opacity offsets creates depth while avoiding the rigid,
        // single inset-looking outline the earlier implementation produced.
        for (var layer = 4; layer >= 1; layer--)
        {
            var offset = layer * 1.45f * uiScale;
            var expansion = layer * 0.55f * uiScale;
            var shadowBox = new UIBox2(
                box.Left - expansion,
                box.Top + offset - expansion * 0.35f,
                box.Right + expansion,
                box.Bottom + offset + expansion);
            FillVertices(shadowBox, radius + expansion, _shadowContour);
            var alpha = ShadowColor.A * (0.045f + (5f - layer) * 0.045f);
            DrawRoundedFill(
                handle,
                shadowBox.Center,
                _shadowContour,
                _shadowFillVertices,
                ShadowColor.WithAlpha(alpha));
        }
    }

    private void DrawHighlight(DrawingHandleScreen handle, UIBox2 box, float radius, float uiScale)
    {
        if (HighlightColor.A <= 0f || box.Width <= radius * 1.5f)
            return;

        var inset = MathF.Max(uiScale, radius * 0.55f);
        var width = MathF.Max(0f, box.Width - inset * 2f);
        if (width <= 0f)
            return;

        handle.DrawRect(
            UIBox2.FromDimensions(
                new Vector2(box.Left + inset, box.Top + uiScale),
                new Vector2(width, MathF.Max(1f, uiScale))),
            HighlightColor);
    }

    private static void DrawRoundedFill(
        DrawingHandleScreen handle,
        Vector2 center,
        Vector2[] contour,
        Vector2[] fill,
        Color color)
    {
        fill[0] = center;
        Array.Copy(contour, 0, fill, 1, contour.Length);
        fill[^1] = contour[0];
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, fill, color);
    }

    private static void FillVertices(UIBox2 box, float radius, Vector2[] vertices)
    {
        var topLeft = new Vector2(box.Left + radius, box.Top + radius);
        var topRight = new Vector2(box.Right - radius, box.Top + radius);
        var bottomRight = new Vector2(box.Right - radius, box.Bottom - radius);
        var bottomLeft = new Vector2(box.Left + radius, box.Bottom - radius);
        for (var index = 0; index < UnitContour.Length; index++)
        {
            var centre = index switch
            {
                <= ArcSegments => topLeft,
                <= (ArcSegments + 1) * 2 - 1 => topRight,
                <= (ArcSegments + 1) * 3 - 1 => bottomRight,
                _ => bottomLeft,
            };
            vertices[index] = centre + UnitContour[index] * radius;
        }
    }

    private static Vector2[] CreateUnitContour()
    {
        var contour = new Vector2[ContourVertexCount];
        var index = 0;
        AddUnitArc(contour, ref index, MathF.PI, MathF.PI * 1.5f);
        AddUnitArc(contour, ref index, MathF.PI * 1.5f, MathF.PI * 2f);
        AddUnitArc(contour, ref index, 0f, MathF.PI * 0.5f);
        AddUnitArc(contour, ref index, MathF.PI * 0.5f, MathF.PI);
        return contour;
    }

    private static void AddUnitArc(Vector2[] vertices, ref int index, float from, float to)
    {
        for (var step = 0; step <= ArcSegments; step++)
        {
            var angle = from + (to - from) * step / ArcSegments;
            vertices[index++] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
    }
}
