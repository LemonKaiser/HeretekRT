using System.Linq;
using System.Text;
using Content.Client._WH40K.Mapping;
using Content.Client._WH40K.UserInterface;
using Content.Shared.Decals;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Decals.UI;

public sealed class DecalPlacerWindow : Wh40kDefaultWindow
{
    private readonly IPrototypeManager _prototypes = IoCManager.Resolve<IPrototypeManager>();
    private readonly DecalPlacementSystem _placement = IoCManager.Resolve<IEntityManager>().System<DecalPlacementSystem>();
    private readonly SpriteSystem _sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();
    private readonly Wh40kMappingPanel _catalog = new();
    private readonly ColorSelectorSliders _color = new() { IsAlphaVisible = true, Color = Color.White };
    private readonly CheckBox _defaults = Check("decal-placer-window-enable-auto");
    private readonly CheckBox _useColor = Check("decal-placer-window-use-color");
    private readonly CheckBox _snap = Check("decal-placer-window-enable-snap");
    private readonly CheckBox _cleanable = Check("decal-placer-window-enable-cleanable");
    private readonly CheckBox _autoRotate = Check("wh40k-mapping-auto-rotate");
    private readonly FloatSpinBox _rotation = new(90, 0) { HorizontalExpand = true };
    private readonly SpinBox _layer = new() { HorizontalExpand = true };
    private readonly OptionButton _brush = new() { HorizontalExpand = true };
    private readonly Button _erase = new() { ToggleMode = true, Text = Loc.GetString("wh40k-mapping-erase") };
    private readonly Button _pick = new() { ToggleMode = true, Text = Loc.GetString("wh40k-mapping-pick") };
    private readonly Wh40kMappingLabel _hint = new(2) { Visible = false, FontColorOverride = Wh40kInterfaceTheme.MutedText };
    private PaletteColorPicker? _palette;
    private bool _usingTool;

    public DecalPlacerWindow()
    {
        Title = Loc.GetString("decal-placer-window-title");
        MinSize = Wh40kMappingPanel.WindowMinimum;
        SetSize = Wh40kMappingPanel.InitialWindowSize(UserInterfaceManager.WindowRoot.Size);
        Contents.AddChild(_catalog);
        _catalog.LoadKit("decals");
        _catalog.EntrySelected += SelectDecal;

        _brush.AddItem(Loc.GetString("wh40k-mapping-brush-point"), 0);
        _brush.AddItem(Loc.GetString("wh40k-mapping-brush-line"), 1);
        _brush.AddItem(Loc.GetString("wh40k-mapping-brush-area"), 2);
        _brush.SelectId(0);
        _brush.OnItemSelected += args => { _brush.SelectId(args.Id); UpdateBrush(); };
        _erase.OnToggled += args =>
        {
            if (args.Pressed)
            {
                _pick.Pressed = false;
                ActivateTool();
            }
            else if (_catalog.Selected == null)
            {
                PausePlacement();
            }
            UpdateBrush();
        };
        _pick.OnToggled += args =>
        {
            if (args.Pressed)
            {
                _erase.Pressed = false;
                ActivateTool();
            }
            else if (_catalog.Selected == null)
            {
                PausePlacement();
            }
            UpdateBrush();
        };
        _autoRotate.OnToggled += _ => UpdateBrush();

        var paletteButton = new Button { Text = Loc.GetString("decal-placer-window-palette") };
        paletteButton.OnPressed += _ => TogglePalette();
        _defaults.OnToggled += args =>
        {
            if (args.Pressed && _catalog.Selected is { } id && _prototypes.TryIndex<DecalPrototype>(id, out var decal))
            {
                ApplyDefaults(decal);
                UpdatePlacement();
            }
        };
        _color.OnColorChanged += _ => UpdatePlacement();
        _useColor.OnToggled += _ => UpdatePlacement();
        _snap.OnToggled += _ => UpdatePlacement();
        _cleanable.OnToggled += _ => UpdatePlacement();
        _rotation.OnValueChanged += _ => UpdatePlacement();
        _layer.ValueChanged += _ => UpdatePlacement();

        var options = _catalog.Options;
        options.AddChild(_brush);
        options.AddChild(new BoxContainer { SeparationOverride = 4, Children = { _erase, _pick } });
        options.AddChild(_hint);
        options.AddChild(Row("decal-placer-window-rotation", _rotation));
        options.AddChild(Row("decal-placer-window-zindex", _layer));
        options.AddChild(_autoRotate);
        options.AddChild(_snap);
        options.AddChild(_cleanable);
        options.AddChild(_defaults);
        options.AddChild(_useColor);
        options.AddChild(_color);
        options.AddChild(paletteButton);
    }

