using System.Linq;
using System.Numerics;
using Content.Client._WH40K.CharacterCreation;
using Content.Client._WH40K.UserInterface;
using Content.Shared.Humanoid.Markings;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;
using Thumbnail = Content.Client._WH40K.CharacterCreation.Wh40kPreviewGrid.SpriteThumbnail;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    private readonly Dictionary<string, Thumbnail> _galleryTilePreviews = new();
    private readonly Dictionary<string, Thumbnail> _galleryRowPreviews = new();
    private readonly Dictionary<string, PanelContainer> _galleryChips = new();
    private string? _gallerySelection;
    private Marking? _galleryPending;
    private Thumbnail? _galleryDetailPreview;
    private Button _galleryApply = default!;
    private Button _galleryUp = default!;
    private Button _galleryDown = default!;
    private Button _galleryRemove = default!;
    private Control _galleryActionSpacer = default!;

    private void InitializeGallery()
    {
        GalleryCatalog.PanelOverride = Wh40kInterfaceTheme.Card(11);
        GalleryLayers.PanelOverride = Wh40kInterfaceTheme.Card(11);
        GalleryDetails.PanelOverride = Wh40kInterfaceTheme.Card(11);
        GallerySample.PanelOverride = Wh40kInterfaceTheme.Card(3);
        GalleryDetailCategory.FontColorOverride = Wh40kInterfaceTheme.MutedText;
        GalleryLayerCount.FontColorOverride = Wh40kInterfaceTheme.MutedText;
        GalleryPoints.FontColorOverride = Wh40kInterfaceTheme.MutedText;

        _galleryApply = AddGalleryAction("GalleryApply", "wh40k-personalization-markings-apply", () =>
        {
            if (_gallerySelection != null && GetMarkings(_selectedMarkingCategory).TryGetValue(_gallerySelection, out var marking))
                AddMarking(marking);
        });
        _galleryUp = AddGalleryAction("GalleryUp", "markings-rank-up", () => MoveGalleryMarking(-1));
        _galleryDown = AddGalleryAction("GalleryDown", "markings-rank-down", () => MoveGalleryMarking(1));
        _galleryActionSpacer = new Control { HorizontalExpand = true };
        GalleryActions.AddChild(_galleryActionSpacer);
        _galleryRemove = AddGalleryAction("GalleryRemove", "wh40k-personalization-markings-remove", RemoveGalleryMarking);
        _galleryRemove.AddStyleClass("Caution");

        GallerySearch.OnTextChanged += args => PopulateGallery(args.Text);
        OnResized += LayoutGallery;
        GalleryCatalog.OnResized += () =>
            GalleryHeader.Orientation = GalleryCatalog.Width < 400 ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
    }

    private Button AddGalleryAction(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = Loc.GetString(text) };
        button.OnPressed += _ => action();
        GalleryActions.AddChild(button);
        return button;
    }

    private void LayoutGallery()
    {
        if (!UseTiles)
            return;

        CategoryTiles.MaxGridWidth = Math.Max(1, Width);
        var wide = Width >= 760;
        GalleryColumns.Orientation = wide ? LayoutOrientation.Horizontal : LayoutOrientation.Vertical;
        var sideWidth = Math.Clamp(Width * 0.31f, 307, 365);
        GalleryCatalog.HorizontalExpand = !wide;
        GalleryCatalog.SetWidth = wide ? Width - sideWidth - GalleryColumns.SeparationOverride.GetValueOrDefault() : float.NaN;
        GallerySide.SetWidth = wide ? sideWidth : float.NaN;
        GalleryScroll.MaxHeight = wide ? 365 : 252;
    }

    private static string GalleryCategoryName(MarkingCategories category) =>
        Loc.GetString(category == MarkingCategories.Snout ? "wh40k-personalization-markings-face" : $"markings-category-{category}");

    private void PopulateGallery(string filter)
    {
        var available = GetMarkings(_selectedMarkingCategory);
        if (_gallerySelection == null || !available.ContainsKey(_gallerySelection))
        {
            _gallerySelection = _currentMarkings.GetReverseEnumerator(_selectedMarkingCategory)
                .FirstOrDefault(marking => available.ContainsKey(marking.MarkingId))?.MarkingId;
            _galleryPending = null;
        }

        Tiles.ClearTiles();
        _galleryTilePreviews.Clear();
        var options = available.Values.Select(marking => (Marking: marking, Name: GetMarkingName(marking)))
            .Where(entry => entry.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                            entry.Marking.ID.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name, StringComparer.CurrentCulture);

        foreach (var (marking, name) in options)
        {
            var applied = _currentMarkings.TryGetMarking(_selectedMarkingCategory, marking.ID, out var current);
            var colors = current?.MarkingColors ?? (_galleryPending?.MarkingId == marking.ID ? _galleryPending.MarkingColors : null);
            _galleryTilePreviews.Add(marking.ID, Tiles.AddGalleryTile(name, marking.Sprites, colors,
                marking.ID == _gallerySelection, applied, () => SelectGalleryMarking(marking)));
        }

        GalleryTitle.Text = $"{GalleryCategoryName(_selectedMarkingCategory)}  ·  {Tiles.ChildCount}";
        var points = _currentMarkings.PointsLeft(_selectedMarkingCategory);
        GalleryPoints.Visible = points >= 0;
        if (points >= 0)
            GalleryPoints.Text = Loc.GetString("marking-points-remaining", ("points", points));
        GalleryScroll.Visible = Tiles.ChildCount > 0;
        GalleryEmpty.Visible = !GalleryScroll.Visible;
    }

    internal void SelectGalleryMarking(MarkingPrototype marking)
    {
        if (marking.MarkingCategory != _selectedMarkingCategory)
        {
            _selectedMarkingCategory = marking.MarkingCategory;
            GallerySearch.SetText(string.Empty, invokeEvent: false);
            GalleryScroll.VScroll = 0;
            SetupCategoryButtons();
        }

        if (_gallerySelection != marking.ID)
            _galleryPending = null;
        _gallerySelection = marking.ID;
        if (!_currentMarkings.TryGetMarking(_selectedMarkingCategory, marking.ID, out _))
            _galleryPending ??= CreateMarking(marking);

        PopulateGallery(GallerySearch.Text);
        PopulateGallerySide();
    }

    private void PopulateGallerySide()
    {
        GalleryRows.DisposeAllChildren();
        _galleryRowPreviews.Clear();
        _galleryChips.Clear();
        var group = new ButtonGroup();
        foreach (var category in _markingCategories)
        {
            if (_ignoreCategories.Contains(category))
                continue;

            foreach (var marking in _currentMarkings.GetReverseEnumerator(category))
            {
                if (!_markingManager.TryGetMarking(marking, out var prototype))
                    continue;

                var name = new RichTextLabel { HorizontalExpand = true, VerticalAlignment = VAlignment.Center, MouseFilter = MouseFilterMode.Ignore };
                var displayName = IsGlowing?.Invoke(marking.MarkingId) == true
                    ? $"{GetMarkingName(prototype)} ✦"
                    : GetMarkingName(prototype);
                name.SetMessage(FormattedMessage.FromUnformatted(displayName));
                var preview = Tiles.CreateMarkingPreview(prototype.Sprites, true, 27);
                preview.SetWidth = 27;
                preview.SetColors(marking.MarkingColors);
                var chip = new PanelContainer { SetSize = new Vector2(16), VerticalAlignment = VAlignment.Center, MouseFilter = MouseFilterMode.Ignore };
                chip.PanelOverride = new StyleBoxFlat(marking.MarkingColors.FirstOrDefault(Color.White));
                var row = new ContainerButton
                {
                    ToggleMode = true,
                    Group = group,
                    Pressed = marking.MarkingId == _gallerySelection,
                    ToolTip = GetMarkingName(prototype),
                    StyleClasses = { ContainerButton.StyleClassButton },
                    Children = { new BoxContainer { SeparationOverride = 8, MouseFilter = MouseFilterMode.Ignore, Children = { preview, name, chip } } },
                };
                row.OnPressed += _ => SelectGalleryMarking(prototype);
                GalleryRows.AddChild(row);
                _galleryRowPreviews[marking.MarkingId] = preview;
                _galleryChips[marking.MarkingId] = chip;
            }
        }
        GalleryLayerCount.Text = GalleryRows.ChildCount.ToString();
        GalleryLayerScroll.Visible = GalleryRows.ChildCount > 0;
        GalleryNoLayers.Visible = !GalleryLayerScroll.Visible;
        PopulateGalleryDetails();
    }

    private void PopulateGalleryDetails()
    {
        GalleryColors.DisposeAllChildren();
        GallerySample.DisposeAllChildren();
        _galleryDetailPreview = null;
        _galleryGlowCheckBox = null;
        if (_gallerySelection == null || !GetMarkings(_selectedMarkingCategory).TryGetValue(_gallerySelection, out var prototype))
        {
            GalleryDetails.Visible = false;
            return;
        }

        GalleryDetails.Visible = true;
        var applied = _currentMarkings.TryGetMarking(_selectedMarkingCategory, prototype.ID, out var marking);
        marking ??= _galleryPending ??= CreateMarking(prototype);
        GalleryDetailCategory.Text = GalleryCategoryName(prototype.MarkingCategory);
        GalleryDetailName.SetMessage(FormattedMessage.FromUnformatted(GetMarkingName(prototype)), Wh40kInterfaceTheme.Gold);
        _galleryDetailPreview = Tiles.CreateMarkingPreview(prototype.Sprites, true, 40);
        _galleryDetailPreview.SetWidth = 40;
        _galleryDetailPreview.SetColors(marking.MarkingColors);
        GallerySample.AddChild(_galleryDetailPreview);
        _galleryApply.Visible = !applied;
        _galleryUp.Visible = _galleryDown.Visible = _galleryActionSpacer.Visible = _galleryRemove.Visible = applied;

        if (applied && prototype.Shader == null && IsGlowing != null)
        {
            var markingId = prototype.ID;
            _galleryGlowCheckBox = new CheckBox
            {
                Text = Loc.GetString("wh40k-marking-glow-toggle"),
                ToolTip = Loc.GetString("wh40k-marking-glow-tooltip"),
                Pressed = IsGlowing(markingId),
                Disabled = EditingDisabled,
            };
            _galleryGlowCheckBox.OnToggled += args => OnGlowChanged?.Invoke(markingId, args.Pressed);
            GalleryColors.AddChild(_galleryGlowCheckBox);
        }

        if (!prototype.ForcedColoring)
        {
            var names = GetMarkingStateNames(prototype);
            for (var layer = 0; layer < marking.MarkingColors.Count; layer++)
            {
                if (marking.MarkingColors.Count > 1 && layer < names.Count)
                    GalleryColors.AddChild(new Label { Text = names[layer], FontColorOverride = Wh40kInterfaceTheme.MutedText });

                var index = layer;
                var palette = new Wh40kColorPalette(marking.MarkingColors[index], inline: true);
                palette.OnColorChanged += color => SetGalleryColor(prototype, index, color);
                GalleryColors.AddChild(palette);
            }
        }
        UpdateGalleryPermissions();
    }

    private void UpdateGalleryPermissions()
    {
        var index = _gallerySelection == null ? -1 : _currentMarkings.FindIndexOf(_selectedMarkingCategory, _gallerySelection);
        var count = _currentMarkings.Markings.TryGetValue(_selectedMarkingCategory, out var category) ? category.Count : 0;
        _galleryApply.Disabled = EditingDisabled || _currentMarkings.PointsLeft(_selectedMarkingCategory) == 0 && !Forced;
        _galleryUp.Disabled = EditingDisabled || index < 0 || index >= count - 1;
        _galleryDown.Disabled = EditingDisabled || index <= 0;
        _galleryRemove.Disabled = EditingDisabled || index < 0;
        foreach (var palette in GalleryColors.Children.OfType<Wh40kColorPalette>())
            palette.SetDisabled(EditingDisabled);
        if (_galleryGlowCheckBox != null)
            _galleryGlowCheckBox.Disabled = EditingDisabled;
    }

    private void SetGalleryColor(MarkingPrototype prototype, int layer, Color color)
    {
        if (EditingDisabled || _gallerySelection != prototype.ID || prototype.ForcedColoring)
            return;

        var category = prototype.MarkingCategory;
        var index = _currentMarkings.FindIndexOf(category, prototype.ID);
        var marking = index >= 0 ? new Marking(_currentMarkings.Markings[category][index]) : _galleryPending;
        if (marking == null)
            return;

        marking.SetColor(layer, color);
        _galleryDetailPreview?.SetColors(marking.MarkingColors);
        if (_galleryTilePreviews.TryGetValue(prototype.ID, out var preview))
            preview.SetColors(marking.MarkingColors);
        if (index < 0)
            return;

        _currentMarkings.Replace(category, index, marking);
        _galleryRowPreviews[prototype.ID].SetColors(marking.MarkingColors);
        _galleryChips[prototype.ID].PanelOverride = new StyleBoxFlat(marking.MarkingColors[0]);
        OnMarkingColorChange?.Invoke(_currentMarkings);
    }

    internal void MoveGalleryMarking(int direction)
    {
        if (_gallerySelection == null || EditingDisabled ||
            !_currentMarkings.Markings.TryGetValue(_selectedMarkingCategory, out var category))
            return;
        var index = _currentMarkings.FindIndexOf(_selectedMarkingCategory, _gallerySelection);
        var row = category.Count - 1 - index;
        if (index < 0 || row + direction < 0 || row + direction >= category.Count)
            return;

        ShiftModelRank(row, direction);
        PopulateGallerySide();
        OnMarkingRankChange?.Invoke(_currentMarkings);
    }

    internal void RemoveGalleryMarking()
    {
        if (EditingDisabled || _gallerySelection == null ||
            !_currentMarkings.TryGetMarking(_selectedMarkingCategory, _gallerySelection, out _))
            return;
        _currentMarkings.Remove(_selectedMarkingCategory, _gallerySelection);
        _gallerySelection = null;
        _galleryPending = null;
        PopulateGallery(GallerySearch.Text);
        PopulateGallerySide();
        OnMarkingRemoved?.Invoke(_currentMarkings);
    }
}
