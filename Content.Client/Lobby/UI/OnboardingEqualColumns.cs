using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Arranges the onboarding ability summaries into equal-width columns.
/// This prevents a long first summary from consuming the second column's space.
/// </summary>
public sealed class OnboardingEqualColumns : Container
{
    public float Separation { get; set; } = 24f;
    public float MinimumColumnWidth { get; set; } = 280f;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var children = GetVisibleChildren();
        if (children.Count == 0)
            return Vector2.Zero;

        var stack = ShouldStack(availableSize.X, children.Count);
        var measureWidth = stack
            ? availableSize.X
            : MathF.Max(0f, (availableSize.X - Separation * (children.Count - 1)) / children.Count);
        var height = 0f;
        var width = 0f;
        foreach (var child in children)
        {
            child.Measure(new Vector2(measureWidth, availableSize.Y));
            height = stack ? height + child.DesiredSize.Y : MathF.Max(height, child.DesiredSize.Y);
            width = MathF.Max(width, child.DesiredSize.X);
        }

        if (stack && children.Count > 1)
            height += Separation * (children.Count - 1);

        // ScrollContainer measures content with an infinite horizontal size before arranging it in
        // the viewport. Returning infinity here made one column claim all remaining width and crush
        // the other one into a few glyphs.
        return new Vector2(float.IsFinite(availableSize.X) ? availableSize.X : width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var children = GetVisibleChildren();
        if (children.Count == 0)
            return finalSize;

        var stack = ShouldStack(finalSize.X, children.Count);
        var columnWidth = stack
            ? finalSize.X
            : MathF.Max(0f, (finalSize.X - Separation * (children.Count - 1)) / children.Count);
        var offset = 0f;
        foreach (var child in children)
        {
            var height = stack ? child.DesiredSize.Y : finalSize.Y;
            var position = stack ? new Vector2(0f, offset) : new Vector2(offset, 0f);
            child.Arrange(UIBox2.FromDimensions(position, new Vector2(columnWidth, height)));
            offset += (stack ? height : columnWidth) + Separation;
        }

        return finalSize;
    }

    private bool ShouldStack(float availableWidth, int childCount)
    {
        return !float.IsFinite(availableWidth) ||
               (availableWidth - Separation * (childCount - 1)) / childCount < MinimumColumnWidth;
    }

    private List<Control> GetVisibleChildren()
    {
        var children = new List<Control>(ChildCount);
        for (var i = 0; i < ChildCount; i++)
        {
            var child = Children[i];
            if (child.Visible)
                children.Add(child);
        }

        return children;
    }
}
