using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Mapping;

/// <summary>Measures the complete result set while keeping only the visible cards in the UI tree.</summary>
internal sealed class Wh40kMappingGrid : Container
{
    private const float Gap = 4;
    private const float MinimumCardWidth = 107;
    private const float CardHeight = 104;
    public const float RowHeight = CardHeight + Gap;

    private int _count;
    public int Columns { get; private set; } = 1;
    public int FirstRow { get; set; }
    public float ContentHeight => Math.Max(0, ((_count + Columns - 1) / Columns) * RowHeight - Gap);

    public int Count
    {
        get => _count;
        set
        {
            if (_count == value)
                return;
            _count = value;
            InvalidateMeasure();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 available)
    {
        var width = float.IsFinite(available.X) ? available.X : MinimumCardWidth;
        Columns = Math.Max(1, (int) ((width + Gap) / (MinimumCardWidth + Gap)));
        var cell = new Vector2(CardWidth(width), CardHeight);
        foreach (var child in Children)
            child.Measure(cell);
        return new Vector2(width, ContentHeight);
    }

    protected override Vector2 ArrangeOverride(Vector2 size)
    {
        var width = CardWidth(size.X);
        var index = 0;
        foreach (var child in Children)
        {
            child.Arrange(UIBox2.FromDimensions(
                index % Columns * (width + Gap), (FirstRow + index / Columns) * RowHeight, width, CardHeight));
            index++;
        }
        return size;
    }

    private float CardWidth(float width) => Math.Max(0, (width - (Columns - 1) * Gap) / Columns);
}
