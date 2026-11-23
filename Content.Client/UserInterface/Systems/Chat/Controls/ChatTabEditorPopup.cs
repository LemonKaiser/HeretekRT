using System.Numerics;
using Content.Client._WH40K.UserInterface;
using Content.Client.Lobby.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chat;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

/// <summary>
/// Compact WH40K-themed popup for creating, renaming, configuring channels, and deleting chat tabs.
/// </summary>
public sealed class ChatTabEditorPopup : Popup
{
    private const float PopupWidth = 260f;
    private const float ScreenMargin = 8f;

    private static readonly Color Gold = Color.FromHex("#E1C17E");
    private static readonly Color TextColor = Color.FromHex("#E9E1D0");
    private static readonly Color MutedText = Color.FromHex("#AD9F86");
    private static readonly Color DisabledText = Color.FromHex("#817865");
    private static readonly Color DarkButtonText = Color.FromHex("#20160B");
    private static readonly Color DangerText = Color.FromHex("#EF8E72");

    private readonly LobbyRoundedStyleBox _inputNormalStyle;
    private readonly LobbyRoundedStyleBox _inputFocusStyle;
    private readonly LobbyRoundedStyleBox _inputInvalidStyle;
    private readonly LobbyRoundedStyleBox _buttonNormalStyle;
    private readonly LobbyRoundedStyleBox _buttonHoverStyle;
    private readonly LobbyRoundedStyleBox _buttonPressedStyle;
    private readonly LobbyRoundedStyleBox _buttonDisabledStyle;
    private readonly LobbyRoundedStyleBox _primaryNormalStyle;
    private readonly LobbyRoundedStyleBox _primaryHoverStyle;
    private readonly LobbyRoundedStyleBox _primaryPressedStyle;
    private readonly LobbyRoundedStyleBox _dangerNormalStyle;
    private readonly LobbyRoundedStyleBox _dangerHoverStyle;
    private readonly LobbyRoundedStyleBox _dangerPressedStyle;

    private readonly Label _titleLabel;
    private readonly LineEdit _nameEdit;
    private readonly EditorPopupButton _selectAllButton;
    private readonly EditorPopupButton _selectNoneButton;
    private readonly BoxContainer _channelsBox;
    private readonly EditorPopupButton _saveButton;
    private readonly EditorPopupButton _deleteButton;
    private readonly EditorPopupButton _cancelButton;
    private readonly Dictionary<ChatChannel, CheckBox> _channelCheckboxes = new();

    private bool _isEditMode;
    private int _editingTabIndex = -1;
    private bool _nameFocused;
    private ChatChannel _hiddenChannels = ChatChannel.None;

    public event Action<string, ChatChannel>? OnTabCreateRequested;
    public event Action<int, string, ChatChannel>? OnTabUpdateRequested;
    public event Action<int>? OnTabDeleteRequested;

