using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.UserInterface;

internal sealed class Wh40kHudButtonStyleBox : StyleBox
{
    // Ten permanently visible HUD buttons do not need forty separate shadow primitives each frame.
    private readonly Wh40kWindowStyleBox _material = new(shadow: false);
    private readonly StyleBox? _highlight;

    internal Wh40kHudButtonStyleBox(bool hover, bool pressed)
    {
        SetContentMarginOverride(Margin.All, 4);
        if (hover || pressed)
            _highlight = Wh40kInterfaceTheme.Surface(pressed ? "#BE9E6430" : "#BE9E6418", "#BE9E64", 0, 0);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        _material.Draw(handle, box, uiScale);
        _highlight?.Draw(handle, box, uiScale);
    }
}
