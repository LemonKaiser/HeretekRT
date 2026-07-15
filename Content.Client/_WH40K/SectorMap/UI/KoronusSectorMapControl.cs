using System.Numerics;
using System.Linq;
using System.Text;
using Content.Shared._WH40K.SectorMap.BUI;
using Content.Shared.Shuttles.Systems;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.SectorMap.UI;

/// <summary>
/// Interactive presentation of the fixed Koronus graph. It contains no authority over routes or FTL.
/// </summary>
public sealed class KoronusSectorMapControl : Control
{
    private const float MinimumZoom = 0.35f;
    private const float MaximumZoom = 3f;
    private const float NodeRadius = 5f;
    private const float SelectionRadius = 20f;

    // The palette follows Rogue Trader's navigation language: jade for charted space, gold for the current system,
    // and increasingly warm routes for growing Warp danger.
    private static readonly Color CurrentSystemColor = Color.FromHex("#F4D64E");
    private static readonly Color ReachableSystemColor = Color.FromHex("#9BDE67");
    private static readonly Color ChartedSystemColor = Color.FromHex("#5F9C66");
    private static readonly Color UnavailableSystemColor = Color.FromHex("#74746E");
    private static readonly Color StableRouteColor = Color.FromHex("#70B843");
    private static readonly Color DangerousRouteColor = Color.FromHex("#E0AC39");
    private static readonly Color ForbiddenRouteColor = Color.FromHex("#C95043");

    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Texture _background;
    private readonly Font _font;
    private readonly Font _labelFont;
    private readonly List<Vector2> _thickSegmentVertices = new(6);
    private readonly Dictionary<string, KoronusSectorNodeState> _systemsById = new();
    private readonly Dictionary<string, string[]> _systemLabelLines = new();
    private readonly List<KoronusSectorNodeState> _orderedLabelNodes = new();
    private readonly List<UIBox2> _occupiedLabelBounds = new();
    private readonly List<string> _cardDetailLines = new();
    private readonly Comparison<KoronusSectorNodeState> _labelNodeComparison;
    private KoronusSectorInterfaceState _state = KoronusSectorInterfaceState.Unavailable();
    private Vector2 _pan;
    private float _zoom = 1f;
    private float _animationTime;
    private Vector2 _focusPan;
    private float _focusZoom;
    private bool _dragging;
    private bool _focusingSystem;
    private string? _hoveredSystem;
    private string? _selectedSystem;
    private UIBox2? _warpButtonBounds;
    private bool _warpButtonHovered;

    public event Action<string>? RequestSectorJump;

