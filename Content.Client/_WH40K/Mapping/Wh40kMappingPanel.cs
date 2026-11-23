using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client._WH40K.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Mapping;

public sealed class Wh40kMappingPanel : BoxContainer
{
    internal static readonly Vector2 WindowMinimum = new(420, 440);
    private static readonly Vector2 WindowDefault = new(670, 640);
    private const int SlotCount = 10;
    private const int RecentLimit = 25;
    private const int FavoriteLimit = 100;
    private const float SearchDelay = .14f;
    private const string All = "all";
    private const string Favorites = "favorites";
    private const string Recent = "recent";
    private static readonly Color Blue = Color.FromHex("#A0B7BF");
    private static readonly Color Red = Color.FromHex("#CF9181");
    private static readonly Color Violet = Color.FromHex("#B7A1C5");
    private static readonly Color Green = Color.FromHex("#93AE8A");
    private static readonly Color Sand = Color.FromHex("#C0A78F");

    private readonly IResourceManager _resources = IoCManager.Resolve<IResourceManager>();
    private readonly IClipboardManager _clipboard = IoCManager.Resolve<IClipboardManager>();
    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly WrapContainer _categories = new() { SeparationOverride = 4, CrossSeparationOverride = 3 };
    private readonly ScrollContainer _categoryScroll = new()
    {
        HScrollEnabled = false, VScrollBarHidden = false,
        ReserveScrollbarSpace = true, ReturnMeasure = true,
    };
    private readonly BoxContainer _slots = new() { SeparationOverride = 4 };
    private readonly Label _count = new() { FontColorOverride = Wh40kInterfaceTheme.MutedText };
    private readonly Label _empty = new() { Align = Label.AlignMode.Center, Visible = false };
    private readonly ScrollContainer _resultsScroll = new() { HScrollEnabled = false, VerticalExpand = true, ReserveScrollbarSpace = true };
    private readonly Wh40kMappingGrid _grid = new();
    private readonly BoxContainer _body = new() { VerticalExpand = true, SeparationOverride = 8, RectClipContent = true };
    private readonly PanelContainer _inspector = new() { PanelOverride = Wh40kInterfaceTheme.Card(7) };
    private readonly TextureRect _preview = Preview(43);
    private readonly Wh40kMappingLabel _previewName = new(2) { MouseFilter = MouseFilterMode.Pass, FontColorOverride = Wh40kInterfaceTheme.Gold };
    private readonly Wh40kMappingLabel _previewId = new(1) { MouseFilter = MouseFilterMode.Pass, FontColorOverride = Wh40kInterfaceTheme.MutedText };
    private readonly Button _pin = new() { Text = Loc.GetString("wh40k-mapping-pin"), VerticalAlignment = VAlignment.Center };
    private readonly Button _copy = new() { Text = Loc.GetString("wh40k-mapping-copy") };
    private readonly HashSet<string> _favorites = new(StringComparer.Ordinal);
    private readonly List<string> _recent = new();
    private readonly string?[] _slotIds = new string?[SlotCount];
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<Entry> _sorted = new();
    private readonly List<Entry> _filtered = new();
    private readonly List<Entry> _shown = new();
    private readonly Dictionary<string, int> _categoryCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ContainerButton Button, Label Count)> _categoryButtons = new(StringComparer.Ordinal);
    private string[] _categoryList = [];
    private string? _appliedQuery;
    private float _searchDelay;
    private ResPath? _storage;
    private string _category = All;
    private string? _selected;
    private (int First, int End, int Columns) _visible = (-1, -1, -1);
    private Color _tint = Color.White;

    internal readonly record struct Entry(string Id, string Name, string Category, Func<Texture?> Icon, string? EditorSuffix = null);

    public BoxContainer Options { get; } = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 5 };
    internal event Action<string>? EntrySelected;
    internal string? Selected => _selected;

    internal static Vector2 InitialWindowSize(Vector2 available) =>
        Vector2.Clamp(available - new Vector2(24, 32), WindowMinimum, WindowDefault);

    public Wh40kMappingPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        Margin = new Thickness(8);
        HorizontalExpand = true;
        VerticalExpand = true;
        AddStyleClass(Wh40kInterfaceTheme.ControlsStyleClass);

        var quick = HorizontalScroll();
        quick.AddChild(_slots);
        var quickHeading = Heading("wh40k-mapping-slots");
        quickHeading.ToolTip = Loc.GetString("wh40k-mapping-slots");
        _pin.AddStyleClass("Wh40kCompactButton");
        AddChild(Card(new BoxContainer
        {
            SeparationOverride = 6,
            Children = { quickHeading, quick, _pin },
        }, 5));
        OnResized += () => quickHeading.Text = Loc.GetString(Width < 710 ? "wh40k-mapping-slots-short" : "wh40k-mapping-slots");

        _search.PlaceHolder = Loc.GetString("wh40k-mapping-search");
        var clear = new Button { Text = Loc.GetString("wh40k-mapping-clear"), Disabled = true };
        clear.OnPressed += _ => { _search.SetText(string.Empty, invokeEvent: true); _search.GrabKeyboardFocus(); };
        _search.OnTextChanged += _ =>
        {
            clear.Disabled = _search.Text.Length == 0;
            _searchDelay = clear.Disabled ? 0 : SearchDelay;
            if (clear.Disabled)
                ApplyQuery(string.Empty);
        };
        AddChild(new BoxContainer { SeparationOverride = 5, Children = { _search, clear } });

        _categoryScroll.AddChild(_categories);
        AddChild(Card(_categoryScroll, 4));

        _resultsScroll.AddChild(_grid);
        var browse = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = Wh40kInterfaceTheme.Card(7),
            Children =
            {
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical, SeparationOverride = 5, VerticalExpand = true,
                    Children = { _count, _empty, _resultsScroll },
                },
            },
        };
        var optionsScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, ReserveScrollbarSpace = true };
        optionsScroll.AddChild(Options);
        _inspector.Children.Add(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical, SeparationOverride = 7, VerticalExpand = true,
            Children =
            {
                Card(new BoxContainer
                {
                    SeparationOverride = 6,
                    Children =
                    {
                        _preview,
                        new BoxContainer
                        {
                            Orientation = LayoutOrientation.Vertical, HorizontalExpand = true,
                            VerticalAlignment = VAlignment.Center,
                            Children = { _previewName, _previewId },
                        },
                    },
                }, 4),
                optionsScroll,
                _copy,
            },
        });
        _body.AddChild(browse);
        _body.AddChild(_inspector);
        AddChild(_body);
        OnResized += LayoutPanel;
        LayoutPanel();

        _copy.OnPressed += _ => { if (_selected != null) _clipboard.SetText(_selected); };
        _pin.OnPressed += _ =>
        {
            var index = Array.IndexOf(_slotIds, null);
            if (index >= 0 && _selected != null)
                AssignSlot(index, _selected);
        };
        UpdateSelection();
    }

    internal void LoadKit(string kind)
    {
        _storage = new ResPath($"/heretek-mapping-{kind}.txt");
        try
        {
            if (!_resources.UserData.Exists(_storage.Value))
                return;
            using var reader = _resources.UserData.OpenText(_storage.Value);
            for (var index = 0; index < SlotCount; index++)
            {
                var id = reader.ReadLine();
                _slotIds[index] = string.IsNullOrEmpty(id) ? null : id;
            }
            while (_favorites.Count < FavoriteLimit && reader.ReadLine() is { } favorite)
            {
                if (favorite.Length > 0)
                    _favorites.Add(favorite);
            }
        }
        catch (IOException e)
        {
            Logger.Warning($"Could not load mapping kit {kind}: {e.Message}");
        }
    }

    internal void SetEntries(IEnumerable<Entry> entries)
    {
        _entries.Clear();
        _sorted.Clear();
        foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(entry => entry.Id, StringComparer.Ordinal))
        {
            _entries.Add(entry.Id, entry);
            _sorted.Add(entry);
        }

        if (_selected is { } id && !_entries.ContainsKey(id))
            _selected = null;
        _categoryList = _sorted.Select(entry => entry.Category).Distinct().OrderBy(CategoryName).ToArray();
        if (_category != All && _category != Favorites && _category != Recent && !_categoryList.Contains(_category))
            _category = All;
        _categoryButtons.Clear();
        _categories.RemoveAllChildren();
        AddCategory(All);
        AddCategory(Favorites);
        AddCategory(Recent);
        foreach (var category in _categoryList)
            AddCategory(category);
        _appliedQuery = null;
        ApplyQuery(_search.Text.Trim());
        UpdateSelection();
    }

    internal void FocusSearch() => _search.GrabKeyboardFocus();

    internal bool Select(string id)
    {
        if (!_entries.ContainsKey(id))
            return false;
        _selected = id;
        _recent.Remove(id);
        _recent.Insert(0, id);
        if (_recent.Count > RecentLimit)
            _recent.RemoveAt(RecentLimit);
        RefreshResults(resetScroll: false);
        UpdateSelection();
        EntrySelected?.Invoke(id);
        return true;
    }

    internal void ClearSelection()
    {
        if (_selected == null)
            return;
        _selected = null;
        _visible = (-1, -1, -1);
        UpdateSelection();
    }

    internal void SetTint(Color tint)
    {
        if (_tint == tint)
            return;
        _tint = tint;
        _preview.Modulate = tint;
        _visible = (-1, -1, -1);
        RenderSlots();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree)
            return;
        if (_searchDelay > 0 && (_searchDelay -= args.DeltaSeconds) <= 0)
            ApplyQuery(_search.Text.Trim());
        if (_resultsScroll.Height <= 0)
            return;
        var firstRow = Math.Max(0, (int) (_resultsScroll.VScroll / Wh40kMappingGrid.RowHeight));
        var first = Math.Min(_shown.Count, firstRow * _grid.Columns);
        var last = Math.Min(_shown.Count,
            (firstRow + 2 + (int) MathF.Ceiling(_resultsScroll.Height / Wh40kMappingGrid.RowHeight)) * _grid.Columns);
        var range = (first, last, _grid.Columns);
        if (_visible == range)
            return;
        _visible = range;
        _grid.FirstRow = firstRow;
        _grid.RemoveAllChildren();
        for (var i = first; i < last; i++)
            _grid.AddChild(MakeCard(_shown[i]));
    }

    private void LayoutPanel()
    {
        _inspector.SetWidth = Math.Clamp(Width * .34f, 177, 255);
        _categoryScroll.MaxHeight = Width < 535 || Height < 440 ? 55 : 113;
    }

    private void ApplyQuery(string query)
    {
        if (string.Equals(_appliedQuery, query, StringComparison.OrdinalIgnoreCase))
            return;
        if (_appliedQuery != null && query.StartsWith(_appliedQuery, StringComparison.OrdinalIgnoreCase))
        {
            if (_filtered.Count == 0)
            {
                _appliedQuery = query;
                return;
            }
            _filtered.RemoveAll(entry => !Matches(entry, query));
        }
        else
        {
            _filtered.Clear();
            foreach (var entry in _sorted)
            {
                if (Matches(entry, query))
                    _filtered.Add(entry);
            }
        }
        _appliedQuery = query;
        _categoryCounts.Clear();
        foreach (var entry in _filtered)
            _categoryCounts[entry.Category] = _categoryCounts.GetValueOrDefault(entry.Category) + 1;
        _categoryScroll.VScroll = _categoryScroll.VScrollTarget = 0;
        RefreshResults();
    }

    private void RefreshResults(bool resetScroll = true)
    {
        var query = _appliedQuery ?? string.Empty;
        var recent = _recent.Where(id => _entries.TryGetValue(id, out var entry) && Matches(entry, query)).ToList();
        var favorites = _favorites.Count(id => _entries.TryGetValue(id, out var entry) && Matches(entry, query));
        foreach (var (category, (button, countLabel)) in _categoryButtons)
        {
            var count = category switch
            {
                All => _filtered.Count,
                Recent => recent.Count,
                Favorites => favorites,
                _ => _categoryCounts.GetValueOrDefault(category),
            };
            button.Pressed = _category == category;
            button.Visible = count > 0 || query.Length == 0 || category is All or Favorites or Recent || _category == category;
            var formattedCount = count.ToString("N0");
            if (countLabel.Text != formattedCount)
                countLabel.Text = formattedCount;
        }

        _shown.Clear();
        if (_category == Recent)
        {
            foreach (var id in recent)
                _shown.Add(_entries[id]);
        }
        else
        {
            _shown.AddRange(_filtered.Where(entry => _category == All ||
                (_category == Favorites ? _favorites.Contains(entry.Id) : entry.Category == _category)));
        }

        _grid.Count = _shown.Count;
        _empty.Visible = _shown.Count == 0;
        _empty.Text = Loc.GetString("wh40k-mapping-empty");
        _count.Text = Loc.GetString("wh40k-mapping-count", ("category", CategoryName(_category)), ("count", _shown.Count));
        _visible = (-1, -1, -1);
        if (resetScroll)
            _resultsScroll.VScroll = _resultsScroll.VScrollTarget = 0;
    }

    private void AddCategory(string category)
    {
        var count = new Label { VAlign = Label.VAlignMode.Center, FontColorOverride = Wh40kInterfaceTheme.MutedText };
        var button = new ContainerButton
        {
            MinHeight = 0, SetHeight = 26, ToggleMode = true, Pressed = _category == category,
            Children =
            {
                new BoxContainer
                {
                    MouseFilter = MouseFilterMode.Ignore, SeparationOverride = 5,
                    Children =
                    {
                        new PanelContainer
                        {
                            MouseFilter = MouseFilterMode.Ignore, VerticalAlignment = VAlignment.Center,
                            SetSize = new Vector2(3, 15), PanelOverride = new StyleBoxFlat(CategoryColor(category)),
                        },
                        new Label { Text = CategoryName(category), VAlign = Label.VAlignMode.Center },
                        count,
                    },
                },
            },
        };
        button.AddStyleClass(ContainerButton.StyleClassButton);
        button.AddStyleClass("Wh40kCompactButton");
        _categoryButtons.Add(category, (button, count));
        button.OnPressed += _ => { _category = category; RefreshResults(); };
        _categories.AddChild(button);
    }

    private Control MakeCard(Entry entry)
    {
        var text = new Wh40kMappingLabel(2) { FullText = entry.Name };
        var texture = Preview(54);
        texture.Texture = entry.Icon();
        texture.Modulate = _tint;
        var body = new ContainerButton
        {
            MouseFilter = MouseFilterMode.Stop, HorizontalExpand = true, VerticalExpand = true,
            ToggleMode = true, Pressed = _selected == entry.Id,
            ToolTip = string.IsNullOrWhiteSpace(entry.EditorSuffix) ? $"{entry.Name}\n{entry.Id}" : $"{entry.Name}\n{entry.EditorSuffix}\n{entry.Id}",
            Children =
            {
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical, SeparationOverride = 2, Margin = new Thickness(4, 3),
                    MouseFilter = MouseFilterMode.Ignore,
                    Children =
                    {
                        new PanelContainer
                        {
                            MouseFilter = MouseFilterMode.Ignore, PanelOverride = new StyleBoxFlat(Wh40kInterfaceTheme.TableAlternateRow),
                            Children = { texture },
                        },
                        text,
                    },
                },
            },
        };
        body.AddStyleClass(ContainerButton.StyleClassButton);
        body.AddStyleClass("Wh40kCompactButton");
        body.OnPressed += _ => Select(entry.Id);
        var isFavorite = _favorites.Contains(entry.Id);
        var favoritesFull = !isFavorite && _favorites.Count >= FavoriteLimit;
        var favorite = new Button
        {
            Text = isFavorite ? "★" : "☆",
            HorizontalAlignment = HAlignment.Right, VerticalAlignment = VAlignment.Top,
            Disabled = favoritesFull,
            MinSize = Vector2.Zero,
            SetSize = new Vector2(23, 23),
            ToolTip = favoritesFull ? Loc.GetString("wh40k-mapping-favorites-limit", ("count", FavoriteLimit)) : CategoryName(Favorites),
            Margin = new Thickness(0, 2, 2, 0),
        };
        favorite.AddStyleClass("Wh40kCompactButton");
        favorite.OnPressed += _ =>
        {
            if (!_favorites.Remove(entry.Id))
            {
                if (_favorites.Count >= FavoriteLimit)
                    return;
                _favorites.Add(entry.Id);
            }
            SaveKit();
            RefreshResults(resetScroll: false);
        };
        var accent = CategoryColor(entry.Category);
        return new BoxContainer
        {
            Children =
            {
                new PanelContainer { SetWidth = 2, PanelOverride = new StyleBoxFlat(accent) },
                new Control { HorizontalExpand = true, Children = { body, favorite } },
            },
        };
    }

    private void UpdateSelection()
    {
        var entry = _selected != null && _entries.TryGetValue(_selected, out var selected) ? selected : (Entry?) null;
        _preview.Texture = entry?.Icon();
        _preview.Visible = _preview.Texture != null;
        _previewName.FullText = entry?.Name ?? Loc.GetString("wh40k-mapping-none");
        _previewName.ToolTip = entry?.Name;
        _previewId.Visible = entry != null && entry.Value.Name != entry.Value.Id;
        _previewId.FullText = entry?.Id ?? string.Empty;
        _previewId.ToolTip = entry?.Id;
        _copy.Disabled = entry == null;
        _pin.Disabled = entry == null || _slotIds.Contains(entry.Value.Id) || Array.IndexOf(_slotIds, null) < 0;
        RenderSlots();
    }

    private void RenderSlots()
    {
        _slots.RemoveAllChildren();
        for (var i = 0; i < SlotCount; i++)
        {
            var index = i;
            var id = _slotIds[index];
            var icon = Preview(17);
            var entry = id != null && _entries.TryGetValue(id, out var found) ? found : (Entry?) null;
            icon.Texture = entry?.Icon();
            icon.Modulate = _tint;
            Control visual = entry != null ? icon : new Label { Text = "+", SetWidth = 17, Align = Label.AlignMode.Center };
            var slot = new ContainerButton
            {
                SetWidth = 43, SetHeight = 30, EnableAllKeybinds = true, ToggleMode = true,
                Pressed = id != null && id == _selected,
                ToolTip = Loc.GetString("wh40k-mapping-slot-tip", ("name", id ?? "+")),
                Children =
                {
                    new BoxContainer
                    {
                        MouseFilter = MouseFilterMode.Ignore, SeparationOverride = 2, HorizontalAlignment = HAlignment.Center,
                        Children =
                        {
                            new Label { Text = (index + 1).ToString(), FontColorOverride = Wh40kInterfaceTheme.Gold },
                            visual,
                        },
                    },
                },
            };
            slot.AddStyleClass(ContainerButton.StyleClassButton);
            slot.AddStyleClass("Wh40kCompactButton");
            slot.OnPressed += args =>
            {
                if (args.Event.Function == EngineKeyFunctions.UIRightClick)
                    AssignSlot(index, null);
                else if (args.Event.Function == EngineKeyFunctions.UIClick && id != null)
                    Select(id);
                else if (args.Event.Function == EngineKeyFunctions.UIClick && _selected != null)
                    AssignSlot(index, _selected);
            };
            _slots.AddChild(slot);
        }
    }

    private void AssignSlot(int index, string? id)
    {
        _slotIds[index] = id;
        SaveKit();
        UpdateSelection();
    }

    private void SaveKit()
    {
        if (_storage is not { } path)
            return;
        try
        {
            using var writer = _resources.UserData.OpenWriteText(path);
            foreach (var id in _slotIds)
                writer.WriteLine(id);
            foreach (var favorite in _favorites)
                writer.WriteLine(favorite);
        }
        catch (IOException e)
        {
            Logger.Warning($"Could not save mapping kit: {e.Message}");
        }
    }

    private static bool Matches(Entry entry, string query) => query.Length == 0 ||
        entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        entry.EditorSuffix?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    private static Color CategoryColor(string category) => category switch
    {
        All or Favorites or "engineering" or "safety" or "power" or "lighting" => Wh40kInterfaceTheme.Gold,
        "medical" or "machines" or "glass" or "snow" or "devices" or "liquids" => Blue,
        "weapons" or "food" or "lines" or "ammunition" => Red,
        "clothing" or "overlays" or "stencils" or "science" or "effects" or "fun" or "graffiti" => Violet,
        "mobs" or "terrain" or "markings" or "botany" or "nature" or "vegetation" or "vehicles" or "atmospherics" => Green,
        "structures" or "finish" or "floors" or "plating" or "furniture" or "storage" or "streets" or "decor" or "materials" => Sand,
        _ => Wh40kInterfaceTheme.MutedText,
    };
    private static string CategoryName(string category) => Loc.GetString($"wh40k-mapping-category-{category}");
    private static Label Heading(string key) => new() { Text = Loc.GetString(key), FontColorOverride = Wh40kInterfaceTheme.Gold };
    private static PanelContainer Card(Control child, int padding) =>
        new() { PanelOverride = Wh40kInterfaceTheme.Card(padding), Children = { child } };
    private static ScrollContainer HorizontalScroll() =>
        new()
        {
            HScrollEnabled = true, VScrollEnabled = false, HScrollBarHidden = false,
            FallbackDeltaScroll = true, ReserveScrollbarSpace = true, SetHeight = 38, HorizontalExpand = true,
        };
    private static TextureRect Preview(float size) =>
        new()
        {
            SetSize = new Vector2(size), Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MouseFilter = MouseFilterMode.Ignore, HorizontalAlignment = HAlignment.Center, VerticalAlignment = VAlignment.Center,
        };
}
