using System.Linq;
using System.Numerics;
using Content.Client._WH40K.UserInterface;
using Content.Client.Administration.Managers;
using Content.Client.Decals;
using Content.Client.Gameplay;
using Content.Client.Sandbox;
using Content.Client.UserInterface.Systems.DecalPlacer;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Mapping;

public sealed partial class Wh40kMappingUIController : UIController, IOnStateChanged<GameplayState>
{
    [Dependency] private readonly IPlacementManager _placement = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ILocalizationManager _localization = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IClientAdminManager _admin = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [UISystemDependency] private readonly SandboxSystem _sandbox = default!;
    [UISystemDependency] private readonly SpriteSystem _sprites = default!;
    [UISystemDependency] private readonly AppearanceSystem _appearance = default!;

    private Window? _entities;
    private Window? _tileWindow;
    private Window? _owner;
    private Window? _picking;
    private bool _changing;

    private sealed class Window : Wh40kDefaultWindow
    {
        public readonly Wh40kMappingPanel Catalog = new();
        public readonly Button Erase = new() { ToggleMode = true, Text = Loc.GetString("wh40k-mapping-erase") };
        public readonly Button Rotate = new() { ToolTip = Loc.GetString("wh40k-mapping-rotation-tip") };
        public readonly Button Pick = new() { ToggleMode = true, Text = Loc.GetString("wh40k-mapping-pick") };
        public readonly Button Mirror = new() { ToggleMode = true, Text = Loc.GetString("wh40k-mapping-mirror") };
        public readonly Button Replace = new() { ToggleMode = true, Text = Loc.GetString("entity-spawn-window-replace-button-text") };
        public readonly OptionButton Mode = new() { HorizontalExpand = true, RectClipContent = true };
        public readonly OptionButton Brush = new() { HorizontalExpand = true };
        public readonly Wh40kMappingLabel Hint = new(2) { Visible = false, FontColorOverride = Wh40kInterfaceTheme.MutedText };
        public bool IsTile { get; set; }

        public Window()
        {
            MinSize = Wh40kMappingPanel.WindowMinimum;
            SetSize = Wh40kMappingPanel.InitialWindowSize(UserInterfaceManager.WindowRoot.Size);
            Contents.AddChild(Catalog);
        }
    }

