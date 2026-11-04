using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.ShuttleStorage;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageBayControl : Control
{
    private static readonly Color Background = Color.FromHex("#191d1b");
    private static readonly Color Grid = Color.FromHex("#30362e");
    private static readonly Color Gold = Color.FromHex("#c9aa66");
    private static readonly Color[] CellColors =
    [Color.FromHex("#595b47"), Color.FromHex("#383d30"), Color.FromHex("#a8a37d"), Color.FromHex("#79a6a0"), Gold];
    private readonly List<Vector2> _grid = new();
    private readonly List<Vector2> _rails = new();
    private readonly List<Vector2> _stripes = new();
    private readonly List<Vector2> _edges = new();
    private readonly List<Vector2> _tileGrid = new();
    private readonly List<Vector2>[] _fills = [new(), new(), new(), new(), new()];
    private readonly Vector2[] _batchBuffer = new Vector2[768];
    private ShuttleStoragePreview? _preview;
    private Vector2 _builtSize;
    private float _builtScale;
    private bool _dirty = true;
    public ShuttleStorageBayControl()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
    }

    public void SetPreview(ShuttleStoragePreview? preview)
    {
        if (preview?.Valid != true)
            preview = null;
        if (ReferenceEquals(preview, _preview) || preview != null && _preview != null &&
            preview.Width == _preview.Width && preview.Height == _preview.Height && preview.Cells.SequenceEqual(_preview.Cells))
            return;
        if (preview?.Width != _preview?.Width || preview?.Height != _preview?.Height)
        {
            _zoom = 1;
            _pan = Vector2.Zero;
        }
        _preview = preview;
        _dirty = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        Vector2 size = PixelSize;
        EnsureGeometry(size);
        handle.DrawRect(PixelSizeBox, Background);
        Batch(handle, _grid, Grid, DrawPrimitiveTopology.LineList);
        Batch(handle, _rails, Color.FromHex("#5b604e"), DrawPrimitiveTopology.LineList);
        Batch(handle, _stripes, Gold.WithAlpha(0.65f), DrawPrimitiveTopology.TriangleList);
        for (var i = 0; i < _fills.Length; i++)
            Batch(handle, _fills[i], CellColors[i], DrawPrimitiveTopology.TriangleList);
        Batch(handle, _tileGrid, Grid.WithAlpha(0.6f), DrawPrimitiveTopology.LineList);
        Batch(handle, _edges, Gold, DrawPrimitiveTopology.LineList);
    }

    private void EnsureGeometry(Vector2 size)
    {
        if (_dirty || _builtSize != size || _builtScale != UIScale)
            BuildGeometry(size);
    }

    private void BuildGeometry(Vector2 size)
    {
        _builtSize = size;
        _builtScale = UIScale;
        _dirty = false;
        _grid.Clear(); _rails.Clear(); _stripes.Clear(); _edges.Clear(); _tileGrid.Clear();
        foreach (var fill in _fills) fill.Clear();
        if (size.X < 1 || size.Y < 1)
            return;
        var scale = UIScale;
        var step = Math.Max(16, 28 * scale);
        for (var x = 0f; x < size.X; x += step) Line(_grid, x, 0, x, size.Y);
        for (var y = 0f; y < size.Y; y += step) Line(_grid, 0, y, size.X, y);
        for (var y = 0f; y < size.Y; y += 22 * scale)
        {
            foreach (var x in new[] { 0f, size.X - 7 * scale })
            {
                _stripes.Add(new(x, y)); _stripes.Add(new(x + 7 * scale, y + 7 * scale)); _stripes.Add(new(x + 7 * scale, y + 18 * scale));
                _stripes.Add(new(x, y)); _stripes.Add(new(x + 7 * scale, y + 18 * scale)); _stripes.Add(new(x, y + 11 * scale));
            }
        }
        var top = Math.Min(65 * scale, size.Y * 0.24f);
        var bottom = size.Y - Math.Min(36 * scale, size.Y * 0.17f);
        _contentTop = top;
        _contentBottom = bottom;
        var left = size.X * 0.15f;
        var right = size.X * 0.85f;
        _contentLeft = left;
        _contentRight = right;
        for (var x = left; x < right; x += 12 * scale)
        {
            Line(_rails, x, top, Math.Min(right, x + 6 * scale), top);
            Line(_rails, x, bottom, Math.Min(right, x + 6 * scale), bottom);
        }
        for (var y = top; y < bottom; y += 12 * scale)
        {
            Line(_rails, left, y, left, Math.Min(bottom, y + 6 * scale));
            Line(_rails, right, y, right, Math.Min(bottom, y + 6 * scale));
        }
        foreach (var railX in new[] { size.X * 0.09f, size.X * 0.91f })
        {
            Line(_rails, railX, top + 12 * scale, railX, bottom - 12 * scale);
            Line(_rails, railX + 8 * scale, top + 12 * scale, railX + 8 * scale, bottom - 12 * scale);
            for (var y = top + 12 * scale; y < bottom - 12 * scale; y += 30 * scale)
                Line(_rails, railX, y, railX + 8 * scale, y);
        }
        if (_preview is not { Valid: true } preview)
            return;
        var cell = Math.Min((right - left - 18 * scale) / preview.Width, (bottom - top - 16 * scale) / preview.Height) * _zoom;
        if (cell <= 0)
            return;
        _viewportCenter = new(size.X / 2, (top + bottom) / 2);
        var origin = _viewportCenter - new Vector2(preview.Width, preview.Height) * cell / 2 + _pan;
        for (var y = 0; y < preview.Height; y++)
        for (var x = 0; x < preview.Width; x++)
        {
            var value = (ShuttleStorageCell) preview.Cells[y * preview.Width + x];
            if (value == ShuttleStorageCell.Empty)
                continue;
            var px = origin.X + x * cell;
            var py = origin.Y + (preview.Height - y - 1) * cell;
            if (px + cell < left || py + cell < top || px > right || py > bottom)
                continue;
            var color = (value & ShuttleStorageCell.Door) != 0 ? 4 : (value & ShuttleStorageCell.Window) != 0 ? 3 :
                (value & ShuttleStorageCell.Wall) != 0 ? 2 : (value & ShuttleStorageCell.Grating) != 0 ? 1 : 0;
            Rect(_fills[color], Math.Max(px, left), Math.Max(py, top),
                Math.Min(px + cell, right), Math.Min(py + cell, bottom));
            if (cell > 5 * scale)
            {
                ShipLine(_tileGrid, px, py, px + cell, py);
                ShipLine(_tileGrid, px, py, px, py + cell);
            }
            bool Empty(int nx, int ny) => nx < 0 || ny < 0 || nx >= preview.Width || ny >= preview.Height ||
                preview.Cells[ny * preview.Width + nx] == 0;
            if (Empty(x, y + 1)) ShipLine(_edges, px, py, px + cell, py);
            if (Empty(x, y - 1)) ShipLine(_edges, px, py + cell, px + cell, py + cell);
            if (Empty(x - 1, y)) ShipLine(_edges, px, py, px, py + cell);
            if (Empty(x + 1, y)) ShipLine(_edges, px + cell, py, px + cell, py + cell);
        }
    }

    private void ShipLine(List<Vector2> vertices, float x, float y, float endX, float endY)
    {
        var left = _contentLeft;
        var right = _contentRight;
        if (Math.Max(x, endX) < left || Math.Min(x, endX) > right ||
            Math.Max(y, endY) < _contentTop || Math.Min(y, endY) > _contentBottom)
            return;
        Line(vertices, Math.Clamp(x, left, right), Math.Clamp(y, _contentTop, _contentBottom),
            Math.Clamp(endX, left, right), Math.Clamp(endY, _contentTop, _contentBottom));
    }

    private static void Line(List<Vector2> vertices, float x, float y, float endX, float endY)
    {
        vertices.Add(new(x, y)); vertices.Add(new(endX, endY));
    }

    private static void Rect(List<Vector2> vertices, float x, float y, float right, float bottom)
    {
        vertices.Add(new(x, y)); vertices.Add(new(right, y)); vertices.Add(new(right, bottom));
        vertices.Add(new(x, y)); vertices.Add(new(right, bottom)); vertices.Add(new(x, bottom));
    }

    private void Batch(DrawingHandleScreen handle, List<Vector2> vertices, Color color, DrawPrimitiveTopology type)
    {
        // The native Vector2 overload uses stackalloc: cap each call, including a full 64 × 96 checkerboard.
        for (var start = 0; start < vertices.Count; start += 768)
        {
            var count = Math.Min(768, vertices.Count - start);
            vertices.CopyTo(start, _batchBuffer, 0, count);
            // Match StyleBox/DrawRect brightness, as in DialogueCutCornerStyleBox.
            handle.DrawPrimitives(type, _batchBuffer.AsSpan(0, count), Color.ToSrgb(color));
        }
    }
}
