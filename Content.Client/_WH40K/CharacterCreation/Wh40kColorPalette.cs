using System.Numerics;
using Content.Client._WH40K.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.CharacterCreation;

internal sealed class Wh40kColorPalette : BoxContainer
{
    private static readonly Color[] Swatches =
    {
        Color.FromHex("#1A1A1A"), Color.FromHex("#4A4A4A"), Color.FromHex("#8C8C8C"), Color.FromHex("#C8C8C8"),
        Color.FromHex("#F2F2F2"), Color.FromHex("#2B1B12"), Color.FromHex("#4E2E1E"), Color.FromHex("#7A4A2A"),
        Color.FromHex("#A0704B"), Color.FromHex("#C9A27C"), Color.FromHex("#E8C9A6"), Color.FromHex("#F6E3C9"),
        Color.FromHex("#D92B2B"), Color.FromHex("#F06B1E"), Color.FromHex("#F2C233"), Color.FromHex("#59B34A"),
        Color.FromHex("#23A08A"), Color.FromHex("#2E8BE6"), Color.FromHex("#4B4BE0"), Color.FromHex("#8A46D6"),
        Color.FromHex("#D24BB0"), Color.FromHex("#E0557A"), Color.FromHex("#A3E635"), Color.FromHex("#63E5FF"),
    };

    private readonly PanelContainer _chip;
    private readonly Button _custom;
    private readonly GridContainer _grid = new() { Columns = 12, HSeparationOverride = 3, VSeparationOverride = 3, HorizontalAlignment = HAlignment.Center };
    private Wh40kColorPopup? _popup;

    public event Action<Color>? OnColorChanged;
    public Color Color { get; private set; }

    public Wh40kColorPalette(Color initial, bool inline = false)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalAlignment = inline ? HAlignment.Left : HAlignment.Center;
        var colors = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        if (inline)
            AddChild(colors);
        else
            AddChild(new PanelContainer { PanelOverride = Wh40kInterfaceTheme.Card(9), Children = { colors } });

        var swatchGroup = new ButtonGroup();
        foreach (var color in Swatches)
        {
            var button = new ContainerButton
            {
                SetSize = new Vector2(19),
                Group = swatchGroup,
                ToggleMode = true,
                ToolTip = color.ToHex(),
                StyleBoxOverride = new StyleBoxFlat(color)
                {
                    BorderColor = Wh40kInterfaceTheme.Border,
                    BorderThickness = new Thickness(1),
                },
            };
            button.OnPressed += _ => Pick(color);
            _grid.AddChild(button);
        }
        _chip = new PanelContainer { SetSize = new Vector2(24), VerticalAlignment = VAlignment.Center };
        _custom = new Button { Text = Loc.GetString("wh40k-personalization-custom-color") };
        _custom.OnPressed += _ => OpenCustom();
        var actions = new BoxContainer
        {
            SeparationOverride = 8,
            Children = { _chip },
        };
        if (!inline)
        {
            actions.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-personalization-palette"), FontColorOverride = Wh40kInterfaceTheme.MutedText,
                HorizontalExpand = true, VerticalAlignment = VAlignment.Center,
            });
        }
        actions.AddChild(_custom);
        if (inline)
            colors.AddChild(_grid);
        colors.AddChild(actions);
        if (!inline)
            colors.AddChild(_grid);
        SetColor(initial);
    }

    public void SetDisabled(bool disabled)
    {
        foreach (var child in _grid.Children)
            ((ContainerButton) child).Disabled = disabled;
        _custom.Disabled = disabled;
        if (disabled)
            CloseCustom();
    }

    public void SetColor(Color color)
    {
        Color = color;
        _chip.PanelOverride = new StyleBoxFlat(color);
        _chip.ToolTip = color.ToHex();
        for (var i = 0; i < Swatches.Length; i++)
        {
            var button = (ContainerButton) _grid.GetChild(i);
            button.Pressed = Swatches[i] == color;
            if (button.StyleBoxOverride is StyleBoxFlat border)
            {
                border.BorderColor = button.Pressed ? Wh40kInterfaceTheme.Gold : Wh40kInterfaceTheme.Border;
                border.BorderThickness = new Thickness(button.Pressed ? 2 : 1);
            }
        }
    }

    internal void Pick(Color color)
    {
        SetColor(color);
        OnColorChanged?.Invoke(color);
    }

    internal Wh40kColorPopup OpenCustom()
    {
        CloseCustom();
        _popup = new Wh40kColorPopup(Color);
        _popup.OnColorChanged += Pick;
        Wh40kInterfaceTheme.ApplyControls(_popup);
        UserInterfaceManager.ModalRoot.AddChild(_popup);
        var anchor = new Vector2(GlobalPosition.X + (Width - Wh40kColorPopup.PanelWidth) / 2, _custom.GlobalPosition.Y);
        _popup.Open(UIBox2.FromDimensions(anchor + new Vector2(0, _custom.Height + 4), Vector2.One),
            altPosUp: anchor - new Vector2(0, 4));
        return _popup;
    }

    internal void CloseCustom()
    {
        _popup?.Close();
        _popup?.Orphan();
        _popup = null;
    }

    protected override void ExitedTree()
    {
        CloseCustom();
        base.ExitedTree();
    }
}
