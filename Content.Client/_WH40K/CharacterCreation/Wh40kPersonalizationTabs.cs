using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.CharacterCreation;

public sealed class Wh40kPersonalizationTabs : GridContainer
{
    private TabContainer? _tabs;
    private readonly List<Button> _buttons = [];
    private float _lastGridWidth;

    public Wh40kPersonalizationTabs()
    {
        HorizontalExpand = true;
        HSeparationOverride = 5;
        VSeparationOverride = 5;
        Margin = new Thickness(0, 0, 0, 12);
        OnResized += () =>
        {
            var width = Math.Max(1, Size.X);
            if (Math.Abs(_lastGridWidth - width) >= 0.5f)
            {
                _lastGridWidth = width;
                MaxGridWidth = width;
            }
        };
    }

    public void Bind(TabContainer tabs)
    {
        _tabs = tabs;
        tabs.TabsVisible = false;
        tabs.PanelStyleBoxOverride = new StyleBoxFlat();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_tabs == null)
            return;
        // Some windows re-enable native headers when their available tabs change.
        // The shared bar remains the only header while preserving each tab's visibility.
        if (_tabs.TabsVisible)
            _tabs.TabsVisible = false;

        while (_buttons.Count > _tabs.ChildCount)
        {
            RemoveChild(_buttons[^1]);
            _buttons.RemoveAt(_buttons.Count - 1);
        }

        for (var index = 0; index < _tabs.ChildCount; index++)
        {
            if (_buttons.Count <= index)
            {
                var tab = index;
                var button = new Button { ToggleMode = true, MinHeight = 36 };
                button.OnPressed += _ => _tabs.CurrentTab = tab;
                _buttons.Add(button);
                AddChild(button);
            }

            var current = _buttons[index];
            var title = _tabs.GetActualTabTitle(index);
            if (current.Text != title)
                current.Text = title;
            var visible = _tabs.GetTabVisible(index);
            if (current.Visible != visible)
                current.Visible = visible;
            var pressed = _tabs.CurrentTab == index;
            if (current.Pressed != pressed)
                current.Pressed = pressed;
        }
    }
}
