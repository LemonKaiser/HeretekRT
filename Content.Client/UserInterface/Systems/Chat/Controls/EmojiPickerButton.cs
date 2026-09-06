using System.Numerics;
using Content.Client.UserInterface.Systems.Chat.RichText;
using Content.Shared.Chat;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

public sealed partial class EmojiPickerButton : ChatPopupButton<EmojiPickerPopup>
{
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private ChatEmojiCatalog _emojiCatalog = default!;
    [Dependency] private IGameTiming _timing = default!;

    public event Action<string>? OnEmojiPicked;
    private static readonly TimeSpan IconRefreshCooldown = TimeSpan.FromSeconds(0.5);
    private TextureRect? _icon;
    private ChatEmojiDefinition? _iconEmoji;
    private TimeSpan _nextIconRefresh;

    public EmojiPickerButton()
    {
        IoCManager.InjectDependencies(this);
        MinWidth = 34f;
        ToolTip = Loc.GetString("hud-chatbox-emoji-button-tooltip");
        RefreshIcon(force: true);
        _emojiCatalog.Changed += OnEmojiCatalogChanged;
        OnMouseEntered += _ => RefreshIcon();
        Popup.OnEmojiPicked += HandleEmojiPicked;
    }

    protected override UIBox2 GetPopupPosition()
    {
        const float margin = 8f;
        var size = _ui.RootControl.Size;
        var popupWidth = Math.Min(EmojiPickerPopup.PopupWidth, Math.Max(0f, size.X - margin * 2));
        var popupHeight = Math.Min(EmojiPickerPopup.PopupHeight, Math.Max(0f, size.Y - margin * 2));
        var x = Math.Clamp(GlobalPosition.X, margin, Math.Max(margin, size.X - popupWidth - margin));
        var above = GlobalPosition.Y - popupHeight - margin;
        var below = GlobalPosition.Y + Size.Y + margin;
        var y = above >= margin ? above : Math.Min(below, Math.Max(margin, size.Y - popupHeight - margin));
        var popupSize = new Vector2(popupWidth, popupHeight);
        Popup.MinSize = popupSize;
        return UIBox2.FromDimensions(new Vector2(x, y), popupSize);
    }

    public void SetAvailable(bool available)
    {
        Disabled = !available;
        if (!available && Popup.Visible)
            Popup.Close();
    }

    public void RefreshLocalization()
    {
        ToolTip = Loc.GetString("hud-chatbox-emoji-button-tooltip");
        Popup.RefreshLocalization();
    }

    private void HandleEmojiPicked(string emoji)
    {
        Popup.Close();
        OnEmojiPicked?.Invoke(emoji);
    }

    private void OnEmojiCatalogChanged()
    {
        RefreshIcon(force: true);
    }

    private void RefreshIcon(bool force = false)
    {
        if (!force && _timing.CurTime < _nextIconRefresh)
            return;

        if (!force)
            _nextIconRefresh = _timing.CurTime + IconRefreshCooldown;

        if (_icon != null)
            RemoveChild(_icon);

        _iconEmoji = _emojiCatalog.GetRandomEmoji(_iconEmoji);
        _icon = ChatEmojiRichText.CreateCategoryTextureRect(
            _resources,
            _iconEmoji.Value);
        AddChild(_icon);
    }

    protected override void ExitedTree()
    {

            _emojiCatalog.Changed -= OnEmojiCatalogChanged;
            Popup.OnEmojiPicked -= HandleEmojiPicked;


        base.ExitedTree();
    }
}