    public ChatTabEditorPopup()
    {
        MinWidth = PopupWidth;
        MaxWidth = PopupWidth;

        _inputNormalStyle = CreateStyle("#080705D9", "#B89F5760", "#00000000", 3f, 8f, 4f);
        _inputFocusStyle = CreateStyle("#030302EB", "#E1C17E", "#E4C37C14", 3f, 8f, 4f);
        _inputInvalidStyle = CreateStyle("#1A0D0BE6", "#C86452", "#00000000", 3f, 8f, 4f);

        _buttonNormalStyle = CreateStyle("#18140FE8", "#56452D", "#E4C37C0C", 4f, 8f, 4f);
        _buttonHoverStyle = CreateStyle("#261E14F2", "#BE9E64", "#E4C37C1C", 4f, 8f, 4f);
        _buttonPressedStyle = CreateStyle("#392B1CF5", "#E1C17E", "#E4C37C2A", 4f, 8f, 4f);
        _buttonDisabledStyle = CreateStyle("#100D0AB0", "#56452D70", "#00000000", 4f, 8f, 4f);

        _primaryNormalStyle = CreateStyle("#C7A35CE8", "#E9D199", "#FFF3D630", 4f, 10f, 4f);
        _primaryHoverStyle = CreateStyle("#DEC07BF2", "#FFF1C8", "#FFF3D642", 4f, 10f, 4f);
        _primaryPressedStyle = CreateStyle("#A98744F5", "#E1C17E", "#FFF3D620", 4f, 10f, 4f);

        _dangerNormalStyle = CreateStyle("#41241FE8", "#A96553", "#E48F7C14", 4f, 10f, 4f);
        _dangerHoverStyle = CreateStyle("#5A2D25F2", "#D57D66", "#E48F7C22", 4f, 10f, 4f);
        _dangerPressedStyle = CreateStyle("#6F352BF5", "#EF8E72", "#E48F7C2E", 4f, 10f, 4f);

        var outerPanel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new Wh40kWindowStyleBox(),
        };
        AddChild(outerPanel);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(10),
            HorizontalExpand = true,
        };
        outerPanel.AddChild(root);

        _titleLabel = new Label
        {
            FontColorOverride = Gold,
        };
        root.AddChild(_titleLabel);

        root.AddChild(new HLine
        {
            Color = Color.FromHex("#8F744A66"),
            Thickness = 1f,
        });

        root.AddChild(new Label
        {
            Text = Loc.GetString("hud-chatbox-tab-name-label"),
            FontColorOverride = MutedText,
        });

        _nameEdit = new LineEdit
        {
            HorizontalExpand = true,
            MinHeight = 28f,
            PlaceHolder = Loc.GetString("hud-chatbox-tab-name-placeholder"),
            StyleBoxOverride = _inputNormalStyle,
        };
        _nameEdit.OnTextChanged += OnNameTextChanged;
        _nameEdit.OnTextEntered += _ => Submit();
        _nameEdit.OnKeyBindDown += OnNameKeyBindDown;
        _nameEdit.OnFocusEnter += _ =>
        {
            _nameFocused = true;
            UpdateNameInputVisuals();
        };
        _nameEdit.OnFocusExit += _ =>
        {
            _nameFocused = false;
            UpdateNameInputVisuals();
        };
        root.AddChild(_nameEdit);

        var channelsHeader = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        root.AddChild(channelsHeader);

        channelsHeader.AddChild(new Label
        {
            Text = Loc.GetString("hud-chatbox-tab-channels-label"),
            FontColorOverride = MutedText,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        });

        _selectAllButton = new EditorPopupButton(this, Loc.GetString("hud-chatbox-tab-channels-all"), 22f, ButtonRole.Secondary);
        _selectAllButton.OnPressed += _ => SetAllChannels(true);
        channelsHeader.AddChild(_selectAllButton);

        _selectNoneButton = new EditorPopupButton(this, Loc.GetString("hud-chatbox-tab-channels-none"), 22f, ButtonRole.Secondary);
        _selectNoneButton.OnPressed += _ => SetAllChannels(false);
        channelsHeader.AddChild(_selectNoneButton);

        var channelsSurface = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = CreateStyle("#0D0B0899", "#56452D", "#00000000", 4f, 4f, 4f),
        };
        root.AddChild(channelsSurface);

        var channelsScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true,
            ReserveScrollbarSpace = true,
            MinHeight = 160f,
            MaxHeight = 220f,
        };
        channelsSurface.AddChild(channelsScroll);

        _channelsBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true,
            Margin = new Thickness(2, 2, 4, 2),
        };
        channelsScroll.AddChild(_channelsBox);

        var footer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2, 0, 0),
        };
        root.AddChild(footer);

        _saveButton = new EditorPopupButton(this, Loc.GetString("hud-chatbox-tab-save-button"), 28f, ButtonRole.Primary)
        {
            HorizontalExpand = true,
        };
        _saveButton.OnPressed += _ => Submit();
        footer.AddChild(_saveButton);

        _deleteButton = new EditorPopupButton(this, Loc.GetString("hud-chatbox-tab-delete-button"), 28f, ButtonRole.Danger);
        _deleteButton.OnPressed += _ => DeleteCurrentTab();
        footer.AddChild(_deleteButton);

        _cancelButton = new EditorPopupButton(this, Loc.GetString("hud-chatbox-tab-cancel-button"), 28f, ButtonRole.Secondary);
        _cancelButton.OnPressed += _ => Close();
        footer.AddChild(_cancelButton);
    }

    public void OpenCreate(Control anchor, ChatChannel availableChannels)
    {
        _isEditMode = false;
        _editingTabIndex = -1;
        _hiddenChannels = ChatChannel.None;

        _titleLabel.Text = Loc.GetString("hud-chatbox-tab-create-title");
        _saveButton.Text = Loc.GetString("hud-chatbox-tab-create-button");
        _deleteButton.Visible = false;
        _cancelButton.Visible = true;

        _nameEdit.Text = string.Empty;
        PopulateChannelCheckboxes(availableChannels, ChatChannel.None);
        UpdateSubmitState();

        OpenAnchored(anchor);
        _nameEdit.GrabKeyboardFocus();
    }

    public void OpenEdit(
        Control anchor,
        int tabIndex,
        string currentName,
        ChatChannel currentChannels,
        ChatChannel availableChannels,
        bool canDelete)
    {
        _isEditMode = true;
        _editingTabIndex = tabIndex;
        _hiddenChannels = currentChannels & ~availableChannels;

        _titleLabel.Text = Loc.GetString("hud-chatbox-tab-edit-title");
        _saveButton.Text = Loc.GetString("hud-chatbox-tab-save-button");
        _deleteButton.Visible = true;
        _deleteButton.Disabled = !canDelete;
        _deleteButton.ToolTip = canDelete ? null : Loc.GetString("hud-chatbox-tab-delete-disabled-tooltip");
        _deleteButton.RefreshVisual();
        _cancelButton.Visible = false;

        _nameEdit.Text = currentName;
        PopulateChannelCheckboxes(availableChannels, currentChannels);
        UpdateSubmitState();

        OpenAnchored(anchor);
        _nameEdit.GrabKeyboardFocus();
        _nameEdit.CursorPosition = _nameEdit.Text.Length;
    }

    private void OpenAnchored(Control anchor)
    {
        InvalidateMeasure();
        Measure(new Vector2(PopupWidth, 480f));

        var rootSize = UserInterfaceManager.RootControl.Size;
        var maxPopupHeight = Math.Max(220f, rootSize.Y - ScreenMargin * 2f);
        var height = Math.Clamp(DesiredSize.Y, 220f, maxPopupHeight);

        var x = Math.Clamp(
            anchor.GlobalPosition.X,
            ScreenMargin,
            Math.Max(ScreenMargin, rootSize.X - PopupWidth - ScreenMargin));

        var belowY = anchor.GlobalPosition.Y + anchor.Size.Y + 4f;
        var aboveY = anchor.GlobalPosition.Y - height - 4f;
        float y;
        if (belowY + height + ScreenMargin <= rootSize.Y)
            y = belowY;
        else if (aboveY >= ScreenMargin)
            y = aboveY;
        else
            y = Math.Clamp(belowY, ScreenMargin, Math.Max(ScreenMargin, rootSize.Y - height - ScreenMargin));

        Open(UIBox2.FromDimensions(new Vector2(x, y), new Vector2(PopupWidth, height)));
    }

    private void PopulateChannelCheckboxes(ChatChannel availableChannels, ChatChannel selectedChannels)
    {
        _channelsBox.RemoveAllChildren();
        _channelCheckboxes.Clear();

        foreach (var channel in ChannelFilterPopup.ChannelFilterOrder)
        {
            if ((availableChannels & channel) == 0)
                continue;

            var checkbox = new CheckBox
            {
                Text = Loc.GetString($"hud-chatbox-channel-{channel}"),
                Pressed = (selectedChannels & channel) != 0,
                MinHeight = 24f,
                MaxHeight = 24f,
                DefaultCursorShape = CursorShape.Hand,
            };
            _channelCheckboxes[channel] = checkbox;
            _channelsBox.AddChild(checkbox);
        }
    }

    private void SetAllChannels(bool selected)
    {
        foreach (var checkbox in _channelCheckboxes.Values)
        {
            checkbox.Pressed = selected;
        }
    }

    private ChatChannel CollectSelectedChannels()
    {
        var channels = _hiddenChannels;
        foreach (var (channel, checkbox) in _channelCheckboxes)
        {
            if (checkbox.Pressed)
                channels |= channel;
        }

        return channels;
    }

    private void OnNameKeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.TextReleaseFocus ||
            args.Function == EngineKeyFunctions.CloseModals)
        {
            args.Handle();
            Close();
        }
    }

    private void OnNameTextChanged(LineEdit.LineEditEventArgs args)
    {
        if (args.Text.Length > ChatUIController.MaxChatTabNameLength)
        {
            var name = ChatUIController.LimitTabNameLength(args.Text);
            var caret = Math.Min(_nameEdit.CursorPosition, name.Length);
            _nameEdit.Text = name;
            _nameEdit.CursorPosition = caret;
        }

        UpdateSubmitState();
    }

    private void UpdateSubmitState()
    {
        var valid = ChatUIController.NormalizeTabName(_nameEdit.Text).Length > 0;
        _saveButton.Disabled = !valid;
        _saveButton.RefreshVisual();
        UpdateNameInputVisuals();
    }

    private void UpdateNameInputVisuals()
    {
        var invalid = !string.IsNullOrEmpty(_nameEdit.Text) &&
                      ChatUIController.NormalizeTabName(_nameEdit.Text).Length == 0;

        _nameEdit.StyleBoxOverride = invalid
            ? _inputInvalidStyle
            : _nameFocused
                ? _inputFocusStyle
                : _inputNormalStyle;
    }

    private void Submit()
    {
        var normalized = ChatUIController.NormalizeTabName(_nameEdit.Text);
        if (normalized.Length == 0)
            return;

        var channels = CollectSelectedChannels();
        if (_isEditMode)
            OnTabUpdateRequested?.Invoke(_editingTabIndex, normalized, channels);
        else
            OnTabCreateRequested?.Invoke(normalized, channels);

        Close();
    }

    private void DeleteCurrentTab()
    {
        if (!_isEditMode || _editingTabIndex < 0)
            return;

        OnTabDeleteRequested?.Invoke(_editingTabIndex);
        Close();
    }

    private void ApplyButtonVisual(Button button, ButtonRole role, BaseButton.DrawModeEnum drawMode)
    {
        if (drawMode == BaseButton.DrawModeEnum.Disabled)
        {
            button.StyleBoxOverride = _buttonDisabledStyle;
            button.Label.FontColorOverride = DisabledText;
            return;
        }

        button.StyleBoxOverride = (role, drawMode) switch
        {
            (ButtonRole.Primary, BaseButton.DrawModeEnum.Hover) => _primaryHoverStyle,
            (ButtonRole.Primary, BaseButton.DrawModeEnum.Pressed) => _primaryPressedStyle,
            (ButtonRole.Primary, _) => _primaryNormalStyle,
            (ButtonRole.Danger, BaseButton.DrawModeEnum.Hover) => _dangerHoverStyle,
            (ButtonRole.Danger, BaseButton.DrawModeEnum.Pressed) => _dangerPressedStyle,
            (ButtonRole.Danger, _) => _dangerNormalStyle,
            (_, BaseButton.DrawModeEnum.Hover) => _buttonHoverStyle,
            (_, BaseButton.DrawModeEnum.Pressed) => _buttonPressedStyle,
            _ => _buttonNormalStyle,
        };

        button.Label.FontColorOverride = role switch
        {
            ButtonRole.Primary => DarkButtonText,
            ButtonRole.Danger => DangerText,
            _ => drawMode == BaseButton.DrawModeEnum.Normal ? TextColor : Gold,
        };
    }

    private static LobbyRoundedStyleBox CreateStyle(
        string background,
        string border,
        string highlight,
        float cornerRadius,
        float horizontalPadding,
        float verticalPadding)
    {
        var style = new LobbyRoundedStyleBox
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            HighlightColor = Color.FromHex(highlight),
            BorderThickness = new Thickness(1f),
            CornerRadius = cornerRadius,
        };
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontalPadding);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, verticalPadding);
        return style;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        UserInterfaceManager.GetUIController<Wh40kInterfaceUIController>().RegisterFloatingPanel(this);
    }

    protected override void ExitedTree()
    {
        UserInterfaceManager.GetUIController<Wh40kInterfaceUIController>().UnregisterFloatingPanel(this);
        base.ExitedTree();
    }

    private sealed class EditorPopupButton : Button
    {
        private readonly ChatTabEditorPopup _owner;
        private readonly ButtonRole _role;

        public EditorPopupButton(ChatTabEditorPopup owner, string text, float height, ButtonRole role)
        {
            _owner = owner;
            _role = role;
            Text = text;
            MinHeight = height;
            MaxHeight = height;
            SetHeight = height;
            DefaultCursorShape = CursorShape.Hand;
            RefreshVisual();
        }

        public void RefreshVisual()
        {
            if (_owner == null)
                return;

            DefaultCursorShape = Disabled ? CursorShape.Arrow : CursorShape.Hand;
            _owner.ApplyButtonVisual(this, _role, DrawMode);
        }

        protected override void DrawModeChanged()
        {
            base.DrawModeChanged();
            RefreshVisual();
        }
    }

    private enum ButtonRole : byte
    {
        Secondary,
        Primary,
        Danger,
    }
}
