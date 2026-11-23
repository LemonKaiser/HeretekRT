using System.Numerics;
using Content.Client._WH40K.CharacterCreation;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.UserInterface;

/// <summary>Keeps the native page selection and references while using the shared spaced tab bar.</summary>
internal sealed class Wh40kTabLayout : BoxContainer
{
    private readonly TabContainer _tabs;
    internal Wh40kTabLayout(TabContainer tabs)
    {
        _tabs = tabs;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true; VerticalExpand = true;
        var bar = new Wh40kPersonalizationTabs(); bar.Bind(tabs);
        AddChild(bar); AddChild(tabs);
        tabs.HorizontalExpand = true; tabs.VerticalExpand = true;
        tabs.OnTabChanged += _ => InvalidateArrange();
    }
    protected override Vector2 ArrangeOverride(Vector2 size)
    {
        base.ArrangeOverride(size);
        // Native TabContainer retains its last header height when TabsVisible is changed.
        // Its selected page must occupy the complete body below our separate tab bar.
        if (_tabs.ChildCount > _tabs.CurrentTab)
            _tabs.GetChild(_tabs.CurrentTab).Arrange(UIBox2.FromDimensions(Vector2.Zero, _tabs.Size));
        return size;
    }
}
