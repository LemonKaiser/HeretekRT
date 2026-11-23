using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.UserInterface.Controls;

public sealed partial class MenuButton : ContainerButton
{
    public const string StylePropertyIconColor = "menu-icon-color";
    public const string StylePropertyIconHoverColor = "menu-icon-hover-color";
    public const string StylePropertyIconPressedColor = "menu-icon-pressed-color";
    [Dependency] private IInputManager _inputManager = default!;
    public const string StyleClassLabelTopButton = "topButtonLabel";
    public const string StyleClassRedTopButton = "topButtonLabel";

    private static readonly Color ColorNormal = Color.FromHex("#8f8f8f");
    private static readonly Color ColorRedNormal = Color.FromHex("#FEFEFE");
    private static readonly Color ColorHovered = Color.FromHex("#363636");
    private static readonly Color ColorRedHovered = Color.FromHex("#FFFFFF");
    private static readonly Color ColorPressed = Color.FromHex("#878787");

    private const float VertPad = 8f;
    private Color NormalColor => GetIconColor(StylePropertyIconColor, HasStyleClass(StyleClassRedTopButton) ? ColorRedNormal : ColorNormal);
    private Color HoveredColor => GetIconColor(StylePropertyIconHoverColor, HasStyleClass(StyleClassRedTopButton) ? ColorRedHovered : ColorHovered);
    private Color PressedColor => GetIconColor(StylePropertyIconPressedColor, ColorPressed);

    private Color GetIconColor(string property, Color fallback) =>
        TryGetStyleProperty<Color>(property, out var color) ? color : fallback;

    private BoundKeyFunction _function;
    private readonly BoxContainer _root;
    private readonly TextureRect? _buttonIcon;
    private readonly Label? _buttonLabel;

    public string AppendStyleClass { set => AddStyleClass(value); }
    public Texture? Icon { get => _buttonIcon!.Texture; set => _buttonIcon!.Texture = value; }

    public BoundKeyFunction BoundKey
    {
        get => _function;
        set
        {
            _function = value;
            _buttonLabel!.Text = BoundKeyHelper.ShortKeyName(value);
        }
    }

    public BoxContainer ButtonRoot => _root;

    public MenuButton()
    {
        IoCManager.InjectDependencies(this);
        _buttonIcon = new TextureRect()
        {
            TextureScale = new Vector2(0.5f, 0.5f),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            VerticalExpand = true,
            Margin = new Thickness(0, VertPad),
            ModulateSelfOverride = NormalColor,
            Stretch = TextureRect.StretchMode.KeepCentered
        };
        _buttonLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HAlignment.Center,
            ModulateSelfOverride = NormalColor,
            StyleClasses = {StyleClassLabelTopButton}
        };
        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children =
            {
                _buttonIcon,
                _buttonLabel
            }
        };
        AddChild(_root);
        ToggleMode = true;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        UserInterfaceManager.GetUIController<Content.Client._WH40K.UserInterface.Wh40kInterfaceUIController>().RegisterFloatingPanel(this);
        _inputManager.OnKeyBindingAdded += OnKeyBindingChanged;
        _inputManager.OnKeyBindingRemoved += OnKeyBindingChanged;
        _inputManager.OnInputModeChanged += OnKeyBindingChanged;
    }

    protected override void ExitedTree()
    {
        UserInterfaceManager.GetUIController<Content.Client._WH40K.UserInterface.Wh40kInterfaceUIController>().UnregisterFloatingPanel(this);
        _inputManager.OnKeyBindingAdded -= OnKeyBindingChanged;
        _inputManager.OnKeyBindingRemoved -= OnKeyBindingChanged;
        _inputManager.OnInputModeChanged -= OnKeyBindingChanged;
        base.ExitedTree();
    }


    private void OnKeyBindingChanged(IKeyBinding obj)
    {
        _buttonLabel!.Text = BoundKeyHelper.ShortKeyName(_function);
    }

    private void OnKeyBindingChanged()
    {
        _buttonLabel!.Text = BoundKeyHelper.ShortKeyName(_function);
    }

    protected override void StylePropertiesChanged()
    {
        // colors of children depend on style, so ensure we update when style is changed
        base.StylePropertiesChanged();
        UpdateChildColors();
    }

    private void UpdateChildColors()
    {
        if (_buttonIcon == null || _buttonLabel == null) return;
        switch (DrawMode)
        {
            case DrawModeEnum.Normal:
                _buttonIcon.ModulateSelfOverride = NormalColor;
                _buttonLabel.ModulateSelfOverride = NormalColor;
                break;

            case DrawModeEnum.Pressed:
                _buttonIcon.ModulateSelfOverride = PressedColor;
                _buttonLabel.ModulateSelfOverride = PressedColor;
                break;

            case DrawModeEnum.Hover:
                _buttonIcon.ModulateSelfOverride = HoveredColor;
                _buttonLabel.ModulateSelfOverride = HoveredColor;
                break;

            case DrawModeEnum.Disabled:
                break;
        }
    }


    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateChildColors();
    }
}