    public override void Initialize()
    {
        _placement.PlacementChanged += OnPlacementChanged;
        _placement.DirectionChanged += (_, _) => UpdateOptions(_owner);
        _placement.MirroredChanged += (_, _) => UpdateOptions(_owner);
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
        _cfg.OnValueChanged(CVars.EntitiesCategoryFilter, _ => Reload(_entities));
    }

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.EditorPlaceObject, new PointerInputCmdHandler(OnWorldClick, outsidePrediction: true),
                typeof(PlacementManager), typeof(DecalPlacementSystem))
            .BindBefore(EngineKeyFunctions.EditorCancelPlace, new PointerInputCmdHandler(CancelPick, outsidePrediction: true),
                typeof(PlacementManager), typeof(DecalPlacementSystem))
            .Register<Wh40kMappingUIController>();
    }

    public void ToggleEntities() => Toggle(ref _entities, false);
    public void ToggleTiles() => Toggle(ref _tileWindow, true);

    public void CloseWindows()
    {
        if (_entities is { Disposed: false, IsOpen: true })
            _entities.Close();
        if (_tileWindow is { Disposed: false, IsOpen: true })
            _tileWindow.Close();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<Wh40kMappingUIController>();
        CloseWindows();
        _entities?.Dispose();
        _tileWindow?.Dispose();
        _entities = null;
        _tileWindow = null;
    }

    private bool Allowed => _admin.CanAdminPlace() || _sandbox is { SandboxAllowed: true };

    private void Toggle(ref Window? store, bool tile)
    {
        if (store is { IsOpen: true })
        {
            store.Close();
            return;
        }
        if (!Allowed)
            return;
        if (store == null || store.Disposed)
        {
            store = CreateWindow(tile);
            store.OpenCenteredAt(new Vector2(tile ? .76f : .24f, .43f));
        }
        else
        {
            store.Open();
        }
        UpdateOptions(store);
        store.Catalog.FocusSearch();
    }

    private Window CreateWindow(bool tile)
    {
        var window = UIManager.CreateWindow<Window>();
        window.IsTile = tile;
        window.Title = Loc.GetString(tile ? "wh40k-mapping-tile-title" : "entity-spawn-window-title");
        window.Catalog.LoadKit(tile ? "tiles" : "entities");
        window.Brush.AddItem(Loc.GetString("wh40k-mapping-brush-point"), 0);
        window.Brush.AddItem(Loc.GetString("wh40k-mapping-brush-line"), 1);
        window.Brush.AddItem(Loc.GetString("wh40k-mapping-brush-area"), 2);
        window.Brush.SelectId(0);
        window.Brush.OnItemSelected += args =>
        {
            window.Brush.SelectId(args.Id);
            if (_owner == window && _placement is PlacementManager placement)
                placement.PlacementType = PlacementManager.PlacementTypes.None;
            UpdateOptions(window);
        };
        window.Catalog.Options.AddChild(window.Brush);
        window.Catalog.Options.AddChild(new BoxContainer { SeparationOverride = 4, Children = { window.Rotate, window.Erase } });
        if (tile)
        {
            window.Catalog.Options.AddChild(window.Mirror);
        }
        else
        {
            for (var i = 0; i < _placement.AllModeNames.Length; i++)
                window.Mode.AddItem(i == 0 ? Loc.GetString("wh40k-mapping-placement-default") : _placement.AllModeNames[i], i);
            window.Mode.SelectId(0);
            window.Catalog.Options.AddChild(window.Replace);
            window.Catalog.Options.AddChild(window.Mode);
        }
        window.Catalog.Options.AddChild(window.Pick);
        window.Catalog.Options.AddChild(window.Hint);
        window.Catalog.EntrySelected += id => Begin(window, id);
        window.Catalog.EntryDeselected += () =>
        {
            if (_owner == window)
                _placement.Clear();
        };
        window.Pick.OnToggled += args =>
        {
            SetPick(args.Pressed ? window : null);
            window.Pick.Pressed = _picking == window;
        };
        window.Erase.OnToggled += args => SetErase(window, args.Pressed);
        window.Rotate.OnPressed += _ => ((PlacementManager) _placement).Rotate();
        window.Mirror.OnToggled += args => _placement.Mirrored = args.Pressed;
        window.Replace.OnToggled += args => _placement.Replacement = args.Pressed;
        window.Mode.OnItemSelected += args =>
        {
            window.Mode.SelectId(args.Id);
            if (_owner == window && window.Catalog.Selected is { } selected)
                Begin(window, selected);
        };
        window.OnClose += () =>
        {
            if (_picking == window)
                SetPick(null);
            if (_owner == window)
                _placement.Clear();
            window.Catalog.ClearSelection();
        };
        Reload(window);
        return window;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            Reload(_entities);
    }

    private void Reload(Window? window)
    {
        if (window == null || window.Disposed)
            return;
        if (window.IsTile)
        {
            window.Catalog.SetEntries(_tiles.Where(tile => !tile.EditorHidden).Select(tile =>
                new Wh40kMappingPanel.Entry(tile.ID, TileName(tile), Wh40kMappingCategories.Tile(tile), () => TileIcon(tile))));
            return;
        }
        _prototypes.TryIndex<EntityCategoryPrototype>(_cfg.GetCVar(CVars.EntitiesCategoryFilter), out var filter);
        var hadSelection = window.Catalog.Selected != null;
        window.Catalog.SetEntries(_prototypes.EnumeratePrototypes<EntityPrototype>()
            .Where(entity => !entity.Abstract && !entity.HideSpawnMenu && (filter == null || entity.Categories.Contains(filter)))
            .Select(entity =>
            {
                var locale = _localization.GetEntityData(entity.ID);
                var hasSprite = entity.HasComp<SpriteComponent>(_componentFactory);
                return new Wh40kMappingPanel.Entry(entity.ID,
                    string.IsNullOrWhiteSpace(locale.Name) ? entity.ID : locale.Name, Wh40kMappingCategories.Entity(entity),
                    hasSprite ? null : () => _sprites.GetPrototypeIcon(entity).Default, locale.Suffix,
                    Prototype: hasSprite ? entity.ID : null);
            }));
        if (_owner == window && hadSelection && window.Catalog.Selected == null)
            _placement.Clear();
    }

    private static string TileName(ITileDefinition tile)
    {
        if (string.IsNullOrWhiteSpace(tile.Name))
            return tile.ID;
        return Loc.TryGetString(tile.Name, out var name) ? name : tile.Name;
    }

    private Texture? TileIcon(ITileDefinition tile)
    {
        if (tile.Sprite is not { } path || !_resources.TryGetResource<TextureResource>(path, out var resource))
            return null;
        var texture = resource.Texture;
        return tile.Variants > 1 ? new AtlasTexture(texture,
            UIBox2.FromDimensions(0, 0, texture.Width / tile.Variants, texture.Height)) : texture;
    }

    private void Begin(Window window, string id)
    {
        if (!Allowed || !window.IsOpen)
        {
            window.Catalog.ClearSelection();
            return;
        }
        SetPick(null);
        PlacementInformation info;
        if (window.IsTile)
        {
            if (!_tiles.TryGetDefinition(id, out var tile))
                return;
            info = new PlacementInformation { PlacementOption = nameof(AlignTileAny), TileType = tile.TileId, Range = 400, IsTile = true };
        }
        else
        {
            if (!_prototypes.TryIndex<EntityPrototype>(id, out var entity) || entity.Abstract || entity.HideSpawnMenu)
                return;
            var mode = _placement.AllModeNames[window.Mode.SelectedId];
            info = new PlacementInformation
            {
                PlacementOption = mode == IPlacementManager.DefaultModeName ? entity.PlacementMode : mode,
                EntityType = entity.ID, Range = 2,
            };
        }
        DrivePlacement(window, () =>
        {
            _placement.BeginPlacing(info);
            if (!window.IsTile && _placement is PlacementManager { CurrentPlacementOverlayEntity: { } uid } &&
                EntityManager.TryGetComponent(uid, out SpriteComponent? sprite))
            {
                _appearance.OnChangeData(uid, sprite);
            }
        });
    }

    private void DrivePlacement(Window window, Action action)
    {
        UIManager.GetUIController<DecalPlacerUIController>().PausePlacement();
        var previous = _owner;
        _owner = window;
        _changing = true;
        try { action(); }
        finally { _changing = false; }
        if (!_placement.IsActive)
        {
            _owner = null;
            window.Catalog.ClearSelection();
        }
        if (previous != window)
        {
            previous?.Catalog.ClearSelection();
            UpdateOptions(previous);
        }
        UpdateOptions(window);
    }

    internal void PausePlacement()
    {
        SetPick(null);
        StopPlacement();
    }

    private void StopPlacement()
    {
        var previous = _owner;
        _owner = null;
        if (_placement.IsActive)
            _placement.Clear();
        previous?.Catalog.ClearSelection();
        UpdateOptions(previous);
    }

    private void SetErase(Window window, bool pressed)
    {
        if (!Allowed)
            return;
        if (!pressed)
        {
            if (_owner == window)
                _placement.Clear();
            return;
        }
        window.Catalog.ClearSelection();
        SetPick(null);
        DrivePlacement(window, () =>
        {
            _placement.Clear();
            if (window.IsTile)
                _placement.BeginPlacing(new PlacementInformation { PlacementOption = nameof(AlignTileAny), TileType = 0, Range = 400, IsTile = true });
            else
                _placement.ToggleEraser();
        });
    }

    private void OnPlacementChanged(object? sender, EventArgs args)
    {
        if (_changing || _owner is not { } owner)
            return;
        _owner = null;
        owner.Catalog.ClearSelection();
        UpdateOptions(owner);
    }

    private void UpdateOptions(Window? window)
    {
        if (window == null || window.Disposed)
            return;
        var own = _owner == window;
        var tile = own && _placement.CurrentPermission is { IsTile: true } info ? _tiles[info.TileType] : null;
        var canRotate = own && !_placement.Eraser && (window.IsTile ? tile?.AllowRotationMirror == true : _placement.CurrentMode != null);
        window.Rotate.Disabled = !canRotate;
        window.Rotate.Text = Loc.GetString("wh40k-mapping-rotate", ("direction", _placement.Direction.ToString()));
        window.Mirror.Disabled = !canRotate;
        window.Mirror.Pressed = own && _placement.Mirrored;
        window.Erase.Pressed = own && (_placement.Eraser || tile?.TileId == 0);
        window.Replace.Pressed = _placement.Replacement;
        var hint = _picking == window
            ? Loc.GetString("wh40k-mapping-pick-hint")
            : window.Brush.SelectedId == 0 ? string.Empty : Loc.GetString("wh40k-mapping-brush-hint");
        window.Hint.FullText = hint;
        window.Hint.Visible = hint.Length > 0;
    }

    private void SetPick(Window? next)
    {
        if (next == null && _picking == null)
            return;
        if (_picking is { } previous)
        {
            previous.Pick.Pressed = false;
            _picking = null;
            UpdateOptions(previous);
        }
        if (next != null && Allowed)
        {
            UIManager.GetUIController<DecalPlacerUIController>().PausePlacement();
            StopPlacement();
            _picking = next;
            next.Pick.Pressed = true;
            _input.Contexts.SetActiveContext("editor");
            UpdateOptions(next);
        }
        else if (!_placement.IsActive)
        {
            EntityManager.System<InputSystem>().SetEntityContextActive();
        }
    }

    private bool CancelPick(ICommonSession? session, EntityCoordinates coordinates, EntityUid uid)
    {
        if (_picking == null)
            return false;
        SetPick(null);
        return true;
    }

    private bool OnWorldClick(ICommonSession? session, EntityCoordinates coordinates, EntityUid uid)
    {
        if (!Allowed)
            return false;
        if (_picking is { } window)
        {
            if (window.IsTile)
            {
                var maps = EntityManager.System<SharedMapSystem>();
                var map = EntityManager.System<SharedTransformSystem>().ToMapCoordinates(coordinates);
                if (maps.TryFindGridAt(map, out var gridUid, out var grid) &&
                    maps.TryGetTileRef(gridUid, grid, coordinates, out var tile))
                    window.Catalog.Select(_tiles[tile.Tile.TypeId].ID);
            }
            else if (EntityManager.TryGetComponent(uid, out MetaDataComponent? metadata) &&
                     metadata.EntityPrototype is { } entity && window.Catalog.Select(entity.ID) &&
                     EntityManager.TryGetComponent(uid, out TransformComponent? transform))
            {
                _placement.Direction = transform.LocalRotation.GetDir();
            }
            return true;
        }
        if (_owner == null || _owner.Brush.SelectedId == 0 || _placement is not PlacementManager
            { IsActive: true, Eraser: false, PlacementType: PlacementManager.PlacementTypes.None, CurrentMode: { } mode } placement)
            return false;
        var line = _owner.Brush.SelectedId == 1;
        if ((line && !mode.HasLineMode) || (!line && !mode.HasGridMode))
            return false;
        mode.AlignPlacementMode(_input.MouseScreenPosition);
        placement.StartPoint = mode.MouseCoords;
        placement.PlacementType = line ? PlacementManager.PlacementTypes.Line : PlacementManager.PlacementTypes.Grid;
        return true;
    }
}
