using Content.Shared._WH40K.ItemRarity.Prototypes;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.ItemRarity;

/// <summary>
/// Code-only pixel painter for rarity frames and badges. Keeping the drawing
/// procedural avoids texture filtering and makes every rectangle resize safely.
/// </summary>
internal static class ItemRarityPainter
{
    private static readonly Color MetalDark = Color.FromHex("#252D35");
    private static readonly Color Metal = Color.FromHex("#66717C");
    private static readonly Color MetalLight = Color.FromHex("#B7C0C8");
    private static readonly Color Panel = Color.FromHex("#0C1219");

    public static void DrawFrame(
        DrawingHandleScreen handle,
        UIBox2 area,
        float uiScale,
        ItemRarityPrototype rarity)
    {
        if (area.Width < 4 || area.Height < 4)
            return;

        var pixel = MathF.Max(1f, MathF.Round(uiScale));
        var border = pixel * 2f;
        var left = MathF.Round(area.Left);
        var top = MathF.Round(area.Top);
        var right = MathF.Round(area.Right);
        var bottom = MathF.Round(area.Bottom);
        var corner = MathF.Min(12f * pixel, MathF.Min(area.Width, area.Height) * 0.32f);

        // Keep the frame visible at a glance. The corner ornaments add detail,
        // but they must not be the only thing distinguishing a rare item from
        // an ordinary slot.
        DrawRect(handle, left, top, right, top + border, MetalDark.WithAlpha(0.96f));
        DrawRect(handle, left, bottom - border, right, bottom, MetalDark.WithAlpha(0.96f));
        DrawRect(handle, left, top, left + border, bottom, MetalDark.WithAlpha(0.96f));
        DrawRect(handle, right - border, top, right, bottom, MetalDark.WithAlpha(0.96f));
        DrawRect(handle, left, top, right, top + pixel, MetalLight.WithAlpha(0.52f));
        DrawRect(handle, left, top, left + pixel, bottom, Metal.WithAlpha(0.72f));

        var innerLeft = left + pixel;
        var innerTop = top + pixel;
        var innerRight = right - pixel;
        var innerBottom = bottom - pixel;
        var frameColor = rarity.Color.WithAlpha(0.88f);
        DrawRect(handle, innerLeft, innerTop, innerRight, innerTop + pixel, frameColor);
        DrawRect(handle, innerLeft, innerBottom - pixel, innerRight, innerBottom, frameColor);
        DrawRect(handle, innerLeft, innerTop, innerLeft + pixel, innerBottom, frameColor);
        DrawRect(handle, innerRight - pixel, innerTop, innerRight, innerBottom, frameColor);

        DrawCorner(handle, left, top, corner, pixel, rarity, false, false);
        DrawCorner(handle, right, top, corner, pixel, rarity, true, false);
        DrawCorner(handle, left, bottom, corner, pixel, rarity, false, true);
        DrawCorner(handle, right, bottom, corner, pixel, rarity, true, true);
    }

    /// <summary>
    /// Draws only the outer edges of one storage-grid cell. Adjacent cells do
    /// not draw their common edge, creating one contour that follows the item
    /// shape instead of the rectangular bounding box.
    /// </summary>
    public static void DrawStorageCellFrame(
        DrawingHandleScreen handle,
        UIBox2 area,
        float uiScale,
        ItemRarityPrototype rarity,
        bool top,
        bool bottom,
        bool left,
        bool right)
    {
        var pixel = MathF.Max(1f, MathF.Round(uiScale));
        var leftEdge = MathF.Round(area.Left);
        var topEdge = MathF.Round(area.Top);
        var rightEdge = MathF.Round(area.Right);
        var bottomEdge = MathF.Round(area.Bottom);

        if (top)
            DrawStorageEdge(handle, leftEdge, topEdge, rightEdge, topEdge + pixel * 2f, rarity, horizontal: true);
        if (bottom)
            DrawStorageEdge(handle, leftEdge, bottomEdge - pixel * 2f, rightEdge, bottomEdge, rarity, horizontal: true);
        if (left)
            DrawStorageEdge(handle, leftEdge, topEdge, leftEdge + pixel * 2f, bottomEdge, rarity, horizontal: false);
        if (right)
            DrawStorageEdge(handle, rightEdge - pixel * 2f, topEdge, rightEdge, bottomEdge, rarity, horizontal: false);
    }

    public static void DrawBadge(
        DrawingHandleScreen handle,
        UIBox2 area,
        float uiScale,
        ItemRarityPrototype rarity)
    {
        if (area.Width < 8 || area.Height < 6)
            return;

        var pixel = MathF.Max(1f, MathF.Round(uiScale));
        var center = area.Center;
        var iconSize = MathF.Min(area.Height - pixel * 2f, area.Width - pixel * 2f);
        var left = MathF.Round(center.X - iconSize * 0.5f);
        var top = MathF.Round(center.Y - iconSize * 0.5f);
        var right = left + MathF.Round(iconSize);
        var bottom = top + MathF.Round(iconSize);
        // The badge is intentionally quiet: at 24x20 a layered seal and a
        // crystal/rosette lose their silhouette. A single dark plate, one
        // rarity stripe and a 1..6 pip grid remain readable at every scale.
        DrawRect(handle, left, top, right, bottom, Panel.WithAlpha(0.96f));
        DrawRect(handle, left, top, right, top + pixel, MetalDark.WithAlpha(0.96f));
        DrawRect(handle, left, bottom - pixel, right, bottom, MetalDark.WithAlpha(0.96f));
        DrawRect(handle, left, top + pixel, left + pixel, bottom - pixel, rarity.Color.WithAlpha(0.9f));
        DrawRect(handle, right - pixel, top + pixel, right, bottom - pixel, Metal.WithAlpha(0.55f));

        var pipUnit = MathF.Max(pixel, MathF.Round(iconSize * 0.13f));
        DrawTierPips(handle, center.X + pixel * 0.45f, center.Y, pipUnit, rarity);
    }

