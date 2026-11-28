using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Shared._WH40K.ShipAccess;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map.Components;

namespace Content.Client._WH40K.ShipAccess;

public sealed class Wh40kShipDoorMap : NavMapControl
{
    public static readonly Color ProtectedColor = Color.FromHex("#E1AD53");
    public static readonly Color DefaultColor = Color.FromHex("#80B5BE");

    private const float MarkerRadius = 4f;
    private static readonly Color FrameColor = Color.FromHex("#262018");
    private static readonly Color MapBackground = Color.FromHex("#11100D");
    private bool _fitPending;

    public Wh40kShipAccessDoor[] Doors { get; set; } = [];
    public NetEntity? SelectedDoor { get; set; }
    public event Action<NetEntity>? DoorSelected;

    public Wh40kShipDoorMap() : base(showToolbar: false)
    {
        SetSize = new Vector2(float.NaN, float.NaN);
        WallColor = Color.FromSrgb(Color.FromHex("#A89267"));
        TileColor = Color.FromHex("#29231B");
        WorldMinRange = 3f;
        PostWallDrawingAction += DrawDoorMarkers;
    }

    public void SetGrid(EntityUid? grid)
    {
        if (MapUid == grid)
            return;

        MapUid = grid;
        ForceNavMapUpdate();
        _fitPending = grid != null;
    }

    public void FitGrid()
    {
        _fitPending = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawRect(PixelSizeBox, MapBackground);

        if (_fitPending && TryFitGrid())
            _fitPending = false;

        base.Draw(handle);
    }

    private bool TryFitGrid()
    {
        if (PixelWidth <= 0 || PixelHeight <= 0 || !EntManager.TryGetComponent<MapGridComponent>(MapUid, out var grid))
            return false;

        var bounds = grid.LocalAABB;
        var offset = bounds.Center - (GetOffset() - Offset);
        var scale = MathF.Min(PixelWidth / (bounds.Width + 4f), PixelHeight / (bounds.Height + 4f));

        WorldMaxRange = MathF.Max(WorldMaxRange, ScaledMinimapRadius / scale);
        WorldRange = ActualRadarRange = Math.Clamp(ScaledMinimapRadius / scale, WorldMinRange, WorldMaxRange);
        Offset = TargetOffset = offset;
        Recentering = false;
        return true;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function != EngineKeyFunctions.UIClick || MapUid == null)
        {
            base.KeyBindDown(args);
            return;
        }

        var pointer = args.PointerLocation.Position - GlobalPixelPosition;
        var nearestDistance = MathF.Pow((MarkerRadius + 4f) * UIScale, 2f);
        NetEntity? nearestDoor = null;
        foreach (var door in Doors)
        {
            var distance = Vector2.DistanceSquared(pointer, ToScreen(door.Position));
            if (distance >= nearestDistance)
                continue;

            nearestDoor = door.Id;
            nearestDistance = distance;
        }

        if (nearestDoor is { } selected)
        {
            DoorSelected?.Invoke(selected);
            args.Handle();
        }
    }

    private void DrawDoorMarkers(DrawingHandleScreen handle)
    {
        foreach (var door in Doors)
        {
            if (door.Id != SelectedDoor)
                DrawDoorMarker(handle, door, false);
        }

        foreach (var door in Doors)
        {
            if (door.Id != SelectedDoor)
                continue;

            DrawDoorMarker(handle, door, true);
            break;
        }
    }

    private void DrawDoorMarker(DrawingHandleScreen handle, Wh40kShipAccessDoor door, bool selected)
    {
        var center = ToScreen(door.Position);
        var radius = new Vector2(MarkerRadius * UIScale);
        var border = radius + new Vector2((selected ? 2f : 1f) * UIScale);
        handle.DrawRect(new UIBox2(center - border, center + border), selected ? Color.White : FrameColor);
        handle.DrawRect(new UIBox2(center - radius, center + radius), door.Protected ? ProtectedColor : DefaultColor);
    }

    private Vector2 ToScreen(Vector2 position)
    {
        var local = position - GetOffset();
        return ScalePosition(new Vector2(local.X, -local.Y));
    }
}
