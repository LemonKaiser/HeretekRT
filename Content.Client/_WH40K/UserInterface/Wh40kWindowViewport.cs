using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.UserInterface;

/// <summary>Keeps large device layouts scrollable inside a window that fits the current UI viewport.</summary>
internal sealed class Wh40kWindowViewport : ScrollContainer
{
    private readonly FancyWindow _window;
    private readonly Vector2 _minimum;
    private readonly Vector2 _preferred;

    internal Wh40kWindowViewport(FancyWindow window)
    {
        _window = window;
        _minimum = window.MinSize;
        _preferred = window.DesiredSize;
        HorizontalExpand = true;
        VerticalExpand = true;
        ReturnMeasure = false;
        var body = new Control { MinSize = Vector2.Max(Vector2.Zero, _preferred - new Vector2(20, 30)) };
        foreach (var child in window.ContentsContainer.Children.ToArray())
        {
            window.ContentsContainer.RemoveChild(child);
            body.AddChild(child);
        }
        AddChild(body);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        UserInterfaceManager.RootControl.OnResized += UpdateBounds;
        UpdateBounds();
    }

    protected override void ExitedTree()
    {
        UserInterfaceManager.RootControl.OnResized -= UpdateBounds;
        base.ExitedTree();
    }

    internal static Vector2 AvailableSize(Vector2 rootSize)
    {
        return Vector2.Max(new Vector2(120), rootSize - new Vector2(40));
    }

    internal static void ClampInitialSize(FancyWindow window, Vector2 rootSize)
    {
        var available = AvailableSize(rootSize);
        if (available.X >= window.MinWidth && available.Y >= window.MinHeight)
            window.SetSize = Vector2.Min(window.SetSize, available);
    }

    private void UpdateBounds()
    {
        if (_window.Parent is OSWindow)
        {
            _window.MaxSize = new Vector2(float.PositiveInfinity);
            return;
        }
        var available = AvailableSize(UserInterfaceManager.RootControl.Size);
        _window.MinSize = Vector2.Min(_minimum, available);
        _window.MaxSize = available;
        var requested = _window.SetSize;
        _window.SetSize = Vector2.Min(new Vector2(
            float.IsFinite(requested.X) ? requested.X : _preferred.X,
            float.IsFinite(requested.Y) ? requested.Y : _preferred.Y), available);
        _window.Measure(available);
    }
}
