using System.Numerics;
using Content.Client.Viewport;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.ShipPreview;

/// <summary>
/// Full-bright client-side ship grid renderer supporting drag panning and cursor-centered scroll zooming.
/// </summary>
public sealed partial class Wh40kShipPreviewControl : Control
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;

    private const float FitMargin = 1.1f;
    private const float ZoomStep = 1.25f;
    private const float AbsoluteMinZoom = 0.02f;
    private const float AbsoluteMaxZoom = 64f;
    private const int LoadRenderFrames = 3;

    private static readonly Color Background = Color.FromHex("#090806");

    private readonly Label _status;

    private readonly ScalingViewport.ZEye _eye = new(0f, 0f, 0f)
    {
        DrawLight = false,
        DrawFov = false,
        DrawParallax = false,
        Rotation = Angle.Zero,
    };

    private IClydeViewport? _viewport;

    private sealed record PreviewSource(VesselPrototype? Vessel, string? Key, string? Yaml, string? Name);

    private PreviewSource? _shown;
    private PreviewSource? _pending;
    private bool _loadQueued;
    private int _loadDelay;

    private EntityUid _grid;
    private Box2 _bounds;
    private bool _hasGrid;
    private Wh40kShipPreviewHandle? _handle;

    private float _zoom = 1f;
    private float _minZoom = AbsoluteMinZoom;
    private float _maxZoom = AbsoluteMaxZoom;

    private bool _fitQueued;
    private bool _dragging;
    private Vector2 _dragAnchor;
    private int _renderFrames;

    public int TileCount { get; private set; }
    public Vector2i GridSize { get; private set; }
    public float MaxLiftGravity { get; private set; }
    public bool HasPreview => _hasGrid;

    public event Action? PreviewUpdated;

    public Wh40kShipPreviewControl()
    {
        IoCManager.InjectDependencies(this);

        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;

        _status = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Align = Label.AlignMode.Center,
            FontColorOverride = Color.FromHex("#AD9F86"),
            Text = Loc.GetString("wh40k-ship-preview-no-vessel"),
        };

        AddChild(_status);
    }

    public void SetVessel(VesselPrototype? vessel)
    {
        if (vessel == null)
        {
            ShowMessage("wh40k-ship-preview-no-vessel");
            return;
        }

        Queue(new PreviewSource(vessel, null, null, null));
    }

    public void SetGridText(string key, string yaml, string? name)
    {
        Queue(new PreviewSource(null, key, yaml, name));
    }

    public void ShowMessage(string locId)
    {
        _pending = null;
        _loadQueued = false;
        ClearPreview(locId);
        PreviewUpdated?.Invoke();
    }

    private void Queue(PreviewSource source)
    {
        if (_loadQueued && Same(_pending, source))
            return;
        if (!_loadQueued && _hasGrid && Same(_shown, source))
            return;

        _pending = source;
        _loadQueued = true;
        _loadDelay = 1;
        SetStatus("wh40k-ship-preview-loading");
    }

    public void FitToGrid()
    {
        if (!_hasGrid)
            return;

        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
        {
            _fitQueued = true;
            return;
        }

        var extents = Vector2.Max(_bounds.Size, Vector2.One) * FitMargin;
        var fit = MathF.Max(
            extents.X * EyeManager.PixelsPerMeter / size.X,
            extents.Y * EyeManager.PixelsPerMeter / size.Y);

        _minZoom = Math.Clamp(fit / 16f, AbsoluteMinZoom, AbsoluteMaxZoom);
        _maxZoom = Math.Clamp(fit * 4f, _minZoom, AbsoluteMaxZoom);

        SetZoom(fit);
        _eye.Position = new MapCoordinates(_bounds.Center, _eye.Position.MapId);

        _fitQueued = false;
        MarkDirty();
    }

    public void Release()
    {
        ClearPreview("wh40k-ship-preview-no-vessel");

        if (_handle == null)
            return;

        _entMan.System<Wh40kShipPreviewSystem>().Release(_handle);
        _handle = null;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_hasGrid && !_entMan.EntityExists(_grid))
            ClearPreview("wh40k-ship-preview-no-vessel");

        if (!_loadQueued)
            return;

        if (_loadDelay > 0)
        {
            _loadDelay--;
            return;
        }

        _loadQueued = false;
        if (_pending is { } source)
            Load(source);
    }

    private static bool Same(PreviewSource? a, PreviewSource? b)
    {
        return a != null && b != null && a.Vessel == b.Vessel && a.Key == b.Key;
    }

    private void Load(PreviewSource source)
    {
        var system = _entMan.System<Wh40kShipPreviewSystem>();

        _handle ??= system.Acquire();

        var loaded = source.Vessel is { } vessel
            ? system.TryLoad(_handle, vessel, out var preview)
            : system.TryLoadText(_handle, new ResPath(source.Key!), source.Yaml!, source.Name, out preview);

        if (!loaded)
        {
            ClearPreview("wh40k-ship-preview-failed");
            PreviewUpdated?.Invoke();
            return;
        }

        _shown = source;
        _grid = preview.Grid.Owner;
        _bounds = preview.LocalBounds;
        _hasGrid = true;
        TileCount = preview.TileCount;
        GridSize = preview.SizeInTiles;
        MaxLiftGravity = Wh40kShipPreviewLift.MaxLiftGravity(_entMan, _protoMan, _grid, TileCount);

        _status.Visible = false;
        _eye.Position = new MapCoordinates(_bounds.Center, _handle.PreviewMap);

        FitToGrid();
        MarkDirty(LoadRenderFrames);
        PreviewUpdated?.Invoke();
    }

    private void ClearPreview(string status)
    {
        if (_hasGrid && _handle != null)
            _entMan.System<Wh40kShipPreviewSystem>().Clear(_handle);

        _shown = null;
        _grid = EntityUid.Invalid;
        _hasGrid = false;
        _dragging = false;
        TileCount = 0;
        GridSize = Vector2i.Zero;
        MaxLiftGravity = 0f;
        SetStatus(status);
    }

    private void SetStatus(string locId)
    {
        _status.Text = Loc.GetString(locId);
        _status.Visible = true;
    }

    protected override void Draw(IRenderHandle handle)
    {
        base.Draw(handle);

        if (!_hasGrid)
            return;

        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
            return;

        if (_viewport == null || _viewport.Size != size)
        {
            _viewport?.Dispose();
            _viewport = _clyde.CreateViewport(size, new TextureSampleParameters { Filter = false }, "wh40k-ship-preview");
            _viewport.Eye = _eye;
            _viewport.ClearColor = Background;
            MarkDirty(LoadRenderFrames);
        }

        if (_fitQueued)
            FitToGrid();

        if (_renderFrames > 0)
        {
            _renderFrames--;
            _viewport.Render();
        }

        handle.DrawingHandleScreen.DrawTextureRect(
            _viewport.RenderTarget.Texture,
            UIBox2.FromDimensions(Vector2.Zero, size));
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _viewport?.Dispose();
        _viewport = null;
        _dragging = false;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        MarkDirty(LoadRenderFrames);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (!_hasGrid || _viewport == null)
            return;

        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.UIRightClick)
            return;

        _dragging = true;
        _dragAnchor = _viewport.LocalToWorld(args.RelativePixelPosition).Position;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.UIRightClick)
            return;

        _dragging = false;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (!_dragging || !_hasGrid || _viewport == null)
            return;

        var delta = _dragAnchor - _viewport.LocalToWorld(args.RelativePixelPosition).Position;
        if (delta == Vector2.Zero)
            return;

        _eye.Position = new MapCoordinates(_eye.Position.Position + delta, _eye.Position.MapId);
        MarkDirty();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (!_hasGrid || _viewport == null || args.Delta.Y == 0f)
            return;

        args.Handle();

        var local = args.RelativePixelPosition;
        var before = _viewport.LocalToWorld(local).Position;
        SetZoom(_zoom * MathF.Pow(ZoomStep, -args.Delta.Y));
        var after = _viewport.LocalToWorld(local).Position;

        _eye.Position = new MapCoordinates(_eye.Position.Position + (before - after), _eye.Position.MapId);
        MarkDirty();
    }

    private void SetZoom(float zoom)
    {
        _zoom = Math.Clamp(zoom, _minZoom, _maxZoom);
        _eye.Zoom = new Vector2(_zoom, _zoom);
    }

    private void MarkDirty(int frames = 1)
    {
        _renderFrames = Math.Max(_renderFrames, frames);
    }
}
