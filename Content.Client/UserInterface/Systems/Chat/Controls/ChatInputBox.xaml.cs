using Content.Client.Stylesheets;
using System;
using Content.Shared.Chat;
using Content.Shared.Input;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

[Virtual]
public class ChatInputBox : PanelContainer
{
    public readonly EmojiPickerButton EmojiButton;
    public readonly ChannelSelectorButton ChannelSelector;
    public readonly HistoryLineEdit Input;
    public readonly ChannelFilterButton FilterButton;
    protected readonly BoxContainer Container;
    protected ChatChannel ActiveChannel { get; private set; } = ChatChannel.Local;
    private bool _inputLocked;
    private bool _emojiAllowed = true;
    private string? _lockedPlaceholder;
    private string? _lockedToolTip;

    public ChatInputBox()
    {
        Container = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4
        };
        AddChild(Container);

        EmojiButton = new EmojiPickerButton
        {
            Name = "EmojiButton",
            StyleClasses = {"chatFilterOptionButton"}
        };
        EmojiButton.OnEmojiPicked += InsertEmoji;
        Container.AddChild(EmojiButton);

        ChannelSelector = new ChannelSelectorButton
        {
            Name = "ChannelSelector",
            ToggleMode = true,
            StyleClasses = {"chatSelectorOptionButton"},
            MinWidth = 75
        };
        Container.AddChild(ChannelSelector);
        Input = new HistoryLineEdit
        {
            Name = "Input",
            PlaceHolder = GetChatboxInfoPlaceholder(),
            HorizontalExpand = true,
            StyleClasses = {"chatLineEdit"}
        };
        Container.AddChild(Input);
        FilterButton = new ChannelFilterButton
        {
            Name = "FilterButton",
            StyleClasses = {"chatFilterOptionButton"}
        };
        Container.AddChild(FilterButton);
        AddStyleClass(StyleNano.StyleClassChatSubPanel);
        ChannelSelector.OnChannelSelect += UpdateActiveChannel;
    }

    private void UpdateActiveChannel(ChatSelectChannel selectedChannel)
    {
        ActiveChannel = (ChatChannel) selectedChannel;
    }

    private void InsertEmoji(string emoji)
    {
        if (_inputLocked)
            return;

        Input.InsertAtCursor(emoji);
        Input.GrabKeyboardFocus();
    }

    public void SetEmojiAllowed(bool allowed)
    {
        _emojiAllowed = allowed;
        ApplyEmojiButtonState();
    }

    /// <summary>
    /// Locks player chat input while an administrative mute is active.
    /// The server remains authoritative; this only provides immediate client feedback.
    /// </summary>
    public void SetInputLockState(bool locked, string? placeholder = null, string? toolTip = null)
    {
        var nextPlaceholder = locked ? placeholder ?? string.Empty : null;
        var nextToolTip = locked ? toolTip : null;
        var changed =
            _inputLocked != locked ||
            !string.Equals(_lockedPlaceholder, nextPlaceholder, StringComparison.Ordinal) ||
            !string.Equals(_lockedToolTip, nextToolTip, StringComparison.Ordinal);

        if (!changed)
        {
            ApplyEmojiButtonState();
            return;
        }

        var lockingNow = locked && !_inputLocked;
        _inputLocked = locked;
        _lockedPlaceholder = nextPlaceholder;
        _lockedToolTip = nextToolTip;

        Input.PlaceHolder = _lockedPlaceholder ?? GetChatboxInfoPlaceholder();
        Input.ToolTip = _lockedToolTip;
        Input.Editable = !locked;
        Input.CanKeyboardFocus = !locked;
        Input.KeyboardFocusOnClick = !locked;
        ApplyEmojiButtonState();
        Input.InvalidateMeasure();
        Input.InvalidateArrange();

        if (!lockingNow)
            return;

        Input.Clear();
        Input.ReleaseKeyboardFocus();
    }

    private void ApplyEmojiButtonState()
    {
        EmojiButton.SetAvailable(_emojiAllowed);

        if (!_inputLocked)
            return;

        EmojiButton.Disabled = true;
        EmojiButton.Popup.Close();
    }

    private static string GetChatboxInfoPlaceholder()
    {
        return (BoundKeyHelper.IsBound(ContentKeyFunctions.FocusChat), BoundKeyHelper.IsBound(ContentKeyFunctions.CycleChatChannelForward)) switch
        {
            (true, true) => Loc.GetString("hud-chatbox-info", ("talk-key", BoundKeyHelper.ShortKeyName(ContentKeyFunctions.FocusChat)), ("cycle-key", BoundKeyHelper.ShortKeyName(ContentKeyFunctions.CycleChatChannelForward))),
            (true, false) => Loc.GetString("hud-chatbox-info-talk", ("talk-key", BoundKeyHelper.ShortKeyName(ContentKeyFunctions.FocusChat))),
            (false, true) => Loc.GetString("hud-chatbox-info-cycle", ("cycle-key", BoundKeyHelper.ShortKeyName(ContentKeyFunctions.CycleChatChannelForward))),
            (false, false) => Loc.GetString("hud-chatbox-info-unbound")
        };
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
            EmojiButton.OnEmojiPicked -= InsertEmoji;
    }
}
