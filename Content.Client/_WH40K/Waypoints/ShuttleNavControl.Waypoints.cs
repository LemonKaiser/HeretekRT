using System.Numerics;
using Content.Client._WH40K.Waypoints;
using Content.Shared._Mono.Shuttles;
using Content.Shared._WH40K.Waypoints;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private const float Wh40kWaypointMarkerSize = 6f;
    private const float Wh40kOverlayEdge = 8f;
    private const float Wh40kWaypointKeyTop = 10f;
    private const float Wh40kWaypointPanelTop = 44f;

    private static readonly Color Wh40kWaypointColor = Color.FromHex("#6EE7FF");
    private static readonly Color Wh40kReadoutColor = Color.FromHex("#D8C7A4");
    private static readonly Color Wh40kReadoutShadow = Color.Black.WithAlpha(0.85f);
    private static readonly IReadOnlyList<Wh40kWaypoint> Wh40kNoWaypoints = new List<Wh40kWaypoint>();

    private readonly Vector2[] _wh40kWaypointVertices = new Vector2[5];
    private Enum? _wh40kWaypointKey;
    private bool _wh40kWaypointCanFly;
    private bool _wh40kWaypointPicking;
    private bool _wh40kSwallowRelease;
    private Button? _wh40kWaypointButton;
    private Wh40kWaypointPanel? _wh40kWaypointPanel;

    public void Wh40kEnableWaypoints(Enum uiKey, bool canFly)
    {
        _wh40kWaypointKey = uiKey;
        _wh40kWaypointCanFly = canFly;
    }

    public Vector2? Wh40kCursorWorldPosition(Vector2 relativePosition)
    {
        var coordinates = GetMouseEntityCoordinates(relativePosition);
        return coordinates.IsValid(EntManager) ? _transform.ToMapCoordinates(coordinates).Position : null;
    }

    public Vector2? Wh40kScannerWorldPosition()
    {
        if (EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console))
            return _transform.GetWorldPosition(console);

        return _coordinates is { } centre && centre.IsValid(EntManager)
            ? _transform.ToMapCoordinates(centre).Position
            : null;
    }

    private void AddWh40kWaypointControls(LayoutContainer parent)
    {
        if (_wh40kWaypointKey == null || _wh40kWaypointButton != null)
            return;

        var button = _wh40kWaypointButton = new Button
        {
            Name = "Wh40kWaypointKey",
            Text = Loc.GetString("wh40k-waypoint-key"),
            ToolTip = Loc.GetString("wh40k-waypoint-key-tooltip"),
            ToggleMode = true,
            StyleClasses = { "ButtonSquare" },
            MinWidth = 64f,
            MinHeight = 28f,
        };
        var panel = _wh40kWaypointPanel = new Wh40kWaypointPanel { Visible = false };
        button.OnToggled += args =>
        {
            panel.Visible = args.Pressed;
            if (!args.Pressed)
                panel.CancelPick();
        };
        panel.PickToggled += picking => _wh40kWaypointPicking = picking;
        panel.AddRequested += (name, position) => Wh40kSendWaypointMessage(new Wh40kWaypointAddMessage(name, position));
        panel.RemoveRequested += id => Wh40kSendWaypointMessage(new Wh40kWaypointRemoveMessage(id));
        panel.FlyRequested += Wh40kFlyTo;

        parent.AddChild(button);
        parent.AddChild(panel);
        Wh40kPlaceWaypointControls(parent);
    }

    private void Wh40kPlaceWaypointControls(LayoutContainer parent)
    {
        if (_wh40kWaypointButton == null || _wh40kWaypointPanel == null)
            return;

        Place(_wh40kWaypointButton, Wh40kWaypointKeyTop);
        Place(_wh40kWaypointPanel, Wh40kWaypointPanelTop);

        void Place(Control control, float top)
        {
            if (control.Parent != parent)
            {
                control.Orphan();
                parent.AddChild(control);
            }

            control.SetPositionLast();
            LayoutContainer.SetAnchorPreset(control, LayoutContainer.LayoutPreset.TopRight);
            LayoutContainer.SetGrowHorizontal(control, LayoutContainer.GrowDirection.Begin);
            LayoutContainer.SetGrowVertical(control, LayoutContainer.GrowDirection.End);
            LayoutContainer.SetMarginLeft(control, -Wh40kOverlayEdge);
            LayoutContainer.SetMarginRight(control, -Wh40kOverlayEdge);
            LayoutContainer.SetMarginTop(control, top);
            LayoutContainer.SetMarginBottom(control, top);
        }
    }

    private void Wh40kUpdateWaypoints()
    {
        if (_wh40kWaypointButton == null || _wh40kWaypointPanel == null)
            return;

        var hasGrid = EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console) && console.GridUid != null;
        _wh40kWaypointButton.Visible = hasGrid;
        if (!hasGrid)
        {
            _wh40kWaypointButton.Pressed = false;
            _wh40kWaypointPanel.Visible = false;
            _wh40kWaypointPanel.CancelPick();
            return;
        }

        var waypoints = EntManager.TryGetComponent<Wh40kWaypointsComponent>(console!.GridUid, out var stored)
            ? stored.Waypoints
            : Wh40kNoWaypoints;
        NetEntity? map = console.MapUid is { } mapUid ? EntManager.GetNetEntity(mapUid) : null;
        _wh40kWaypointPanel.UpdateWaypoints(waypoints, map, _wh40kWaypointCanFly);
    }

    private void Wh40kSendWaypointMessage(BoundUserInterfaceMessage message)
    {
        if (_consoleEntity is { } console && _wh40kWaypointKey != null)
            EntManager.System<SharedUserInterfaceSystem>().ClientSendUiMessage(console, _wh40kWaypointKey, message);
    }

    private void Wh40kFlyTo(Vector2 position)
    {
        if (_consoleEntity is not { } consoleUid
            || !EntManager.TryGetComponent<TransformComponent>(consoleUid, out var console)
            || console.MapID == MapId.Nullspace)
            return;

        Wh40kSendWaypointMessage(new ShuttleConsoleAutopilotPositionMessage
        {
            Coordinates = new MapCoordinates(position, console.MapID),
            Angle = _transform.GetWorldRotation(console.GridUid ?? consoleUid),
        });
    }

    private bool Wh40kTryPickWaypoint(GUIBoundKeyEventArgs args)
    {
        if (!_wh40kWaypointPicking || args.Function != EngineKeyFunctions.UIClick)
            return false;

        if (Wh40kCursorWorldPosition(args.RelativePosition) is { } position)
            _wh40kWaypointPanel?.SetPicked(position);
        _wh40kSwallowRelease = true;
        args.Handle();
        return true;
    }

    private bool Wh40kTrySwallowPickRelease(GUIBoundKeyEventArgs args)
    {
        if (!_wh40kSwallowRelease || args.Function != EngineKeyFunctions.UIClick)
            return false;

        _wh40kSwallowRelease = false;
        args.Handle();
        return true;
    }

    private void DrawWh40kWaypoints(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (map is not { } mapUid ||
            !EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console) ||
            !EntManager.TryGetComponent<Wh40kWaypointsComponent>(console.GridUid, out var stored))
            return;

        var mapNet = EntManager.GetNetEntity(mapUid);
        var inset = (Wh40kWaypointMarkerSize + 2f) * UIScale;
        foreach (var waypoint in stored.Waypoints)
        {
            if (waypoint.Map != mapNet)
                continue;

            var view = Vector2.Transform(waypoint.Position, worldToView);
            var onScope = Wh40kOnScope(view);
            view = Vector2.Clamp(
                view,
                new Vector2(inset),
                Vector2.Max(new Vector2(inset), new Vector2(PixelSize.X, PixelSize.Y) - new Vector2(inset)));

            var size = Wh40kWaypointMarkerSize * UIScale;
            _wh40kWaypointVertices[0] = view + new Vector2(0f, -size);
            _wh40kWaypointVertices[1] = view + new Vector2(size, 0f);
            _wh40kWaypointVertices[2] = view + new Vector2(0f, size);
            _wh40kWaypointVertices[3] = view + new Vector2(-size, 0f);
            _wh40kWaypointVertices[4] = _wh40kWaypointVertices[0];
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _wh40kWaypointVertices, Wh40kWaypointColor);

            if (!onScope)
                continue;

            var dimensions = handle.GetDimensions(Font, waypoint.Name, UIScale * 0.8f);
            var labelX = view.X + size + 2f * UIScale + dimensions.X > PixelSize.X
                ? view.X - size - 2f * UIScale - dimensions.X
                : view.X + size + 2f * UIScale;
            Wh40kBeginTag(view, new Vector2(labelX, view.Y - dimensions.Y / 2f), 2, always: true);
            Wh40kTagLine(handle, waypoint.Name, UIScale * 0.8f, Wh40kWaypointColor);
        }
    }

    private void DrawWh40kCoordinateReadout(DrawingHandleScreen handle)
    {
        var cursor = _isMouseInside
            ? Wh40kCursorWorldPosition(UserInterfaceManager.MousePositionScaled.Position - GlobalPosition)
            : null;
        var scanner = Wh40kScannerWorldPosition();
        const float scale = 0.8f;
        var bottom = PixelSize.Y - Wh40kOverlayEdge * UIScale;
        var right = PixelSize.X - Wh40kOverlayEdge * UIScale;

        bottom = DrawLine(
            scanner is { } scan
                ? Loc.GetString("wh40k-radar-scanner-coordinates", ("x", $"{scan.X:0.0}"), ("y", $"{scan.Y:0.0}"))
                : Loc.GetString("wh40k-radar-scanner-unknown"),
            bottom);
        DrawLine(
            cursor is { } point
                ? Loc.GetString("wh40k-radar-cursor-coordinates", ("x", $"{point.X:0.0}"), ("y", $"{point.Y:0.0}"))
                : Loc.GetString("wh40k-radar-cursor-unknown"),
            bottom);

        float DrawLine(string text, float lineBottom)
        {
            var dimensions = handle.GetDimensions(Font, text, UIScale * scale);
            var position = new Vector2(right - dimensions.X, lineBottom - dimensions.Y);
            handle.DrawString(Font, position + new Vector2(1f, 1f), text, UIScale * scale, Wh40kReadoutShadow);
            handle.DrawString(Font, position, text, UIScale * scale, Wh40kReadoutColor);
            return position.Y;
        }
    }
}