    public KoronusSectorMapControl()
    {
        IoCManager.InjectDependencies(this);

        _background = _resources.GetResource<TextureResource>("/Textures/_WH40K/UI/SectorMap/koronus-expanse-map.png").Texture;
        _font = new VectorFont(
            _resources.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"),
            12);
        _labelFont = new VectorFont(
            _resources.GetResource<FontResource>("/Fonts/NotoSansDisplay/NotoSansDisplay-Italic.ttf"),
            12);
        _labelNodeComparison = CompareLabelNodes;

        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void UpdateState(KoronusSectorInterfaceState state)
    {
        _state = state;
        RebuildRenderCaches();

        if (state.WarpTravel != null)
        {
            _selectedSystem = null;
            _warpButtonBounds = null;
            _warpButtonHovered = false;
            _focusingSystem = false;
            return;
        }

        if (_selectedSystem != null && FindSystem(_selectedSystem) == null)
        {
            _selectedSystem = null;
            _warpButtonBounds = null;
            _warpButtonHovered = false;
            _focusingSystem = false;
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // This uses client frame time only: animations are presentational and must not depend on map pan,
        // zoom, or server ticks.
        _animationTime = (_animationTime + args.DeltaSeconds) % 3600f;

        if (!_focusingSystem)
            return;

        var smoothing = 1f - MathF.Exp(-7f * args.DeltaSeconds);
        _zoom += (_focusZoom - _zoom) * smoothing;
        _pan += (_focusPan - _pan) * smoothing;

        if (MathF.Abs(_focusZoom - _zoom) < 0.002f && Vector2.DistanceSquared(_focusPan, _pan) < 0.01f)
        {
            _zoom = _focusZoom;
            _pan = _focusPan;
            _focusingSystem = false;
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UseSecondary)
            return;

        _focusingSystem = false;
        _dragging = true;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UseSecondary)
        {
            _dragging = false;
            args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.UIClick && _state.Available)
        {
            if (_state.WarpTravel != null)
            {
                args.Handle();
                return;
            }

            if (_warpButtonBounds?.Contains(args.RelativePixelPosition) == true)
            {
                var selected = FindSystem(_selectedSystem);
                if (selected != null && selected.Reachable && _state.CanJump)
                    RequestSectorJump?.Invoke(selected.Id);

                args.Handle();
                return;
            }

            var node = FindNode(args.RelativePixelPosition);
            if (node != null)
            {
                SelectSystem(node);
                args.Handle();
                return;
            }

            _selectedSystem = null;
            _warpButtonBounds = null;
            _warpButtonHovered = false;
        }

        base.KeyBindUp(args);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_dragging)
        {
            _focusingSystem = false;
            _pan += new Vector2(args.Relative.X, args.Relative.Y);
        }

        _warpButtonHovered = _warpButtonBounds?.Contains(args.RelativePixelPosition) == true;
        _hoveredSystem = _warpButtonHovered ? null : FindNode(args.RelativePixelPosition)?.Id;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        _focusingSystem = false;
        var before = ScreenToNormalized(args.RelativePixelPosition);
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, args.Delta.Y), MinimumZoom, MaximumZoom);
        var after = NormalizedToScreen(before);
        _pan += args.RelativePixelPosition - after;
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // The authored parchment ends at the image frame; every uncovered area deliberately falls into black.
        handle.DrawRect(PixelSizeBox, Color.Black);
        if (!_state.Available)
        {
            DrawCentered(handle, Loc.GetString("koronus-sector-map-unavailable"), Color.LightGray);
            return;
        }

        var imageBox = GetImageBox();
        handle.DrawTextureRect(_background, imageBox);

        var systems = _systemsById;
        foreach (var route in _state.Routes)
        {
            if (!systems.TryGetValue(route.From, out var from) || !systems.TryGetValue(route.To, out var to))
                continue;

            var directRoute = route.Enabled && (from.Current || to.Current);
            DrawRoute(
                handle,
                NormalizedToScreen(from.Position),
                NormalizedToScreen(to.Position),
                GetRouteColor(route.RouteClass, route.Enabled),
                route.Enabled,
                directRoute);
        }

        if (_state.WarpTravel != null)
            DrawWarpTravelShip(handle, systems, _state.WarpTravel);

        foreach (var node in _state.Systems)
            DrawNode(handle, node);

        DrawSystemLabels(handle);
        if (_state.WarpTravel != null)
            DrawWarpTravelStatus(handle, systems, _state.WarpTravel);
        else
            DrawSelectedSystemCard(handle);
        DrawLegend(handle);
        DrawControlsHint(handle);
    }

    private void DrawNode(DrawingHandleScreen handle, KoronusSectorNodeState node)
    {
        var position = NormalizedToScreen(node.Position);
        var color = GetNodeColor(node);
        var radius = NodeRadius * UIScale * (node.Current ? 1.15f : 1f);
        var highlighted = node.Current || node.Reachable || node.Id == _hoveredSystem || node.Id == _selectedSystem;
        var phase = _animationTime + GetNodePhase(node.Id);

        if (node.Current)
        {
            DrawCurrentSystemBeacon(handle, position, radius, color, phase);
            return;
        }

        if (node.Reachable)
        {
            DrawReachableSystemBeacon(handle, position, radius, color, phase);
            return;
        }

        DrawChartedSystemBeacon(handle, position, radius, color, highlighted);
    }

    private void DrawChartedSystemBeacon(
        DrawingHandleScreen handle,
        Vector2 position,
        float radius,
        Color color,
        bool hovered)
    {
        var frameRadius = radius + 3.6f * UIScale;
        var ringColor = hovered ? Color.White : Color.FromHex("#6B7068");

        if (hovered)
            handle.DrawCircle(position, frameRadius + 5f * UIScale, color.WithAlpha(0.12f));

        handle.DrawCircle(position, frameRadius + 1.8f * UIScale, Color.Black.WithAlpha(0.9f));
        DrawBeaconWings(handle, position, radius + 1.5f * UIScale, frameRadius + 1.5f * UIScale, 1.7f * UIScale,
            ringColor.WithAlpha(hovered ? 0.92f : 0.48f));
        handle.DrawCircle(position, frameRadius, ringColor.WithAlpha(hovered ? 0.96f : 0.72f), filled: false);
        handle.DrawCircle(position, radius + 1.15f * UIScale, Color.FromHex("#10120F"));
        handle.DrawCircle(position, radius, ringColor.WithAlpha(0.72f), filled: false);
        handle.DrawCircle(position, MathF.Max(1.15f * UIScale, radius * 0.28f), Color.White.WithAlpha(hovered ? 0.9f : 0.65f));
    }

    private void DrawReachableSystemBeacon(
        DrawingHandleScreen handle,
        Vector2 position,
        float radius,
        Color color,
        float phase)
    {
        var pulse = (MathF.Sin(phase * 2.2f) + 1f) * 0.5f;
        var frameRadius = radius + 5f * UIScale;

        handle.DrawCircle(position, frameRadius + (4f + pulse * 3f) * UIScale, color.WithAlpha(0.05f + pulse * 0.06f));
        handle.DrawCircle(position, frameRadius + 2f * UIScale, Color.Black.WithAlpha(0.9f));
        DrawBeaconWings(handle, position, radius + 1.5f * UIScale, frameRadius + 2f * UIScale, 2.1f * UIScale,
            color.WithAlpha(0.66f + pulse * 0.22f));
        handle.DrawCircle(position, frameRadius, color.WithAlpha(0.80f + pulse * 0.14f), filled: false);
        handle.DrawCircle(position, radius + 1.35f * UIScale, Color.FromHex("#10140E"));
        handle.DrawCircle(position, radius, color.WithAlpha(0.88f), filled: false);
        handle.DrawCircle(position, MathF.Max(1.3f * UIScale, radius * 0.34f), Color.InterpolateBetween(color, Color.White, 0.58f));
    }

    private void DrawCurrentSystemBeacon(
        DrawingHandleScreen handle,
        Vector2 position,
        float radius,
        Color color,
        float phase)
    {
        var pulse = (MathF.Sin(phase * 2.7f) + 1f) * 0.5f;
        var frameRadius = radius + 11f * UIScale;
        var compassRadius = (frameRadius + 4f * UIScale) * 1.5f;
        var signalRadius = compassRadius + (2f + pulse * 4f) * UIScale;
        var compassAngle = GetCompassHeading();
        var compassDirection = new Vector2(MathF.Cos(compassAngle), MathF.Sin(compassAngle));

        // The selected system reads as a live astrolabe: a fixed four-point frame, a breathing signal ring,
        // and a double compass needle that slowly settles on a new heading every few seconds.
        handle.DrawCircle(position, signalRadius, color.WithAlpha((1f - pulse) * 0.16f), filled: false);
        handle.DrawCircle(position, frameRadius + 4f * UIScale, color.WithAlpha(0.12f + pulse * 0.08f));
        handle.DrawCircle(position, frameRadius + 2.2f * UIScale, Color.Black.WithAlpha(0.94f));
        DrawBeaconWings(handle, position, radius + 2f * UIScale, frameRadius + 4f * UIScale, 3.0f * UIScale,
            color.WithAlpha(0.82f));
        handle.DrawCircle(position, frameRadius, color.WithAlpha(0.96f), filled: false);
        DrawBeaconTicks(handle, position, frameRadius, color.WithAlpha(0.80f));

        DrawCompassNeedle(handle, position, compassDirection, compassRadius, color);

        handle.DrawCircle(position, radius + 2.4f * UIScale, Color.Black.WithAlpha(0.96f));
        handle.DrawCircle(position, radius + 0.8f * UIScale, color.WithAlpha(0.96f), filled: false);
        handle.DrawCircle(position, radius - 1.05f * UIScale, Color.FromHex("#191605"));
        handle.DrawCircle(position, MathF.Max(1.55f * UIScale, radius * 0.38f), Color.InterpolateBetween(color, Color.White, 0.74f));
    }

    private float GetCurrentBeaconOuterRadius(float radius)
    {
        var compassRadius = (radius + 15f * UIScale) * 1.5f;
        return compassRadius + 6f * UIScale;
    }

    private void DrawBeaconWings(
        DrawingHandleScreen handle,
        Vector2 position,
        float innerRadius,
        float outerRadius,
        float halfWidth,
        Color color)
    {
        for (var i = 0; i < 4; i++)
        {
            var angle = MathF.PI / 4f + i * MathF.PI / 2f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var normal = new Vector2(-direction.Y, direction.X);
            var basePoint = position + direction * innerRadius;

            _thickSegmentVertices.Clear();
            _thickSegmentVertices.Add(basePoint + normal * halfWidth);
            _thickSegmentVertices.Add(position + direction * outerRadius);
            _thickSegmentVertices.Add(basePoint - normal * halfWidth);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _thickSegmentVertices, color);
        }
    }

    private void DrawBeaconTicks(DrawingHandleScreen handle, Vector2 position, float radius, Color color)
    {
        for (var i = 0; i < 4; i++)
        {
            var angle = i * MathF.PI / 2f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            DrawThickSegment(
                handle,
                position + direction * (radius - 2f * UIScale),
                position + direction * (radius + 2.5f * UIScale),
                1.15f * UIScale,
                color);
        }
    }

    private void DrawCompassNeedle(
        DrawingHandleScreen handle,
        Vector2 position,
        Vector2 direction,
        float radius,
        Color color)
    {
        var normal = new Vector2(-direction.Y, direction.X);
        var needleLength = radius - 5f * UIScale;
        DrawCompassNeedleHead(handle, position, direction, normal, needleLength, 3.75f * UIScale, Color.Black.WithAlpha(0.94f));
        DrawCompassNeedleHead(handle, position, -direction, -normal, needleLength, 3.2f * UIScale, Color.Black.WithAlpha(0.94f));
        DrawCompassNeedleHead(handle, position, direction, normal, needleLength, 2.48f * UIScale,
            Color.InterpolateBetween(color, Color.White, 0.54f));
        DrawCompassNeedleHead(handle, position, -direction, -normal, needleLength, 2.03f * UIScale,
            color.WithAlpha(0.72f));
    }

    private void DrawCompassNeedleHead(
        DrawingHandleScreen handle,
        Vector2 position,
        Vector2 direction,
        Vector2 normal,
        float length,
        float halfWidth,
        Color color)
    {
        var baseCenter = position + direction * (1.5f * UIScale);

        _thickSegmentVertices.Clear();
        _thickSegmentVertices.Add(baseCenter + normal * halfWidth);
        _thickSegmentVertices.Add(position + direction * length);
        _thickSegmentVertices.Add(baseCenter - normal * halfWidth);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _thickSegmentVertices, color);
    }

    private float GetCompassHeading()
    {
        const float headingInterval = 5f;
        const float turnDuration = 1.25f;
        var currentInterval = (int) MathF.Floor(_animationTime / headingInterval);
        var intervalProgress = _animationTime % headingInterval;
        var turnProgress = Math.Clamp(intervalProgress / turnDuration, 0f, 1f);
        turnProgress = turnProgress * turnProgress * (3f - 2f * turnProgress);
        return LerpAngle(GetCompassHeadingForInterval(currentInterval - 1),
            GetCompassHeadingForInterval(currentInterval), turnProgress);
    }

    private static float GetCompassHeadingForInterval(int interval)
    {
        uint value = unchecked((uint) interval) * 747796405u + 2891336453u;
        value = (value >> ((int) (value >> 28) + 4)) ^ value;
        value *= 277803737u;
        value = (value >> 22) ^ value;
        return (value & 0xFFFF) / 65535f * MathF.PI * 2f;
    }

    private static float LerpAngle(float from, float to, float amount)
    {
        var delta = (to - from + MathF.PI) % (MathF.PI * 2f);
        if (delta < 0f)
            delta += MathF.PI * 2f;

        return from + (delta - MathF.PI) * amount;
    }

    private static float GetNodePhase(string id)
    {
        uint hash = 2166136261;
        foreach (var character in id)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return (hash & 0xFFFF) / 65535f * MathF.PI * 2f;
    }

    private void DrawRoute(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color, bool enabled, bool directRoute)
    {
        var direction = end - start;
        var length = direction.Length();
        if (length <= 0.01f)
            return;

        var routeColor = !enabled
            ? Color.FromHex("#4E514C")
            : directRoute
                ? color
                : Color.InterpolateBetween(color, Color.FromHex("#343832"), 0.72f);
        var outerWidth = (directRoute ? 4.5f : enabled ? 3.4f : 2.7f) * UIScale;
        var coreWidth = (directRoute ? 2.1f : 1.25f) * UIScale;
        var dashLength = (directRoute ? 15f : 10f) * UIScale;
        var gapLength = (directRoute ? 7f : 6f) * UIScale;
        var endpointInset = outerWidth + 4f * UIScale;

        // Routes are deliberately straight dashed lanes. Unlike the former screen-coordinate curve seed,
        // every dash is derived only from its endpoints, so panning cannot flip or visibly jump a route.
        DrawDashedRoute(handle, start, end, endpointInset, dashLength, gapLength, outerWidth, Color.Black.WithAlpha(0.76f));
        DrawDashedRoute(handle, start, end, endpointInset, dashLength, gapLength, coreWidth,
            routeColor.WithAlpha(directRoute ? 0.96f : enabled ? 0.72f : 0.44f));
    }

    private void DrawThickSegment(DrawingHandleScreen handle, Vector2 start, Vector2 end, float width, Color color)
    {
        var direction = end - start;
        var length = direction.Length();
        if (length <= 0.01f)
            return;

        var normal = new Vector2(-direction.Y, direction.X) / length * width / 2f;
        _thickSegmentVertices.Clear();
        _thickSegmentVertices.Add(start + normal);
        _thickSegmentVertices.Add(end + normal);
        _thickSegmentVertices.Add(end - normal);
        _thickSegmentVertices.Add(start + normal);
        _thickSegmentVertices.Add(end - normal);
        _thickSegmentVertices.Add(start - normal);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _thickSegmentVertices, color);
    }

    private void DrawDashedRoute(
        DrawingHandleScreen handle,
        Vector2 start,
        Vector2 end,
        float endpointInset,
        float dashLength,
        float gapLength,
        float width,
        Color color)
    {
        var direction = end - start;
        var length = direction.Length();
        if (length <= endpointInset * 2f)
            return;

        var normalized = direction / length;
        var routeStart = endpointInset;
        var routeEnd = length - endpointInset;
        for (var cursor = routeStart; cursor < routeEnd; cursor += dashLength + gapLength)
        {
            var dashEnd = MathF.Min(cursor + dashLength, routeEnd);
            DrawThickSegment(handle, start + normalized * cursor, start + normalized * dashEnd, width, color);
        }
    }

    private void DrawWarpTravelShip(
        DrawingHandleScreen handle,
        Dictionary<string, KoronusSectorNodeState> systems,
        KoronusSectorTravelState travel)
    {
        if (!systems.TryGetValue(travel.OriginSystem, out var origin) ||
            !systems.TryGetValue(travel.DestinationSystem, out var destination))
        {
            return;
        }

        var start = NormalizedToScreen(origin.Position);
        var end = NormalizedToScreen(destination.Position);
        var route = end - start;
        var routeLength = route.Length();
        if (routeLength <= 0.01f)
            return;

        var direction = route / routeLength;
        var normal = new Vector2(-direction.Y, direction.X);
        var progress = GetWarpTravelProgress(travel);
        // Keep the marker clear of the two beacons while still mapping its position linearly to the real FTL timer.
        var visualProgress = 0.07f + progress * 0.86f;
        var position = Vector2.Lerp(start, end, visualProgress);
        var pulse = (MathF.Sin(_animationTime * 7f) + 1f) * 0.5f;
        var glowRadius = (8f + pulse * 3f) * UIScale;

        handle.DrawCircle(position, glowRadius, CurrentSystemColor.WithAlpha(0.11f + pulse * 0.09f));

        var nose = position + direction * 8f * UIScale;
        var stern = position - direction * 6f * UIScale;
        var wingSpan = 5f * UIScale;
        _thickSegmentVertices.Clear();
        _thickSegmentVertices.Add(nose);
        _thickSegmentVertices.Add(stern + normal * wingSpan);
        _thickSegmentVertices.Add(stern - normal * wingSpan);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _thickSegmentVertices, Color.Black.WithAlpha(0.96f));

        var innerNose = position + direction * 6.3f * UIScale;
        var innerStern = position - direction * 4.3f * UIScale;
        _thickSegmentVertices.Clear();
        _thickSegmentVertices.Add(innerNose);
        _thickSegmentVertices.Add(innerStern + normal * 3.15f * UIScale);
        _thickSegmentVertices.Add(innerStern - normal * 3.15f * UIScale);
        handle.DrawPrimitives(
            DrawPrimitiveTopology.TriangleList,
            _thickSegmentVertices,
            Color.InterpolateBetween(CurrentSystemColor, Color.White, 0.58f));

        DrawThickSegment(
            handle,
            stern + normal * wingSpan,
            stern - normal * wingSpan,
            1.1f * UIScale,
            CurrentSystemColor.WithAlpha(0.92f));
    }

    private float GetWarpTravelProgress(KoronusSectorTravelState travel)
    {
        if (travel.State is FTLState.Arriving or FTLState.Cooldown)
            return 1f;

        if (travel.State != FTLState.Travelling)
            return 0f;

        var progress = travel.StateTime.ProgressAt(_timing.CurTime);
        return float.IsNaN(progress) ? 0f : Math.Clamp(progress, 0f, 1f);
    }

    private string GetWarpTravelRemaining(KoronusSectorTravelState travel)
    {
        var remaining = Math.Max(0, (int) Math.Ceiling((travel.StateTime.End - _timing.CurTime).TotalSeconds));
        return $"{remaining / 60:00}:{remaining % 60:00}";
    }

    private void DrawWarpTravelStatus(
        DrawingHandleScreen handle,
        Dictionary<string, KoronusSectorNodeState> systems,
        KoronusSectorTravelState travel)
    {
        if (!systems.TryGetValue(travel.OriginSystem, out var origin) ||
            !systems.TryGetValue(travel.DestinationSystem, out var destination))
        {
            return;
        }

        var remaining = GetWarpTravelRemaining(travel);
        var text = Loc.GetString(
            "koronus-sector-map-warp-travelling",
            ("origin", origin.Name),
            ("destination", destination.Name),
            ("remaining", remaining));
        var scale = 0.68f * UIScale;
        var dimensions = handle.GetDimensions(_font, text, scale);
        var size = dimensions + new Vector2(20f, 14f) * UIScale;
        var panel = UIBox2.FromDimensions(new Vector2(12f, 12f) * UIScale, size);

        handle.DrawRect(panel, Color.Black.WithAlpha(0.78f));
        handle.DrawString(
            _font,
            panel.TopLeft + new Vector2(10f, (panel.Size.Y - dimensions.Y) / (2f * UIScale)) * UIScale,
            text,
            scale,
            Color.White.WithAlpha(0.94f));
    }

    private void DrawSystemLabels(DrawingHandleScreen handle)
    {
        _occupiedLabelBounds.Clear();
        _orderedLabelNodes.Sort(_labelNodeComparison);

        foreach (var node in _orderedLabelNodes)
        {
            var special = node.Current || node.Reachable || node.Id == _hoveredSystem || node.Id == _selectedSystem;
            if (_zoom < 0.62f && !special)
                continue;

            var position = NormalizedToScreen(node.Position);
            var radius = NodeRadius * UIScale * (node.Current ? 1.15f : 1f);
            var outerRadius = node.Current
                ? GetCurrentBeaconOuterRadius(radius)
                : node.Reachable
                    ? radius + 8f * UIScale
                    : radius + 3f * UIScale;
            var lines = _systemLabelLines[node.Id];
            var bounds = GetNodeLabelBounds(handle, position, outerRadius, lines, special);

            // At overview zoom the central systems are dense. Hide colliding secondary labels instead of letting
            // them form an unreadable dark wall; they remain visible after zooming or hovering.
            if (!special && IntersectsOccupiedLabel(bounds))
                continue;

            var color = special
                ? node.Id == _hoveredSystem || node.Id == _selectedSystem ? Color.White : GetNodeColor(node)
                : Color.FromHex("#C0C2B8");
            DrawNodeLabel(handle, position, outerRadius, lines, color, special, node.Current);
            _occupiedLabelBounds.Add(bounds);
        }
    }

    private bool IntersectsOccupiedLabel(UIBox2 bounds)
    {
        foreach (var occupied in _occupiedLabelBounds)
        {
            if (occupied.Intersects(bounds))
                return true;
        }

        return false;
    }

    private int CompareLabelNodes(KoronusSectorNodeState left, KoronusSectorNodeState right)
    {
        var priorityComparison = GetLabelPriority(right).CompareTo(GetLabelPriority(left));
        return priorityComparison != 0
            ? priorityComparison
            : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
    }

    private int GetLabelPriority(KoronusSectorNodeState node)
    {
        if (node.Current)
            return 4;
        if (node.Id == _selectedSystem)
            return 3;
        if (node.Id == _hoveredSystem)
            return 2;
        if (node.Reachable)
            return 1;
        return 1;
    }

    private UIBox2 GetNodeLabelBounds(DrawingHandleScreen handle, Vector2 position, float outerRadius, string[] lines, bool special)
    {
        var scale = GetLabelScale(special);
        var maxWidth = 0f;
        foreach (var line in lines)
        {
            maxWidth = MathF.Max(maxWidth, handle.GetDimensions(_labelFont, line, scale).X);
        }

        var lineHeight = handle.GetDimensions(_labelFont, "Ag", scale).Y;
        var top = position.Y + outerRadius + 5f * UIScale;
        var height = lineHeight * lines.Length + 2f * UIScale;
        return UIBox2.FromDimensions(
            new Vector2(position.X - maxWidth / 2f - 5f * UIScale, top - 2f * UIScale),
            new Vector2(maxWidth + 10f * UIScale, height));
    }

    private void DrawNodeLabel(
        DrawingHandleScreen handle,
        Vector2 position,
        float outerRadius,
        string[] lines,
        Color color,
        bool special,
        bool curved)
    {
        var scale = GetLabelScale(special);
        var top = position.Y + outerRadius + 5f * UIScale;

        // A short current-system title is the only curved label: each glyph follows one shared baseline
        // and rotates on its tangent. This keeps the accent readable instead of forming a decorative semicircle.
        if (curved && lines.Length == 1)
        {
            DrawCurvedLabel(handle, position, outerRadius, lines[0], scale, color.WithAlpha(0.96f));
            return;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var dimensions = handle.GetDimensions(_labelFont, lines[i], scale);
            var lineTop = top + dimensions.Y * i;
            var labelPosition = new Vector2(position.X - dimensions.X / 2f, lineTop);
            DrawReadableString(handle, labelPosition, lines[i], scale, color.WithAlpha(special ? 0.96f : 0.78f));
        }
    }

    private void DrawCurvedLabel(
        DrawingHandleScreen handle,
        Vector2 position,
        float outerRadius,
        string text,
        float scale,
        Color color)
    {
        var textWidth = GetLabelAdvance(text, scale);
        if (textWidth <= 0f)
            return;

        // Limit the whole title to a gentle ~28 degree arc. A broad semicircle is hard to read and makes
        // ordinary system names look like an emblem rather than navigation text.
        var radius = MathF.Max(96f * UIScale, textWidth / 0.48f);
        var baselineY = position.Y + outerRadius + 5f * UIScale + _labelFont.GetAscent(scale);
        var circleCenter = new Vector2(position.X, baselineY - radius);
        var cursor = -textWidth / 2f;
        var previousTransform = handle.GetTransform();
        var shadowOffset = MathF.Max(0.8f, UIScale * 0.8f);

        try
        {
            foreach (var rune in text.EnumerateRunes())
            {
                var metrics = _labelFont.GetCharMetrics(rune, scale);
                if (metrics == null)
                    continue;

                var angle = cursor / radius;
                var baseline = circleCenter + new Vector2(
                    MathF.Sin(angle) * radius,
                    MathF.Cos(angle) * radius);
                var glyphTransform = Matrix3x2.CreateRotation(-angle, baseline) * previousTransform;
                handle.SetTransform(glyphTransform);
                _labelFont.DrawChar(handle, rune, baseline + new Vector2(shadowOffset, shadowOffset), scale, Color.Black.WithAlpha(0.88f));
                _labelFont.DrawChar(handle, rune, baseline, scale, color);
                cursor += metrics.Value.Advance;
            }
        }
        finally
        {
            handle.SetTransform(previousTransform);
        }
    }

    private float GetLabelAdvance(string text, float scale)
    {
        var advance = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var metrics = _labelFont.GetCharMetrics(rune, scale);
            if (metrics != null)
                advance += metrics.Value.Advance;
        }

        return advance;
    }

    private void DrawReadableString(DrawingHandleScreen handle, Vector2 position, string text, float scale, Color color)
    {
        var shadowOffset = MathF.Max(0.8f, UIScale * 0.8f);
        handle.DrawString(_labelFont, position + new Vector2(shadowOffset, shadowOffset), text, scale, Color.Black.WithAlpha(0.88f));
        handle.DrawString(_labelFont, position, text, scale, color);
    }

    private float GetLabelScale(bool special)
    {
        // Labels follow map zoom, but the overview keeps a readable lower bound rather than collapsing into noise.
        var zoomScale = Math.Clamp(MathF.Pow(_zoom, 0.55f), 0.85f, 1.55f);
        return (special ? 0.70f : 0.62f) * UIScale * zoomScale;
    }

    private static string[] SplitLabel(string name)
    {
        if (name.Length <= 25)
            return [name];

        var splitIndex = name.LastIndexOf(' ', name.Length / 2);
        return splitIndex > 0
            ? [name[..splitIndex], name[(splitIndex + 1)..]]
            : [name];
    }

    private void SelectSystem(KoronusSectorNodeState node)
    {
        _selectedSystem = node.Id;
        _focusZoom = Math.Clamp(MathF.Max(_zoom, 1.65f), MinimumZoom, MaximumZoom);

        var baseScale = MathF.Min(PixelWidth / (float) _background.Width, PixelHeight / (float) _background.Height);
        var targetSize = _background.Size * baseScale * _focusZoom;
        var desiredPosition = PixelSize * new Vector2(0.43f, 0.57f);
        _focusPan = desiredPosition - (PixelSize - targetSize) / 2f - targetSize * node.Position;
        _focusingSystem = true;
    }

    private KoronusSectorNodeState? FindSystem(string? id)
    {
        return id != null && _systemsById.TryGetValue(id, out var node) ? node : null;
    }

    private void DrawSelectedSystemCard(DrawingHandleScreen handle)
    {
        _warpButtonBounds = null;
        var node = FindSystem(_selectedSystem);
        if (node == null)
            return;

        var cardSize = new Vector2(210f, 100f) * UIScale;
        var nodePosition = NormalizedToScreen(node.Position);
        // Keep the dossier visually attached to its beacon; it should read as an inspection popup, not a remote panel.
        var cardPosition = nodePosition + new Vector2(20f, -18f) * UIScale;

        if (cardPosition.X + cardSize.X > PixelWidth - 10f * UIScale)
            cardPosition.X = nodePosition.X - cardSize.X - 20f * UIScale;
        if (cardPosition.Y + cardSize.Y > PixelHeight - 10f * UIScale)
            cardPosition.Y = nodePosition.Y - cardSize.Y - 18f * UIScale;

        cardPosition.X = Math.Clamp(cardPosition.X, 10f * UIScale, PixelWidth - cardSize.X - 10f * UIScale);
        cardPosition.Y = Math.Clamp(cardPosition.Y, 48f * UIScale, PixelHeight - cardSize.Y - 10f * UIScale);
        var card = UIBox2.FromDimensions(cardPosition, cardSize);
        var canWarp = node.Reachable && _state.CanJump;
        var accent = node.Current ? CurrentSystemColor : node.Reachable ? ReachableSystemColor : UnavailableSystemColor;

        handle.DrawRect(card, Color.Black.WithAlpha(0.88f));
        handle.DrawRect(card, accent.WithAlpha(0.92f), filled: false);
        handle.DrawRect(UIBox2.FromDimensions(card.TopLeft, new Vector2(card.Size.X, 2f * UIScale)), accent);

        var titleScale = 0.76f * UIScale;
        var titleLines = _systemLabelLines[node.Id];
        for (var i = 0; i < titleLines.Length; i++)
        {
            var titlePosition = card.TopLeft + new Vector2(9f, 7f + 13f * i) * UIScale;
            DrawReadableString(handle, titlePosition, titleLines[i], titleScale, Color.White);
        }

        var detailScale = 0.58f * UIScale;
        WrapCardText(
            handle,
            GetSystemCardDetail(node),
            detailScale,
            card.Size.X - 18f * UIScale,
            _cardDetailLines);
        for (var i = 0; i < _cardDetailLines.Count; i++)
        {
            var detailPosition = card.TopLeft + new Vector2(9f, 40f + 12f * i) * UIScale;
            handle.DrawString(_font, detailPosition, _cardDetailLines[i], detailScale, accent.WithAlpha(0.94f));
        }

        var button = UIBox2.FromDimensions(
            card.TopLeft + new Vector2(9f, card.Size.Y / UIScale - 29f) * UIScale,
            new Vector2(card.Size.X / UIScale - 18f, 20f) * UIScale);
        _warpButtonBounds = button;
        var buttonHovered = canWarp && _warpButtonHovered;
        var buttonPulse = (MathF.Sin(_animationTime * 5f) + 1f) * 0.5f;
        var buttonColor = !canWarp
            ? Color.FromHex("#555850")
            : buttonHovered
                ? Color.InterpolateBetween(ReachableSystemColor, Color.White, 0.42f)
                : ReachableSystemColor;

        if (buttonHovered)
        {
            var glow = UIBox2.FromDimensions(
                button.TopLeft - new Vector2(2f, 2f) * UIScale,
                button.Size + new Vector2(4f, 4f) * UIScale);
            handle.DrawRect(glow, ReachableSystemColor.WithAlpha(0.52f + buttonPulse * 0.24f), filled: false);
        }

        handle.DrawRect(
            button,
            !canWarp
                ? Color.FromHex("#252725")
                : buttonHovered
                    ? Color.FromHex("#315E22")
                    : Color.FromHex("#173619"));
        handle.DrawRect(button, buttonColor.WithAlpha(canWarp ? 0.98f : 0.52f), filled: false);

        if (buttonHovered)
        {
            var centerY = button.TopLeft.Y + button.Size.Y / 2f;
            var left = button.TopLeft.X + 14f * UIScale;
            var right = button.BottomRight.X - 14f * UIScale;
            var chevronSize = 3.3f * UIScale;
            DrawThickSegment(handle, new Vector2(left - chevronSize, centerY - chevronSize), new Vector2(left, centerY), 1.1f * UIScale, buttonColor);
            DrawThickSegment(handle, new Vector2(left - chevronSize, centerY + chevronSize), new Vector2(left, centerY), 1.1f * UIScale, buttonColor);
            DrawThickSegment(handle, new Vector2(right + chevronSize, centerY - chevronSize), new Vector2(right, centerY), 1.1f * UIScale, buttonColor);
            DrawThickSegment(handle, new Vector2(right + chevronSize, centerY + chevronSize), new Vector2(right, centerY), 1.1f * UIScale, buttonColor);
        }

        var buttonText = canWarp
            ? Loc.GetString("koronus-sector-card-warp")
            : Loc.GetString("koronus-sector-card-warp-unavailable");
        var buttonDimensions = handle.GetDimensions(_font, buttonText, 0.62f * UIScale);
        handle.DrawString(
            _font,
            button.TopLeft + (button.Size - buttonDimensions) / 2f,
            buttonText,
            0.62f * UIScale,
            canWarp ? buttonHovered ? Color.White : Color.White.WithAlpha(0.92f) : Color.LightGray.WithAlpha(0.62f));
    }

    private string GetSystemCardDetail(KoronusSectorNodeState node)
    {
        if (node.Current)
            return Loc.GetString("koronus-sector-card-current");
        if (!_state.CanJump)
            return Loc.GetString("koronus-sector-card-drive-offline");
        if (!node.Reachable)
            return Loc.GetString("koronus-sector-card-no-route");

        var route = _state.Routes.FirstOrDefault(candidate =>
            candidate.Enabled &&
            ((candidate.From == _state.CurrentSystem && candidate.To == node.Id) ||
             (candidate.To == _state.CurrentSystem && candidate.From == node.Id)));
        var routeName = route?.RouteClass switch
        {
            "Dangerous" => Loc.GetString("koronus-sector-card-route-dangerous"),
            "Forbidden" => Loc.GetString("koronus-sector-card-route-forbidden"),
            _ => Loc.GetString("koronus-sector-card-route-stable"),
        };
        return Loc.GetString("koronus-sector-card-route", ("route", routeName));
    }

    private void WrapCardText(DrawingHandleScreen handle, string text, float scale, float maxWidth, List<string> lines)
    {
        lines.Clear();
        var line = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && handle.GetDimensions(_font, candidate, scale).X > maxWidth)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
            lines.Add(line);
    }

    private void RebuildRenderCaches()
    {
        _systemsById.Clear();
        _systemLabelLines.Clear();
        _orderedLabelNodes.Clear();

        foreach (var node in _state.Systems)
        {
            _systemsById[node.Id] = node;
            _systemLabelLines[node.Id] = SplitLabel(node.Name);
            _orderedLabelNodes.Add(node);
        }
    }

    private void DrawLegend(DrawingHandleScreen handle)
    {
        var entries = new (Color Color, string Text)[]
        {
            (CurrentSystemColor, Loc.GetString("koronus-sector-map-legend-current")),
            (ReachableSystemColor, Loc.GetString("koronus-sector-map-legend-reachable")),
            (StableRouteColor, Loc.GetString("koronus-sector-map-legend-stable")),
            (DangerousRouteColor, Loc.GetString("koronus-sector-map-legend-dangerous")),
        };

        var scale = 0.7f * UIScale;
        var rowHeight = 16f * UIScale;
        var size = new Vector2(198f, entries.Length * rowHeight + 12f) * UIScale;
        var origin = new Vector2(12f, PixelSize.Y - size.Y - 12f * UIScale);
        handle.DrawRect(UIBox2.FromDimensions(origin, size), Color.Black.WithAlpha(0.72f));

        for (var i = 0; i < entries.Length; i++)
        {
            var position = origin + new Vector2(10f, 8f + rowHeight * i) * UIScale;
            DrawThickSegment(
                handle,
                position + new Vector2(0f, 5f) * UIScale,
                position + new Vector2(9f, 5f) * UIScale,
                3f * UIScale,
                entries[i].Color);
            handle.DrawString(_font, position + new Vector2(14f, 0f) * UIScale, entries[i].Text, scale, Color.White);
        }
    }

    private void DrawControlsHint(DrawingHandleScreen handle)
    {
        var entries = new (Color Color, string Text)[]
        {
            (Color.FromHex("#9AA29A"), Loc.GetString("koronus-sector-map-hint-zoom")),
            (Color.FromHex("#9AA29A"), Loc.GetString("koronus-sector-map-hint-pan")),
            (Color.FromHex("#9AA29A"), Loc.GetString("koronus-sector-map-hint-select")),
        };
        var scale = 0.72f * UIScale;
        var rowHeight = 21f * UIScale;
        var maxTextWidth = entries.Max(entry => handle.GetDimensions(_font, entry.Text, scale).X);
        var size = new Vector2(maxTextWidth + 34f * UIScale, entries.Length * rowHeight + 14f * UIScale);
        var origin = new Vector2(PixelSize.X - size.X - 12f * UIScale, PixelSize.Y - size.Y - 12f * UIScale);

        handle.DrawRect(UIBox2.FromDimensions(origin, size), Color.Black.WithAlpha(0.74f));

        for (var i = 0; i < entries.Length; i++)
        {
            var dimensions = handle.GetDimensions(_font, entries[i].Text, scale);
            var rowTop = origin.Y + 7f * UIScale + rowHeight * i;
            var markerY = rowTop + rowHeight / 2f;
            var textPosition = new Vector2(
                origin.X + 16f * UIScale,
                rowTop + (rowHeight - dimensions.Y) / 2f);
            DrawThickSegment(
                handle,
                new Vector2(origin.X + 6f * UIScale, markerY),
                new Vector2(origin.X + 12f * UIScale, markerY),
                2.8f * UIScale,
                entries[i].Color);
            handle.DrawString(_font, textPosition, entries[i].Text, scale, Color.White.WithAlpha(0.94f));
        }
    }

    private KoronusSectorNodeState? FindNode(Vector2 screenPosition)
    {
        foreach (var node in _state.Systems)
        {
            if ((NormalizedToScreen(node.Position) - screenPosition).LengthSquared() <= SelectionRadius * SelectionRadius * UIScale * UIScale)
                return node;
        }

        return null;
    }

    private UIBox2 GetImageBox()
    {
        var baseScale = MathF.Min(PixelWidth / (float) _background.Width, PixelHeight / (float) _background.Height);
        var size = _background.Size * baseScale * _zoom;
        var position = (PixelSize - size) / 2f + _pan;
        return UIBox2.FromDimensions(position, size);
    }

    private Vector2 NormalizedToScreen(Vector2 normalized)
    {
        var imageBox = GetImageBox();
        return imageBox.TopLeft + imageBox.Size * normalized;
    }

    private Vector2 ScreenToNormalized(Vector2 screenPosition)
    {
        var imageBox = GetImageBox();
        return (screenPosition - imageBox.TopLeft) / imageBox.Size;
    }

    private static Color GetNodeColor(KoronusSectorNodeState node)
    {
        if (node.Current)
            return CurrentSystemColor;
        if (node.Reachable)
            return ReachableSystemColor;
        return node.Enabled ? ChartedSystemColor : UnavailableSystemColor;
    }

    private static Color GetRouteColor(string routeClass, bool enabled)
    {
        if (!enabled)
            return Color.FromHex("#5D625C");

        return routeClass switch
        {
            "Dangerous" => DangerousRouteColor,
            "Forbidden" => ForbiddenRouteColor,
            _ => StableRouteColor,
        };
    }

    private void DrawCentered(DrawingHandleScreen handle, string text, Color color)
    {
        var dimensions = handle.GetDimensions(_font, text, UIScale);
        handle.DrawString(_font, (PixelSize - dimensions) / 2f, text, UIScale, color);
    }
}
