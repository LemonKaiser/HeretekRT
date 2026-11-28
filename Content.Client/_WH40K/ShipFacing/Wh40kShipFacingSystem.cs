using Content.Client.Shuttles.UI;
using Content.Shared._WH40K.ShipFacing;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.ShipFacing;

/// <summary>
/// Sends the world point under the cursor while a pilot holds the cursor-facing key, and freezes camera/radar rotation.
/// </summary>
public sealed partial class Wh40kShipFacingSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan MoveInterval = TimeSpan.FromSeconds(0.05);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(0.25);
    private const float MoveDistance = 0.25f;

    private bool _held;
    private MapCoordinates? _target;
    private MapCoordinates? _sent;
    private TimeSpan _sentAt;

    private readonly Wh40kHeldAngle _eyeRotation = new();

    public bool Held => _held;

    private bool Steering => _held && _player.LocalEntity is { } user &&
                             TryComp<PilotComponent>(user, out var pilot) && pilot.Console != null;

    public Angle HoldRotation(Wh40kHeldAngle angle, Angle live) => angle.Apply(live, Steering, _timing.RealTime);

    public Angle HoldEyeRotation(EntityUid eye, Angle live) =>
        eye == _player.LocalEntity ? HoldRotation(_eyeRotation, live) : live;

    public override void Initialize()
    {
        base.Initialize();
        _clyde.OnWindowFocused += OnWindowFocused;

        CommandBinds.Builder
            .Bind(Wh40kShipFacingKeys.Wh40kShuttleFaceCursor,
                InputCmdHandler.FromDelegate(_ => _held = true, _ =>
                {
                    if (!KeyDown())
                        Release();
                }, handle: true, outsidePrediction: true))
            .Register<Wh40kShipFacingSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _clyde.OnWindowFocused -= OnWindowFocused;
        CommandBinds.Unregister<Wh40kShipFacingSystem>();
    }

    private void OnWindowFocused(WindowFocusedEventArgs args)
    {
        if (!args.Focused)
            Release();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (!_held)
            return;

        if (!KeyDown() || _player.LocalEntity is not { } user || !TryComp<PilotComponent>(user, out var pilot) || pilot.Console == null)
        {
            Release();
            return;
        }

        if (CursorTarget() is { } cursor)
            _target = cursor;

        if (_target is not { } target)
            return;

        var now = _timing.RealTime;
        var moved = _sent is not { } sent
                    || sent.MapId != target.MapId
                    || (sent.Position - target.Position).LengthSquared() > MoveDistance * MoveDistance;

        if (now < _sentAt + (moved ? MoveInterval : KeepAliveInterval))
            return;

        RaiseNetworkEvent(new Wh40kShipFacingTargetEvent(target));
        _sent = target;
        _sentAt = now;
    }

    private bool KeyDown()
    {
        foreach (var binding in _input.GetKeyBindings(Wh40kShipFacingKeys.Wh40kShuttleFaceCursor))
        {
            if (_input.IsKeyDown(binding.BaseKey))
                return true;
        }

        return false;
    }

    private void Release()
    {
        _held = false;
        _target = null;
        if (_sent == null)
            return;

        RaiseNetworkEvent(new Wh40kShipFacingTargetEvent(null));
        _sent = null;
    }

    private MapCoordinates? CursorTarget()
    {
        var mouse = _input.MouseScreenPosition;
        if (!mouse.IsValid)
            return null;

        for (var control = _ui.MouseGetControl(mouse); control != null; control = control.Parent)
        {
            if (control is ShuttleNavControl nav)
            {
                var coords = nav.GetMouseCoordinatesFromCenter();
                return coords.IsValid(EntityManager) ? _transform.ToMapCoordinates(coords) : null;
            }

            if (control is IViewportControl viewport)
            {
                var point = viewport.PixelToMap(mouse.Position);
                return point.MapId == MapId.Nullspace ? null : point;
            }
        }

        return null;
    }
}