    public void Populate(IEnumerable<DecalPrototype> prototypes)
    {
        _catalog.SetEntries(prototypes.Where(decal => decal.ShowMenu && !decal.Abstract).Select(decal =>
            new Wh40kMappingPanel.Entry(decal.ID, DisplayName(decal.ID), Wh40kMappingCategories.Decal(decal), () => _sprites.Frame0(decal.Sprite))));
        if (_catalog.Selected == null)
            _placement.ClearDecal();
    }

    protected override void Opened()
    {
        base.Opened();
        _placement.Sampled += OnSampled;
        UpdatePlacement();
        UpdateBrush();
        _catalog.FocusSearch();
    }

    public override void Close()
    {
        if (!IsOpen)
            return;
        _palette?.Close();
        _placement.Sampled -= OnSampled;
        PausePlacement();
        base.Close();
    }

    private void SelectDecal(string id)
    {
        if (!_prototypes.TryIndex<DecalPrototype>(id, out var decal))
            return;
        ActivateTool();
        _erase.Pressed = false;
        _pick.Pressed = false;
        if (_defaults.Pressed)
            ApplyDefaults(decal);
        UpdatePlacement();
        UpdateBrush();
    }

    private void ApplyDefaults(DecalPrototype decal)
    {
        _cleanable.Pressed = decal.DefaultCleanable;
        _useColor.Pressed = decal.DefaultCustomColor;
        _snap.Pressed = decal.DefaultSnap;
    }

    private void OnSampled(Decal decal)
    {
        if (!_catalog.Select(decal.Id))
            return;
        _color.Color = decal.Color ?? Color.White;
        _useColor.Pressed = decal.Color is { } color && color != Color.White;
        _rotation.Value = (float) decal.Angle.Degrees;
        _layer.Value = decal.ZIndex;
        _cleanable.Pressed = decal.Cleanable;
        UpdatePlacement();
    }

    private void UpdatePlacement()
    {
        var tint = _useColor.Pressed ? _color.Color : Color.White;
        _catalog.SetTint(tint);
        if (_catalog.Selected is { } id)
            _placement.UpdateDecalInfo(id, tint, _rotation.Value, _snap.Pressed, _layer.Value, _cleanable.Pressed);
    }

    private void UpdateBrush()
    {
        if (_usingTool)
            _placement.SetMappingOptions(_brush.SelectedId, _erase.Pressed, _pick.Pressed, _autoRotate.Pressed);
        var hint = _pick.Pressed ? Loc.GetString("wh40k-mapping-pick-hint")
            : _brush.SelectedId == 0 ? string.Empty : Loc.GetString("wh40k-mapping-brush-hint");
        _hint.FullText = hint;
        _hint.Visible = hint.Length > 0;
    }

    private void TogglePalette()
    {
        if (_palette is { IsOpen: true })
        {
            _palette.Close();
            return;
        }
        if (_palette == null || _palette.Disposed)
        {
            _palette = new PaletteColorPicker();
            _palette.PaletteList.OnItemSelected += _ =>
            {
                if (_palette.PaletteList.GetSelected().FirstOrDefault()?.Metadata is Color color)
                    _color.Color = color;
                UpdatePlacement();
            };
        }
        _palette.OpenToLeft();
    }

    private static string DisplayName(string id)
    {
        var name = new StringBuilder(id.Length);
        for (var i = 0; i < id.Length; i++)
        {
            var current = id[i];
            if (i > 0 && char.IsUpper(current) && (char.IsLower(id[i - 1]) ||
                (i + 1 < id.Length && char.IsUpper(id[i - 1]) && char.IsLower(id[i + 1]))))
                name.Append(' ');
            name.Append(current);
        }
        return name.ToString();
    }

    private void ActivateTool()
    {
        if (_usingTool || !IsOpen)
            return;
        UserInterfaceManager.GetUIController<Wh40kMappingUIController>().PausePlacement();
        _usingTool = true;
        _placement.SetActive(true);
    }

    internal void PausePlacement()
    {
        if (!_usingTool)
            return;
        _usingTool = false;
        _erase.Pressed = false;
        _pick.Pressed = false;
        _placement.SetActive(false);
        UpdateBrush();
    }

    private static CheckBox Check(string key) => new() { Text = Loc.GetString(key) };
    private static BoxContainer Row(string key, Control field) =>
        new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2,
            Children = { new Label { Text = Loc.GetString(key) }, field } };
}
