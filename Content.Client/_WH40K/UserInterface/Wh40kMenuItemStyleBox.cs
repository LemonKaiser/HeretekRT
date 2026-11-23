using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.UserInterface;

internal sealed class Wh40kMenuItemStyleBox : StyleBox
{
    internal const int ContextVerticalPadding = 4;
    private readonly StyleBox? _background;

    internal Wh40kMenuItemStyleBox(bool active, bool danger, int verticalPadding)
    {
        SetContentMarginOverride(Margin.Horizontal, 9);
        SetContentMarginOverride(Margin.Vertical, verticalPadding);
        if (active || danger)
            _background = Wh40kInterfaceTheme.Surface(danger ? "#3D1B14" : "#392B1C", "#56452D", 0, 0);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        _background?.Draw(handle, box, uiScale);
    }
}
