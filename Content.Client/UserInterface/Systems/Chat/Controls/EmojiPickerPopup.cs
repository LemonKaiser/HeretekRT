using System.Numerics;
using System.Linq;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.Chat.RichText;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

/// <summary>
/// Searchable emoji palette following the Heretek dark-brass UI palette. It keeps the active grid alive between
/// openings and persists recent/favourite aliases locally.
/// </summary>
public sealed partial class EmojiPickerPopup : Popup
{
    public const float PopupWidth = 440f;
    public const float PopupHeight = 440f;

    private const int EmojiColumns = 6;
    private const int RecentLimit = 24;
    private const float EmojiButtonSize = 48f;
    private static readonly Color PanelBackgroundColor = Color.FromHex("#0A0907E8");
    private static readonly Color RailBackgroundColor = Color.FromHex("#070605C2");
    private static readonly Color ContentBackgroundColor = Color.FromHex("#0D0B08D1");
    private static readonly Color BorderColor = Color.FromHex("#B6975499");
    private static readonly Color HeaderTextColor = Color.FromHex("#E5C879");
    private static readonly Color PreviewTextColor = Color.FromHex("#DFD7C3");
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private ChatEmojiCatalog _catalog = default!;

    private readonly Dictionary<ChatEmojiCategory, Button> _categoryButtons = new();
    private readonly BoxContainer _categoryBox;
    private readonly GridContainer _emojiGrid;
    private readonly Label _header;
    private readonly Label _emptyResult;
    private readonly RichTextLabel _preview;
    private readonly LineEdit _search;
    private readonly Button _favoritesButton;
    private readonly Button _recentButton;
    private readonly Button _toggleFavoriteButton;
    private readonly HeretekRoundedStyleBox _searchNormalStyle;
    private readonly HeretekRoundedStyleBox _searchFocusStyle;
    private readonly HeretekRoundedStyleBox _buttonNormalStyle;
    private readonly HeretekRoundedStyleBox _buttonHoverStyle;
    private readonly HeretekRoundedStyleBox _buttonPressedStyle;
    private readonly HeretekRoundedStyleBox _buttonSelectedStyle;
    private readonly HeretekRoundedStyleBox _buttonDisabledStyle;
    private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recent = new();

    private ChatEmojiCategory _selectedCategory = ChatEmojiCategory.Smileys;
    private PickerView _view = PickerView.Category;
    private ChatEmojiDefinition? _previewEmoji;
    private bool _catalogueDirty = true;
    private bool _categorySelectedByUser;

    public event Action<string>? OnEmojiPicked;

    public EmojiPickerPopup()
    {
        IoCManager.InjectDependencies(this);
        _searchNormalStyle = CreateSurfaceStyle("#080705D9", "#B89F5760");
        _searchFocusStyle = CreateSurfaceStyle("#030302D9", "#E5C879");
        _searchNormalStyle.ContentMarginLeftOverride = 6f;
        _searchFocusStyle.ContentMarginLeftOverride = 6f;
        _buttonNormalStyle = CreateSurfaceStyle("#120F0AD0", "#9E86504D");
        _buttonHoverStyle = CreateSurfaceStyle("#2A2111E8", "#E5C879");
        _buttonPressedStyle = CreateSurfaceStyle("#4A3816E8", "#FFF1C8");
        _buttonSelectedStyle = CreateSurfaceStyle("#392D12E0", "#E5C879");
        _buttonDisabledStyle = CreateSurfaceStyle("#09080680", "#5C4C2F4D");
        MinSize = new Vector2(PopupWidth, PopupHeight);

        foreach (var alias in ReadAliases(CCVars.ChatEmojiFavorites))
            _favorites.Add(alias);
        _recent.AddRange(ReadAliases(CCVars.ChatEmojiRecent));

        var panel = new PanelContainer
        {
            MinSize = MinSize,
            PanelOverride = CreateSurfaceStyle(PanelBackgroundColor, BorderColor, 8f),
        };
        AddChild(panel);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(8),
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        panel.AddChild(root);

        var rail = new PanelContainer
        {
            MinWidth = 42f,
            PanelOverride = CreateSurfaceStyle(RailBackgroundColor, BorderColor.WithAlpha(0.8f), 6f),
        };
        root.AddChild(rail);

        _categoryBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(4),
        };
        rail.AddChild(_categoryBox);

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        root.AddChild(content);

        var toolbar = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        content.AddChild(toolbar);

