using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageBayControl
{
    private float _zoom = 1;
    private Vector2 _pan;
    private Vector2 _viewportCenter;
    private float _contentLeft;
    private float _contentRight;
    private float _contentTop;
    private float _contentBottom;
    private bool _dragging;

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (_preview == null)
            return;
        var next = Math.Clamp(_zoom * MathF.Pow(1.2f, args.Delta.Y), 1, 4);
        var relative = args.RelativePixelPosition - _viewportCenter;
        _pan = next == 1 ? Vector2.Zero : relative - (relative - _pan) * (next / _zoom);
        _zoom = next;
        _dirty = true;
        args.Handle();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick || _preview == null)
            return;
        _dragging = true;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick)
            _dragging = false;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _dragging = false;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (!_dragging)
            return;
        _pan += args.Relative * UIScale;
        _dirty = true;
    }
}