    private static void DrawTierPips(
        DrawingHandleScreen handle,
        float centerX,
        float centerY,
        float unit,
        ItemRarityPrototype rarity)
    {
        var count = Math.Clamp((int) rarity.Tier, 1, 6);
        var gap = MathF.Max(unit, MathF.Round(unit * 0.7f));
        var columns = 3;
        var rows = 2;
        var width = columns * unit + (columns - 1) * gap;
        var height = rows * unit + (rows - 1) * gap;
        var left = MathF.Round(centerX - width * 0.5f);
        var top = MathF.Round(centerY - height * 0.5f);

        for (var index = 0; index < columns * rows; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var x = left + column * (unit + gap);
            var y = top + row * (unit + gap);
            var color = index < count
                ? rarity.AccentColor.WithAlpha(0.96f)
                : Metal.WithAlpha(0.34f);

            DrawRect(handle, x, y, x + unit, y + unit, color);
        }
    }

    private static void DrawCorner(
        DrawingHandleScreen handle,
        float edgeX,
        float edgeY,
        float corner,
        float pixel,
        ItemRarityPrototype rarity,
        bool right,
        bool bottom)
    {
        // Describe one top-left corner in local coordinates pointing inwards,
        // then mirror those coordinates for the other three corners. Merely
        // moving the old top-left geometry left the right/bottom shapes uneven.
        DrawCornerRect(handle, edgeX, edgeY, 0f, 0f, corner, pixel, right, bottom, MetalDark.WithAlpha(0.96f));
        DrawCornerRect(handle, edgeX, edgeY, 0f, 0f, pixel, corner, right, bottom, MetalDark.WithAlpha(0.96f));
        DrawCornerRect(handle, edgeX, edgeY, pixel, pixel, corner - pixel, pixel * 2f, right, bottom, MetalLight.WithAlpha(0.82f));
        DrawCornerRect(handle, edgeX, edgeY, pixel, pixel, pixel * 2f, corner - pixel, right, bottom, Metal.WithAlpha(0.82f));

        // The colored inner L is deliberately longer than the plain rail: it
        // makes the corner plates readable even for neutral stamped quality.
        var accent = rarity.Color.WithAlpha(0.98f);
        var armStart = pixel * 2f;
        var armEnd = corner - pixel;
        DrawCornerRect(handle, edgeX, edgeY, armStart, armStart, armEnd, armStart + pixel, right, bottom, accent);
        DrawCornerRect(handle, edgeX, edgeY, armStart, armStart, armStart + pixel, armEnd, right, bottom, accent);

        var rivet = corner * 0.5f;
        DrawCornerRect(handle, edgeX, edgeY, rivet - pixel, rivet - pixel, rivet + pixel, rivet + pixel, right, bottom, MetalLight.WithAlpha(0.9f));
        DrawCornerRect(handle, edgeX, edgeY, rivet, rivet, rivet + pixel, rivet + pixel, right, bottom, rarity.AccentColor.WithAlpha(0.98f));
    }

    private static void DrawCornerRect(
        DrawingHandleScreen handle,
        float edgeX,
        float edgeY,
        float localLeft,
        float localTop,
        float localRight,
        float localBottom,
        bool mirrorX,
        bool mirrorY,
        Color color)
    {
        var x0 = edgeX + (mirrorX ? -localLeft : localLeft);
        var x1 = edgeX + (mirrorX ? -localRight : localRight);
        var y0 = edgeY + (mirrorY ? -localTop : localTop);
        var y1 = edgeY + (mirrorY ? -localBottom : localBottom);

        DrawRect(handle, MathF.Min(x0, x1), MathF.Min(y0, y1), MathF.Max(x0, x1), MathF.Max(y0, y1), color);
    }

    private static void DrawStorageEdge(
        DrawingHandleScreen handle,
        float left,
        float top,
        float right,
        float bottom,
        ItemRarityPrototype rarity,
        bool horizontal)
    {
        DrawRect(handle, left, top, right, bottom, Panel.WithAlpha(0.9f));

        if (horizontal)
            DrawRect(handle, left, top, right, top + MathF.Max(1f, (bottom - top) * 0.5f), rarity.Color.WithAlpha(0.86f));
        else
            DrawRect(handle, left, top, left + MathF.Max(1f, (right - left) * 0.5f), bottom, rarity.Color.WithAlpha(0.86f));
    }

    private static void DrawRect(DrawingHandleScreen handle, float left, float top, float right, float bottom, Color color)
    {
        handle.DrawRect(new UIBox2(left, top, right, bottom), color);
    }
}