        _search = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("hud-chatbox-emoji-search-placeholder"),
            StyleBoxOverride = _searchNormalStyle,
        };
        _search.OnTextChanged += _ => RebuildGrid();
        _search.OnTextEntered += _ => PickFirstVisibleEmoji();
        _search.OnFocusEnter += _ => _search.StyleBoxOverride = _searchFocusStyle;
        _search.OnFocusExit += _ => _search.StyleBoxOverride = _searchNormalStyle;
        toolbar.AddChild(_search);

        _recentButton = CreateToolbarButton("◷", "hud-chatbox-emoji-recent-tooltip");
        _recentButton.OnPressed += _ => SelectView(PickerView.Recent);
        toolbar.AddChild(_recentButton);

        _favoritesButton = CreateToolbarButton("★", "hud-chatbox-emoji-favorites-tooltip");
        _favoritesButton.OnPressed += _ => SelectView(PickerView.Favorites);
        toolbar.AddChild(_favoritesButton);

        _header = new Label
        {
            Margin = new Thickness(4, 0, 4, 0),
            FontColorOverride = HeaderTextColor,
        };
        content.AddChild(_header);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            ReserveScrollbarSpace = true,
        };
        content.AddChild(scroll);

        var emojiPanel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = CreateSurfaceStyle(ContentBackgroundColor, BorderColor.WithAlpha(0.8f), 6f),
        };
        scroll.AddChild(emojiPanel);

        _emojiGrid = new GridContainer
        {
            Columns = EmojiColumns,
            HSeparationOverride = 10,
            VSeparationOverride = 10,
            Margin = new Thickness(10, 10, 14, 10),
        };
        emojiPanel.AddChild(_emojiGrid);

        _emptyResult = new Label
        {
            Visible = false,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = PreviewTextColor,
        };
        emojiPanel.AddChild(_emptyResult);

        var previewBar = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        content.AddChild(previewBar);

        _preview = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(4, 0, 4, 0),
            ModulateSelfOverride = PreviewTextColor,
        };
        previewBar.AddChild(_preview);

        _toggleFavoriteButton = CreateToolbarButton("☆", "hud-chatbox-emoji-favorite-add-tooltip");
        _toggleFavoriteButton.Disabled = true;
        _toggleFavoriteButton.OnPressed += _ => TogglePreviewFavorite();
        previewBar.AddChild(_toggleFavoriteButton);

        _catalog.Changed += OnCatalogChanged;
        OnPopupOpen += HandlePopupOpen;
        EnsureCatalogue();
        SelectCategory(GetDefaultCategory());
    }

    public void RefreshLocalization()
    {
        _search.PlaceHolder = Loc.GetString("hud-chatbox-emoji-search-placeholder");
        _recentButton.ToolTip = Loc.GetString("hud-chatbox-emoji-recent-tooltip");
        _favoritesButton.ToolTip = Loc.GetString("hud-chatbox-emoji-favorites-tooltip");
        RefreshFavoriteButton();
        RebuildCategoryButtons();
        RebuildGrid();
    }

    private void HandlePopupOpen()
    {
        EnsureCatalogue();
        RebuildGrid();
        _search.GrabKeyboardFocus();
    }

    private void OnCatalogChanged()
    {
        _catalogueDirty = true;
    }

    private void EnsureCatalogue()
    {
        if (!_catalogueDirty)
            return;

        _catalogueDirty = false;
        RebuildCategoryButtons();
        if (!_categoryButtons.ContainsKey(_selectedCategory))
            _selectedCategory = GetDefaultCategory();
    }

    private void SelectCategory(ChatEmojiCategory category, bool selectedByUser = false)
    {
        EnsureCatalogue();
        if (!_categoryButtons.ContainsKey(category))
            category = GetDefaultCategory();

        _selectedCategory = category;
        _categorySelectedByUser |= selectedByUser;
        _view = PickerView.Category;
        RefreshCategoryButtonStyles();
        RefreshViewButtonStyles();
        RebuildGrid();
    }

    private void SelectView(PickerView view)
    {
        _view = view;
        RefreshViewButtonStyles();
        RebuildGrid();
    }

    private void RebuildGrid()
    {
        var emojis = GetVisibleEmoji().ToArray();
        _header.Text = _view switch
        {
            PickerView.Favorites => Loc.GetString("hud-chatbox-emoji-favorites"),
            PickerView.Recent => Loc.GetString("hud-chatbox-emoji-recent"),
            _ => GetCategoryName(_selectedCategory),
        };
        _emojiGrid.RemoveAllChildren();
        _emptyResult.Visible = emojis.Length == 0;
        _emptyResult.Text = Loc.GetString("hud-chatbox-emoji-no-results");
        _previewEmoji = null;
        _preview.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("hud-chatbox-emoji-preview-empty")), tagsAllowed: null);
        RefreshFavoriteButton();

        foreach (var emoji in emojis)
        {
            var emojiButton = new Button
            {
                MinSize = new Vector2(EmojiButtonSize, EmojiButtonSize),
                ToolTip = emoji.InsertText,
            };
            ConfigureThemedButton(emojiButton);
            emojiButton.AddChild(ChatEmojiRichText.CreatePickerTextureRect(_resources, emoji));
            emojiButton.OnPressed += _ => PickEmoji(emoji);
            emojiButton.OnMouseEntered += _ => PreviewEmoji(emoji);
            _emojiGrid.AddChild(emojiButton);
        }
    }

    private IEnumerable<ChatEmojiDefinition> GetVisibleEmoji()
    {
        var query = _search.Text;
        return _view switch
        {
            PickerView.Favorites => ResolveAliases(_favorites, query),
            PickerView.Recent => ResolveAliases(_recent, query),
            _ => _catalog.Search(_selectedCategory, query),
        };
    }

    private IEnumerable<ChatEmojiDefinition> ResolveAliases(IEnumerable<string> aliases, string query)
    {
        foreach (var alias in aliases)
        {
            if (!_catalog.TryGet(alias, out var emoji))
                continue;

            if (!string.IsNullOrWhiteSpace(query) &&
                !emoji.Alias.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                emoji.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) != true &&
                emoji.Keywords?.Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase)) != true)
                continue;

            yield return emoji;
        }
    }

    private void RebuildCategoryButtons()
    {
        _categoryButtons.Clear();
        _categoryBox.RemoveAllChildren();
        foreach (var category in _catalog.GetCategoryOrder())
        {
            var button = new Button
            {
                MinSize = new Vector2(32f, 32f),
                ToolTip = GetCategoryName(category),
            };
            ConfigureThemedButton(button, () => _view == PickerView.Category && category == _selectedCategory);
            button.AddChild(ChatEmojiRichText.CreateCategoryTextureRect(_resources, _catalog.GetCategoryIcon(category)));
            button.OnPressed += _ => SelectCategory(category, selectedByUser: true);
            _categoryButtons[category] = button;
            _categoryBox.AddChild(button);
        }

        // The catalogue can be rebuilt after custom prototypes finish loading. Until the player has chosen a
        // category themselves, prefer the intended default once it becomes available.
        if (!_categorySelectedByUser && _categoryButtons.ContainsKey(ChatEmojiCategory.Custom))
            _selectedCategory = ChatEmojiCategory.Custom;
        else if (!_categoryButtons.ContainsKey(_selectedCategory))
            _selectedCategory = GetDefaultCategory();

        RefreshCategoryButtonStyles();
    }

    private void PreviewEmoji(ChatEmojiDefinition emoji)
    {
        _previewEmoji = emoji;
        _preview.SetMessage(ChatEmojiRichText.BuildPreviewMessage(emoji), tagsAllowed: null);
        RefreshFavoriteButton();
    }

    private void PickEmoji(ChatEmojiDefinition emoji)
    {
        RememberRecent(emoji.Alias);
        OnEmojiPicked?.Invoke(emoji.InsertText);
    }

    private void PickFirstVisibleEmoji()
    {
        var emoji = GetVisibleEmoji().FirstOrDefault();
        if (string.IsNullOrEmpty(emoji.Alias))
            return;

        PickEmoji(emoji);
    }

    private void TogglePreviewFavorite()
    {
        if (_previewEmoji is not { } emoji)
            return;

        if (!_favorites.Add(emoji.Alias))
            _favorites.Remove(emoji.Alias);
        SaveFavorites();
        RefreshFavoriteButton();
        if (_view == PickerView.Favorites)
            RebuildGrid();
    }

    private void RefreshFavoriteButton()
    {
        var isFavorite = _previewEmoji is { } emoji && _favorites.Contains(emoji.Alias);
        _toggleFavoriteButton.Disabled = _previewEmoji == null;
        _toggleFavoriteButton.Text = isFavorite ? "★" : "☆";
        _toggleFavoriteButton.ToolTip = Loc.GetString(isFavorite
            ? "hud-chatbox-emoji-favorite-remove-tooltip"
            : "hud-chatbox-emoji-favorite-add-tooltip");
        ApplyThemedButtonStyle(
            _toggleFavoriteButton,
            _toggleFavoriteButton.Disabled ? PickerButtonVisual.Disabled : PickerButtonVisual.Normal);
    }

    private void RememberRecent(string alias)
    {
        _recent.RemoveAll(existing => string.Equals(existing, alias, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, alias);
        if (_recent.Count > RecentLimit)
            _recent.RemoveRange(RecentLimit, _recent.Count - RecentLimit);
        _config.SetCVar(CCVars.ChatEmojiRecent, string.Join(',', _recent));
    }

    private void SaveFavorites()
    {
        _config.SetCVar(
            CCVars.ChatEmojiFavorites,
            string.Join(',', _favorites.OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)));
    }

    private IEnumerable<string> ReadAliases(CVarDef<string> cvar)
    {
        return _config.GetCVar(cvar)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(alias => _catalog.TryGet(alias, out _));
    }

    private ChatEmojiCategory GetDefaultCategory()
        => _categoryButtons.ContainsKey(ChatEmojiCategory.Custom)
            ? ChatEmojiCategory.Custom
            : ChatEmojiCategory.Smileys;

    private Button CreateToolbarButton(string text, string tooltipKey)
    {
        var button = new Button
        {
            Text = text,
            MinSize = new Vector2(28f, 28f),
            ToolTip = Loc.GetString(tooltipKey),
        };
        ConfigureThemedButton(button);
        return button;
    }

    private void RefreshCategoryButtonStyles()
    {
        foreach (var (category, button) in _categoryButtons)
        {
            var selected = _view == PickerView.Category && category == _selectedCategory;
            ApplyThemedButtonStyle(button, selected ? PickerButtonVisual.Selected : PickerButtonVisual.Normal);
        }
    }

    private void RefreshViewButtonStyles()
    {
        ApplyThemedButtonStyle(
            _recentButton,
            _view == PickerView.Recent ? PickerButtonVisual.Selected : PickerButtonVisual.Normal);
        ApplyThemedButtonStyle(
            _favoritesButton,
            _view == PickerView.Favorites ? PickerButtonVisual.Selected : PickerButtonVisual.Normal);
    }

    private void ConfigureThemedButton(Button button, Func<bool>? selected = null)
    {
        ApplyThemedButtonStyle(button, PickerButtonVisual.Normal);
        button.OnMouseEntered += _ =>
        {
            if (!button.Disabled)
                ApplyThemedButtonStyle(button, selected?.Invoke() == true ? PickerButtonVisual.Selected : PickerButtonVisual.Hover);
        };
        button.OnMouseExited += _ => ApplyThemedButtonStyle(
            button,
            button.Disabled
                ? PickerButtonVisual.Disabled
                : selected?.Invoke() == true
                    ? PickerButtonVisual.Selected
                    : PickerButtonVisual.Normal);
        button.OnButtonDown += _ =>
        {
            if (!button.Disabled)
                ApplyThemedButtonStyle(button, PickerButtonVisual.Pressed);
        };
        button.OnButtonUp += _ =>
        {
            if (!button.Disabled)
                ApplyThemedButtonStyle(button, selected?.Invoke() == true ? PickerButtonVisual.Selected : PickerButtonVisual.Hover);
        };
    }

    private void ApplyThemedButtonStyle(Button button, PickerButtonVisual visual)
    {
        button.StyleBoxOverride = visual switch
        {
            PickerButtonVisual.Hover => _buttonHoverStyle,
            PickerButtonVisual.Pressed => _buttonPressedStyle,
            PickerButtonVisual.Selected => _buttonSelectedStyle,
            PickerButtonVisual.Disabled => _buttonDisabledStyle,
            _ => _buttonNormalStyle,
        };
        button.Label.FontColorOverride = visual is PickerButtonVisual.Hover or PickerButtonVisual.Selected
            ? HeaderTextColor
            : PreviewTextColor;
    }

    private HeretekRoundedStyleBox CreateSurfaceStyle(string background, string border)
        => CreateSurfaceStyle(Color.FromHex(background), Color.FromHex(border), 5f);

    private HeretekRoundedStyleBox CreateSurfaceStyle(Color background, Color border, float cornerRadius)
    {
        return new HeretekRoundedStyleBox
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(1f),
            CornerRadius = cornerRadius,
        };
    }

    private static string GetCategoryName(ChatEmojiCategory category)
        => Loc.GetString($"hud-chatbox-emoji-category-{category}");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _catalog.Changed -= OnCatalogChanged;
    }

    private enum PickerView : byte
    {
        Category,
        Favorites,
        Recent,
    }

    private enum PickerButtonVisual : byte
    {
        Normal,
        Hover,
        Pressed,
        Selected,
        Disabled,
    }
}
