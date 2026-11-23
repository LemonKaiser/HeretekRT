using System.Numerics;
using Content.Client.Lobby.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

/// <summary>
/// Compact WH40K-styled chat tab bar with wheel-scrollable tabs and a single add-tab button.
/// </summary>
public sealed partial class ChatTabBar : BoxContainer
{
    private const float TabHeight = 26f;

    [Dependency] private IGameTiming _timing = default!;

    private readonly ChatUIController _controller;
    private readonly ChatTabScrollContainer _scrollContainer;
    private readonly BoxContainer _tabsRow;
    private readonly ChatTabAddButton _addButton;
    private readonly ChatTabEditorPopup _editorPopup;
    private readonly List<ChatTabButton> _tabButtons = new();

    private readonly LobbyRoundedStyleBox _activeNormalStyle;
    private readonly LobbyRoundedStyleBox _activeHoverStyle;
    private readonly LobbyRoundedStyleBox _activePressedStyle;
    private readonly LobbyRoundedStyleBox _inactiveNormalStyle;
    private readonly LobbyRoundedStyleBox _inactiveHoverStyle;
    private readonly LobbyRoundedStyleBox _inactivePressedStyle;
    private readonly LobbyRoundedStyleBox _unreadStyle;
    private readonly LobbyRoundedStyleBox _addNormalStyle;
    private readonly LobbyRoundedStyleBox _addHoverStyle;
    private readonly LobbyRoundedStyleBox _addPressedStyle;
    private readonly LobbyRoundedStyleBox _addDisabledStyle;

    private static readonly Color ActiveTextColor = Color.FromHex("#E1C17E");
    private static readonly Color ActiveHoverTextColor = Color.FromHex("#F3DC9E");
    private static readonly Color InactiveTextColor = Color.FromHex("#AD9F86");
    private static readonly Color HoverTextColor = Color.FromHex("#E9E1D0");
    private static readonly Color PressedTextColor = Color.FromHex("#FFF1C8");
    private static readonly Color DisabledTextColor = Color.FromHex("#6B614F");

    private uint _frameLastPopupChanged;
    private int _lastActiveIndex = -1;

    public ChatTabBar()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        HorizontalExpand = true;
        MinHeight = TabHeight;

        _activeNormalStyle = CreateButtonStyle("#392B1CF0", "#BE9E64", "#E4C37C20", 10f, 2f);
        _activeHoverStyle = CreateButtonStyle("#44331EF2", "#E1C17E", "#E4C37C2A", 10f, 2f);
        _activePressedStyle = CreateButtonStyle("#4F3B20F5", "#F0D49B", "#E4C37C33", 10f, 2f);
        _inactiveNormalStyle = CreateButtonStyle("#18140FD9", "#56452DC0", "#E4C37C0A", 10f, 2f);
        _inactiveHoverStyle = CreateButtonStyle("#261E14EB", "#9E8650", "#E4C37C16", 10f, 2f);
        _inactivePressedStyle = CreateButtonStyle("#4A3816F2", "#E1C17E", "#E4C37C2A", 10f, 2f);
        _unreadStyle = CreateButtonStyle("#241B11E8", "#BE9E64C8", "#E4C37C18", 10f, 2f);

        _addNormalStyle = CreateButtonStyle("#18140FD9", "#56452DC0", "#E4C37C0A", 0f, 0f);
        _addHoverStyle = CreateButtonStyle("#261E14EB", "#BE9E64", "#E4C37C1A", 0f, 0f);
        _addPressedStyle = CreateButtonStyle("#392B1CF2", "#E1C17E", "#E4C37C2A", 0f, 0f);
        _addDisabledStyle = CreateButtonStyle("#100D0AB0", "#56452D66", "#00000000", 0f, 0f);

        _scrollContainer = new ChatTabScrollContainer
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        AddChild(_scrollContainer);

