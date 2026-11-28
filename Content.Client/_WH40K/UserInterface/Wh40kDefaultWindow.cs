using System.Numerics;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.UserInterface;

/// <summary>Content-owned window using the full height of the decorated header as its drag target.</summary>
[Virtual]
public class Wh40kDefaultWindow : DefaultWindow
{
    protected override DragMode GetDragModeFor(Vector2 position)
    {
        var mode = base.GetDragModeFor(position);
        if (mode == DragMode.None && position.Y < FindControl<PanelContainer>("WindowHeader").Height)
            return DragMode.Move;
        return mode;
    }
}
