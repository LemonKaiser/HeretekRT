using System.Linq;
using System.Numerics;
using Content.Client.Actions;
using Content.Client.Decals.Overlays;
using Content.Shared.Actions;
using Content.Shared.Decals;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Decals;

// This is shit and basically a half-rewrite of PlacementManager
// TODO refactor placementmanager so this isnt shit anymore
public sealed partial class DecalPlacementSystem : EntitySystem
{
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private InputSystem _inputSystem = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private string? _decalId;
    private Color _decalColor = Color.White;
    private Angle _decalAngle = Angle.Zero;
    private bool _snap;
    private int _zIndex;
    private bool _cleanable;

    private bool _active;
    private bool _placing;
    private bool _erasing;
    private bool _eraseMode;
    private bool _pickMode;
    private bool _autoRotate;
    private int _brush;
    private EntityCoordinates? _strokeStart;

    public event Action<Decal>? Sampled;

    public (DecalPrototype? Decal, bool Snap, Angle Angle, Color Color) GetActiveDecal()
    {
        return _active && !_eraseMode && !_pickMode && _decalId != null ?
            (_protoMan.Index<DecalPrototype>(_decalId), _snap, _decalAngle, _decalColor) :
            (null, false, Angle.Zero, Color.Wheat);
    }

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new DecalPlacementOverlay(this, _transform, _sprite));

        CommandBinds.Builder.Bind(EngineKeyFunctions.EditorPlaceObject, new PointerStateInputCmdHandler(
            (session, coords, uid) =>
            {
                if (!_active || _placing || (_decalId == null && !_eraseMode && !_pickMode))
                    return false;

                _placing = true;

                if (_pickMode)
                {
                    SampleAt(coords);
                    return true;
                }
                if (_eraseMode)
                {
                    RaiseNetworkEvent(new RequestDecalRemovalEvent(GetNetCoordinates(coords)));
                    return true;
                }
                if (_brush != 0)
                {
                    PlaceStroke(coords);
                    return true;
                }

                if (_snap)
                {
                    var newPos = new Vector2(
                        (float) (MathF.Round(coords.X - 0.5f, MidpointRounding.AwayFromZero) + 0.5),
                        (float) (MathF.Round(coords.Y - 0.5f, MidpointRounding.AwayFromZero) + 0.5)
                    );
                    coords = coords.WithPosition(newPos);
                }

                coords = coords.Offset(new Vector2(-0.5f, -0.5f));

                if (!coords.IsValid(EntityManager))
                    return false;

                PlaceDecal(coords, _decalAngle);

                return true;
            },
            (session, coords, uid) =>
            {
                if (!_active)
                    return false;

                _placing = false;
                return true;
            }, true))
            .Bind(EngineKeyFunctions.EditorCancelPlace, new PointerStateInputCmdHandler(
            (session, coords, uid) =>
            {
                if (!_active || _erasing)
                    return false;

                _erasing = true;
                if (_strokeStart != null || _pickMode)
                {
                    _strokeStart = null;
                    return true;
                }

                RaiseNetworkEvent(new RequestDecalRemovalEvent(GetNetCoordinates(coords)));

                return true;
            }, (session, coords, uid) =>
            {
                if (!_active)
                    return false;
                _erasing = false;

                return true;
            }, true)).Register<DecalPlacementSystem>();

        SubscribeLocalEvent<FillActionSlotEvent>(OnFillSlot);
        SubscribeLocalEvent<PlaceDecalActionEvent>(OnPlaceDecalAction);
    }

    private void OnPlaceDecalAction(PlaceDecalActionEvent args)
    {
        if (args.Handled)
            return;

        if (_transform.GetGrid(args.Target) == null)
            return;

        args.Handled = true;

        if (args.Snap)
        {
            var newPos = new Vector2(
                (float) (MathF.Round(args.Target.X - 0.5f, MidpointRounding.AwayFromZero) + 0.5),
                (float) (MathF.Round(args.Target.Y - 0.5f, MidpointRounding.AwayFromZero) + 0.5)
            );
            args.Target = args.Target.WithPosition(newPos);
        }

        args.Target = args.Target.Offset(new Vector2(-0.5f, -0.5f));

        var decal = new Decal(args.Target.Position, args.DecalId, args.Color, Angle.FromDegrees(args.Rotation), args.ZIndex, args.Cleanable);
        RaiseNetworkEvent(new RequestDecalPlacementEvent(decal, GetNetCoordinates(args.Target)));
    }

    private void OnFillSlot(FillActionSlotEvent ev)
    {
        if (!_active || _placing)
            return;

        if (ev.Action != null)
            return;

        if (_decalId == null || !_protoMan.TryIndex<DecalPrototype>(_decalId, out var decalProto))
            return;

        var actionEvent = new PlaceDecalActionEvent()
        {
            DecalId = _decalId,
            Color = _decalColor,
            Rotation = _decalAngle.Degrees,
            Snap = _snap,
            ZIndex = _zIndex,
            Cleanable = _cleanable,
        };

        var actionId = Spawn(null);
        AddComp(actionId, new WorldTargetActionComponent
        {
            // non-unique actions may be considered duplicates when saving/loading.
            Icon = decalProto.Sprite,
            Repeat = true,
            ClientExclusive = true,
            CheckCanAccess = false,
            CheckCanInteract = false,
            Range = -1,
            Event = actionEvent,
            IconColor = _decalColor,
        });

        _metaData.SetEntityName(actionId, $"{_decalId} ({_decalColor.ToHex()}, {(int) _decalAngle.Degrees})");

        ev.Action = actionId;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<DecalPlacementOverlay>();
        CommandBinds.Unregister<DecalPlacementSystem>();
    }

    public void UpdateDecalInfo(string id, Color color, float rotation, bool snap, int zIndex, bool cleanable)
    {
        _decalId = id;
        _decalColor = color;
        _decalAngle = Angle.FromDegrees(rotation);
        _snap = snap;
        _zIndex = zIndex;
        _cleanable = cleanable;
    }

    public void ClearDecal()
    {
        _decalId = null;
        _strokeStart = null;
    }

    public void SetMappingOptions(int brush, bool erase, bool pick, bool autoRotate)
    {
        _brush = brush;
        _eraseMode = erase;
        _pickMode = pick;
        _autoRotate = autoRotate;
        _strokeStart = null;
    }

    private void SampleAt(EntityCoordinates position)
    {
        if (_transform.GetGrid(position) is not { } grid)
            return;
        var local = _transform.ToCoordinates(grid, _transform.ToMapCoordinates(position)).Position;
        var sample = EntityManager.System<DecalSystem>().GetDecalsInRange(grid, local - new Vector2(.5f))
            .OrderByDescending(entry => entry.Decal.ZIndex)
            .ThenBy(entry => Vector2.DistanceSquared(entry.Decal.Coordinates + new Vector2(.5f), local))
            .FirstOrDefault().Decal;
        if (sample != null)
            Sampled?.Invoke(sample);
    }

    private void PlaceStroke(EntityCoordinates end)
    {
        if (_transform.GetGrid(end) is not { } grid)
        {
            _strokeStart = null;
            return;
        }
        var local = _transform.ToCoordinates(grid, _transform.ToMapCoordinates(end));
        if (_strokeStart is not { } start || start.EntityId != grid)
        {
            _strokeStart = local;
            return;
        }
        _strokeStart = null;
        var x1 = (int) MathF.Floor(start.X);
        var y1 = (int) MathF.Floor(start.Y);
        var x2 = (int) MathF.Floor(local.X);
        var y2 = (int) MathF.Floor(local.Y);
        var dx = x2 - x1;
        var dy = y2 - y1;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        // Bound a single brush stroke to keep the existing placement request traffic small.
        if (steps >= 64 || (_brush == 2 && (Math.Abs(dx) + 1) * (Math.Abs(dy) + 1) > 64))
            return;
        if (_brush == 2)
        {
            for (var y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
                for (var x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
                    PlaceDecal(new EntityCoordinates(grid, x, y), _decalAngle);
            return;
        }
        var angle = _autoRotate && Math.Abs(dy) > Math.Abs(dx) ? _decalAngle + Angle.FromDegrees(90) : _decalAngle;
        for (var i = 0; i <= steps; i++)
        {
            var progress = steps == 0 ? 0f : (float) i / steps;
            PlaceDecal(new EntityCoordinates(grid, x1 + MathF.Round(dx * progress), y1 + MathF.Round(dy * progress)), angle);
        }
    }

    private void PlaceDecal(EntityCoordinates coordinates, Angle angle)
    {
        if (_decalId == null || !coordinates.IsValid(EntityManager))
            return;
        var decal = new Decal(coordinates.Position, _decalId, _decalColor, angle, _zIndex, _cleanable);
        RaiseNetworkEvent(new RequestDecalPlacementEvent(decal, GetNetCoordinates(coordinates)));
    }

    public void SetActive(bool active)
    {
        _active = active;
        _placing = false;
        _erasing = false;
        _strokeStart = null;
        if (_active)
            _inputManager.Contexts.SetActiveContext("editor");
        else
            _inputSystem.SetEntityContextActive();
    }
}