        _tabsRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            VerticalAlignment = VAlignment.Center,
        };
        _scrollContainer.AddChild(_tabsRow);

        _addButton = new ChatTabAddButton(this);
        _addButton.OnPressed += OnAddButtonPressed;
        AddChild(_addButton);

        _controller = UserInterfaceManager.GetUIController<ChatUIController>();
        _editorPopup = UserInterfaceManager.CreatePopup<ChatTabEditorPopup>();
        _editorPopup.OnVisibilityChanged += OnEditorPopupVisibilityChanged;
        _editorPopup.OnTabCreateRequested += OnTabCreateRequested;
        _editorPopup.OnTabUpdateRequested += OnTabUpdateRequested;
        _editorPopup.OnTabDeleteRequested += OnTabDeleteRequested;

        RefreshTabs();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _controller.TabsChanged += RefreshTabs;
        _controller.TabUnreadChanged += RefreshUnreadStates;
        RefreshTabs();
    }

    protected override void ExitedTree()
    {
        _controller.TabsChanged -= RefreshTabs;
        _controller.TabUnreadChanged -= RefreshUnreadStates;
        _editorPopup.Close();
        base.ExitedTree();
    }

    private void OnEditorPopupVisibilityChanged(Control _)
    {
        _frameLastPopupChanged = _timing.CurFrame;
    }

    private void OnAddButtonPressed(BaseButton.ButtonEventArgs _)
    {
        if (_frameLastPopupChanged == _timing.CurFrame)
            return;

        if (_editorPopup.Visible)
        {
            _editorPopup.Close();
            return;
        }

        if (!_controller.CanAddTab)
            return;

        _editorPopup.OpenCreate(_addButton, _controller.GetConfigurableTabChannels());
    }

    private void OnTabRightClicked(ChatTabButton button)
    {
        if (_frameLastPopupChanged == _timing.CurFrame)
            return;

        if (_editorPopup.Visible)
        {
            _editorPopup.Close();
            return;
        }

        var tabs = _controller.GetTabs();
        if (button.TabIndex < 0 || button.TabIndex >= tabs.Count)
            return;

        var tab = tabs[button.TabIndex];
        _editorPopup.OpenEdit(
            button,
            button.TabIndex,
            tab.Name,
            tab.Channels,
            _controller.GetConfigurableTabChannels(),
            _controller.CanDeleteTab);
    }

    private void OnTabCreateRequested(string name, Shared.Chat.ChatChannel channels)
    {
        _controller.TryAddTab(name, channels);
    }

    private void OnTabUpdateRequested(int index, string name, Shared.Chat.ChatChannel channels)
    {
        _controller.TryUpdateTab(index, name, channels);
    }

    private void OnTabDeleteRequested(int index)
    {
        _controller.TryRemoveTab(index);
    }

    private void RefreshTabs()
    {
        var tabs = _controller.GetTabs();
        var activeIndex = _controller.ActiveTabIndex;

        while (_tabButtons.Count > tabs.Count)
        {
            var last = _tabButtons[^1];
            last.OnRightClicked -= OnTabRightClicked;
            _tabsRow.RemoveChild(last);
            _tabButtons.RemoveAt(_tabButtons.Count - 1);
        }

        for (var i = 0; i < tabs.Count; i++)
        {
            if (_tabButtons.Count <= i)
            {
                var button = new ChatTabButton(this)
                {
                    TabIndex = i,
                };
                button.OnPressed += _ => _controller.SelectTab(button.TabIndex);
                button.OnRightClicked += OnTabRightClicked;
                _tabButtons.Add(button);
                _tabsRow.AddChild(button);
            }

            var current = _tabButtons[i];
            var tab = tabs[i];
            current.TabIndex = i;
            current.Text = tab.Name;
            current.ToolTip = null;
            current.SetTabState(i == activeIndex, tab.HasUnread);
        }

        if (activeIndex >= 0 && activeIndex < _tabButtons.Count && _lastActiveIndex != activeIndex)
        {
            _scrollContainer.EnsureChildVisible(_tabButtons[activeIndex]);
        }

        _lastActiveIndex = activeIndex;

        var canAdd = _controller.CanAddTab;
        _addButton.Disabled = !canAdd;
        _addButton.ToolTip = canAdd
            ? Loc.GetString("hud-chatbox-tab-add-tooltip", ("count", tabs.Count), ("max", ChatUIController.MaxChatTabs))
            : Loc.GetString("hud-chatbox-tab-add-limit", ("max", ChatUIController.MaxChatTabs));
        _addButton.RefreshVisual();
    }

    private void RefreshUnreadStates()
    {
        var tabs = _controller.GetTabs();
        var activeIndex = _controller.ActiveTabIndex;
        var count = Math.Min(_tabButtons.Count, tabs.Count);
        for (var i = 0; i < count; i++)
        {
            _tabButtons[i].SetTabState(i == activeIndex, tabs[i].HasUnread);
        }
    }

    private static LobbyRoundedStyleBox CreateButtonStyle(
        string background,
        string border,
        string highlight,
        float horizontalMargin,
        float verticalMargin)
    {
        var style = new LobbyRoundedStyleBox
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            HighlightColor = Color.FromHex(highlight),
            BorderThickness = new Thickness(1f),
            CornerRadius = 4f,
        };
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontalMargin);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, verticalMargin);
        return style;
    }

    private sealed class ChatTabAddButton : Button
    {
        private readonly ChatTabBar _owner;

        public ChatTabAddButton(ChatTabBar owner)
        {
            _owner = owner;
            Text = "+";
            MinSize = new Vector2(TabHeight, TabHeight);
            MaxSize = new Vector2(TabHeight, TabHeight);
            SetSize = new Vector2(TabHeight, TabHeight);
            DefaultCursorShape = CursorShape.Hand;
            HorizontalAlignment = HAlignment.Right;
            VerticalAlignment = VAlignment.Center;
            RefreshVisual();
        }

        public void RefreshVisual()
        {
            if (_owner == null)
                return;

            DefaultCursorShape = Disabled ? CursorShape.Arrow : CursorShape.Hand;
            switch (DrawMode)
            {
                case DrawModeEnum.Disabled:
                    StyleBoxOverride = _owner._addDisabledStyle;
                    Label.FontColorOverride = DisabledTextColor;
                    break;
                case DrawModeEnum.Pressed:
                    StyleBoxOverride = _owner._addPressedStyle;
                    Label.FontColorOverride = PressedTextColor;
                    break;
                case DrawModeEnum.Hover:
                    StyleBoxOverride = _owner._addHoverStyle;
                    Label.FontColorOverride = ActiveTextColor;
                    break;
                default:
                    StyleBoxOverride = _owner._addNormalStyle;
                    Label.FontColorOverride = InactiveTextColor;
                    break;
            }
        }

        protected override void DrawModeChanged()
        {
            base.DrawModeChanged();
            RefreshVisual();
        }
    }

    private sealed class ChatTabButton : Button
    {
        private readonly ChatTabBar _owner;
        private bool _active;
        private bool _hasUnread;

        public int TabIndex { get; set; }

        public event Action<ChatTabButton>? OnRightClicked;

        public ChatTabButton(ChatTabBar owner)
        {
            _owner = owner;
            MinHeight = TabHeight;
            MaxHeight = TabHeight;
            SetHeight = TabHeight;
            DefaultCursorShape = CursorShape.Hand;
            RefreshVisual();
        }

        public void SetTabState(bool active, bool hasUnread)
        {
            _active = active;
            _hasUnread = hasUnread && !active;
            RefreshVisual();
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            if (args.Function == EngineKeyFunctions.UIRightClick)
            {
                args.Handle();
                OnRightClicked?.Invoke(this);
                return;
            }

            base.KeyBindDown(args);
        }

        protected override void DrawModeChanged()
        {
            base.DrawModeChanged();
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (_owner == null)
                return;

            switch (DrawMode)
            {
                case DrawModeEnum.Pressed:
                    StyleBoxOverride = _active ? _owner._activePressedStyle : _owner._inactivePressedStyle;
                    Label.FontColorOverride = PressedTextColor;
                    break;
                case DrawModeEnum.Hover:
                    StyleBoxOverride = _active ? _owner._activeHoverStyle : _owner._inactiveHoverStyle;
                    Label.FontColorOverride = _active ? ActiveHoverTextColor : HoverTextColor;
                    break;
                default:
                    if (_active)
                    {
                        StyleBoxOverride = _owner._activeNormalStyle;
                        Label.FontColorOverride = ActiveTextColor;
                    }
                    else if (_hasUnread)
                    {
                        StyleBoxOverride = _owner._unreadStyle;
                        Label.FontColorOverride = ActiveTextColor;
                    }
                    else
                    {
                        StyleBoxOverride = _owner._inactiveNormalStyle;
                        Label.FontColorOverride = InactiveTextColor;
                    }
                    break;
            }
        }
    }
}
