using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.ClassProgression;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Progression;

/// <summary>
/// Mouse and keyboard driven presentation of the two prototype-authored skill branches.
/// It never predicts purchases or mutates a server snapshot.
/// </summary>
public sealed partial class Wh40kClassTreeGraphControl : Control
{
    private const float MinimumZoom = 0.32f;
    private const float MaximumZoom = 1.65f;
    private const float HorizontalSpacing = 78f;
    private const float VerticalSpacing = 64f;
    private const float NodeRadius = 18f;
    private const float RootRadius = 28f;

    private static readonly Color Background = Color.FromHex("#090A08");
    private static readonly Color Copper = Color.FromHex("#A9703D");
    private static readonly Color Gold = Color.FromHex("#D7B45C");
    private static readonly Color Points = Color.FromHex("#F2D44F");
    private static readonly Color Muted = Color.FromHex("#55554E");
    private static readonly Color LevelBlocked = Color.FromHex("#7D5B45");
    private static readonly Color PrerequisiteBlocked = Color.FromHex("#62646A");
    private static readonly Color BranchLeft = Color.FromHex("#9E704C");
    private static readonly Color BranchRight = Color.FromHex("#768B82");
    private static readonly Color Selection = Color.FromHex("#AAA79E");

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IEntitySystemManager _entitySystems = default!;

    private readonly Dictionary<string, NodeView> _nodes = new(StringComparer.Ordinal);
    private readonly SpriteSystem _sprites;
    private readonly Font _font;
    private readonly Texture _fallbackIcon;
    private readonly Texture _rootIcon;
    private Wh40kClassUiSnapshot? _snapshot;
    private Vector2 _pan;
    private float _zoom = 0.46f;
    private bool _fitPending = true;
    private bool _dragging;
    private string? _hoveredSkillId;
    private string? _focusedSkillId;
    private string? _selectedSkillId;
    private string? _confirmedSkillId;
    private float _confirmationGlow;
    private string _searchQuery = string.Empty;

    public event Action<string>? NodeSelected;
    public event Action<string>? NodeActivated;
    public event Action? EscapeRequested;

    public string? SelectedSkillId => _selectedSkillId;
    public float Zoom => _zoom;

    public Wh40kClassTreeGraphControl()
    {
        IoCManager.InjectDependencies(this);
        _sprites = _entitySystems.GetEntitySystem<SpriteSystem>();
        _font = new VectorFont(
            _resources.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"),
            12);
        _fallbackIcon = _sprites.Frame0(Wh40kClassSkillIconPaths.ClassSigil);
        _rootIcon = _fallbackIcon;

        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
    }

    public void UpdateSnapshot(Wh40kClassUiSnapshot? snapshot)
    {
        var previousPurchased = _snapshot?.Tree.PurchasedSkillIds.ToHashSet(StringComparer.Ordinal) ?? [];
        var previousClass = _snapshot?.Tree.ClassId;
        _snapshot = snapshot;
        RebuildNodes();

        if (snapshot == null)
        {
            _selectedSkillId = null;
            _focusedSkillId = null;
            return;
        }

        var newlyPurchased = snapshot.Tree.PurchasedSkillIds
            .FirstOrDefault(id => !previousPurchased.Contains(id));
        if (newlyPurchased != null)
        {
            _confirmedSkillId = newlyPurchased;
            _confirmationGlow = 1f;
        }

        if (previousClass != snapshot.Tree.ClassId ||
            _selectedSkillId != null && !_nodes.ContainsKey(_selectedSkillId))
        {
            _selectedSkillId = null;
            _focusedSkillId = null;
            _pan = Vector2.Zero;
            _zoom = 0.46f;
            _fitPending = true;
        }

        if (_focusedSkillId == null)
            _focusedSkillId = GetCurrentSkillId();
    }

    public void SetSelectedSkill(string? skillId)
    {
        if (skillId != null && !_nodes.ContainsKey(skillId))
            return;

        _selectedSkillId = skillId;
        if (skillId != null)
            _focusedSkillId = skillId;
    }

    public void SetSearchQuery(string query)
    {
        _searchQuery = query.Trim();
    }

    public void FocusCurrent()
    {
        var skillId = GetCurrentSkillId();
        if (skillId != null)
            FocusSkill(skillId, true);
    }

    public void FocusTree()
    {
        _pan = Vector2.Zero;
        _fitPending = true;
    }

    public void FocusSpecialization(string specializationId)
    {
        var skillId = _nodes.Values
            .Where(node => node.SpecializationId == specializationId)
            .OrderByDescending(node => node.State == Wh40kClassSkillNodeState.Purchased)
            .ThenBy(node => node.State == Wh40kClassSkillNodeState.Purchased ? -node.Prototype.Order : node.Prototype.Order)
            .Select(node => node.Prototype.ID)
            .FirstOrDefault();
        if (skillId != null)
            FocusSkill(skillId, true);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_fitPending && _snapshot != null && PixelSize.X > 0f && PixelSize.Y > 0f)
            FitTreeToViewport();
        _confirmationGlow = MathF.Max(0f, _confirmationGlow - args.DeltaSeconds * 0.7f);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UseSecondary)
            return;

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

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            var node = FindNode(args.RelativePixelPosition);
            if (node != null)
            {
                GrabKeyboardFocus();
                SelectNode(node.Prototype.ID);
                args.Handle();
                return;
            }
        }

        base.KeyBindUp(args);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_dragging)
            _pan += new Vector2(args.Relative.X, args.Relative.Y);

        var hovered = FindNode(args.RelativePixelPosition)?.Prototype.ID;
        if (hovered == _hoveredSkillId)
            return;

        _hoveredSkillId = hovered;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hoveredSkillId = null;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        var graphPoint = ScreenToGraph(args.RelativePixelPosition);
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, args.Delta.Y), MinimumZoom, MaximumZoom);
        _pan += args.RelativePixelPosition - GraphToScreen(graphPoint);
        args.Handle();
    }

    protected override void KeyHeld(GUIKeyEventArgs args)
    {
        base.KeyHeld(args);
        if (_nodes.Count == 0)
            return;

        switch (args.Key)
        {
            case Keyboard.Key.Up:
                Navigate(0, -1);
                break;
            case Keyboard.Key.Down:
                Navigate(0, 1);
                break;
            case Keyboard.Key.Left:
                Navigate(-1, 0);
                break;
            case Keyboard.Key.Right:
                Navigate(1, 0);
                break;
            case Keyboard.Key.Return:
            case Keyboard.Key.NumpadEnter:
            case Keyboard.Key.Space:
                if (!args.IsRepeat)
                    ActivateFocused();
                break;
            case Keyboard.Key.Escape:
                if (!args.IsRepeat)
                    EscapeRequested?.Invoke();
                break;
        }
    }

    protected override void KeyboardFocusEntered()
    {
        base.KeyboardFocusEntered();
        _focusedSkillId ??= _selectedSkillId ?? GetCurrentSkillId();
        if (_focusedSkillId != null)
            KeepFocusedNodeVisible(_focusedSkillId);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, Background);
        DrawImperialBackdrop(handle);

        if (_snapshot == null)
        {
            DrawCentered(handle, Loc.GetString("wh40k-class-ui-unavailable"), Muted);
            return;
        }

        DrawRootConnections(handle);
        foreach (var node in _nodes.Values)
        {
            foreach (var connectionId in node.Prototype.Connections)
            {
                if (!_nodes.TryGetValue(connectionId.Id, out var target))
                    continue;

                var branchColor = node.SpecializationIndex == 0 ? BranchLeft : BranchRight;
                var color = node.State == Wh40kClassSkillNodeState.Purchased &&
                            target.State == Wh40kClassSkillNodeState.Purchased
                    ? Gold
                    : target.State == Wh40kClassSkillNodeState.Available
                        ? Points.WithAlpha(0.72f)
                        : branchColor.WithAlpha(0.52f);
                DrawBranchConnection(
                    handle,
                    GraphToScreen(node.Prototype.DisplayPosition),
                    GraphToScreen(target.Prototype.DisplayPosition),
                    color);
            }
        }

        foreach (var node in _nodes.Values.OrderBy(node => node.Prototype.Order >= 19))
            DrawNode(handle, node);
        DrawClassRoot(handle);
        DrawSpecializationLabels(handle);
    }

    private void RebuildNodes()
    {
        _nodes.Clear();
        if (_snapshot == null)
            return;

        for (var specializationIndex = 0;
             specializationIndex < _snapshot.Tree.Specializations.Count;
             specializationIndex++)
        {
            var specialization = _snapshot.Tree.Specializations[specializationIndex];
            foreach (var node in specialization.Skills)
            {
                if (_prototypes.TryIndex<Wh40kClassSkillPrototype>(node.SkillId, out var prototype))
                {
                    var icon = _sprites.Frame0(Wh40kClassSkillIconPaths.GetSpecifier(prototype));
                    _nodes.Add(node.SkillId, new NodeView(
                        specialization.SpecializationId,
                        specializationIndex,
                        prototype,
                        node.State,
                        icon));
                }
            }
        }
    }

    private void DrawImperialBackdrop(DrawingHandleScreen handle)
    {
        var scale = UIScale;
        var outerMargin = 8f * scale;
        var innerMargin = 14f * scale;
        var outer = UIBox2.FromDimensions(
            new Vector2(outerMargin),
            PixelSize - new Vector2(outerMargin * 2f));
        var inner = UIBox2.FromDimensions(
            new Vector2(innerMargin),
            PixelSize - new Vector2(innerMargin * 2f));
        var trim = Copper.WithAlpha(0.22f);

        DrawBoxOutline(handle, outer, trim);
        DrawBoxOutline(handle, inner, Copper.WithAlpha(0.08f));
        DrawCornerBrackets(handle, inner, trim, 38f * scale);

        var rivet = Copper.WithAlpha(0.32f);
        foreach (var position in new[]
                 {
                     outer.TopLeft,
                     outer.TopRight,
                     outer.BottomLeft,
                     outer.BottomRight,
                 })
        {
            handle.DrawCircle(position, 1.6f * scale, rivet);
        }

        DrawCogSeal(handle, PixelSize / 2f, MathF.Min(PixelSize.X, PixelSize.Y) * 0.26f);
    }

    private void DrawCornerBrackets(DrawingHandleScreen handle, UIBox2 box, Color color, float length)
    {
        handle.DrawLine(box.TopLeft, box.TopLeft + new Vector2(length, 0f), color);
        handle.DrawLine(box.TopLeft, box.TopLeft + new Vector2(0f, length), color);
        handle.DrawLine(box.TopRight, box.TopRight - new Vector2(length, 0f), color);
        handle.DrawLine(box.TopRight, box.TopRight + new Vector2(0f, length), color);
        handle.DrawLine(box.BottomLeft, box.BottomLeft + new Vector2(length, 0f), color);
        handle.DrawLine(box.BottomLeft, box.BottomLeft - new Vector2(0f, length), color);
        handle.DrawLine(box.BottomRight, box.BottomRight - new Vector2(length, 0f), color);
        handle.DrawLine(box.BottomRight, box.BottomRight - new Vector2(0f, length), color);
    }

    private void DrawCogSeal(DrawingHandleScreen handle, Vector2 center, float radius)
    {
        if (radius <= 0f)
            return;

        var ink = Copper.WithAlpha(0.075f);
        handle.DrawCircle(center, radius, ink, filled: false);
        handle.DrawCircle(center, radius * 0.72f, ink, filled: false);
        handle.DrawCircle(center, radius * 0.22f, ink, filled: false);

        const int spokes = 12;
        for (var index = 0; index < spokes; index++)
        {
            var angle = MathF.Tau * index / spokes;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + direction * radius * 0.28f, center + direction * radius * 0.67f, ink);
            handle.DrawCircle(center + direction * radius * 0.86f, 2f * UIScale, ink);
        }
    }

    private void DrawRootConnections(DrawingHandleScreen handle)
    {
        var root = GraphToScreen(Vector2.Zero);
        foreach (var node in _nodes.Values.Where(node => node.Prototype.Order == 1))
        {
            var branchColor = node.SpecializationIndex == 0 ? BranchLeft : BranchRight;
            var color = node.State == Wh40kClassSkillNodeState.Purchased
                ? Gold
                : node.State == Wh40kClassSkillNodeState.Available
                    ? Points.WithAlpha(0.82f)
                : branchColor.WithAlpha(0.62f);
            DrawBranchConnection(handle, root, GraphToScreen(node.Prototype.DisplayPosition), color);
        }
    }

    private void DrawClassRoot(DrawingHandleScreen handle)
    {
        var center = GraphToScreen(Vector2.Zero);
        var radius = RootRadius * UIScale * MathF.Sqrt(_zoom);
        handle.DrawCircle(center, radius + 9f * UIScale, Copper.WithAlpha(0.11f));
        handle.DrawCircle(center, radius + 4f * UIScale, Color.Black.WithAlpha(0.98f));
        handle.DrawCircle(center, radius + 3f * UIScale, Gold.WithAlpha(0.85f), filled: false);
        handle.DrawCircle(center, radius, Copper.WithAlpha(0.42f));
        handle.DrawCircle(center, radius, Gold, filled: false);
        var iconRadius = radius * 0.68f;
        handle.DrawTextureRect(
            _rootIcon,
            UIBox2.FromDimensions(center - new Vector2(iconRadius), new Vector2(iconRadius * 2f)),
            Gold);

        var number = "00";
        var textScale = 0.68f * UIScale;
        var size = handle.GetDimensions(_font, number, textScale);
        handle.DrawString(
            _font,
            center + new Vector2(-size.X / 2f, radius + 7f * UIScale),
            number,
            textScale,
            Gold);
    }

    private void DrawSpecializationLabels(DrawingHandleScreen handle)
    {
        foreach (var root in _nodes.Values.Where(node => node.Prototype.Order == 1))
        {
            if (!_prototypes.TryIndex<Wh40kClassSpecializationPrototype>(root.SpecializationId, out var specialization))
                continue;

            var anchor = GraphToScreen(new Vector2(root.SpecializationIndex == 0 ? -4f : 4f, -6.15f));
            var text = Loc.GetString(specialization.Name).ToUpperInvariant();
            var scale = 0.72f * UIScale;
            var size = handle.GetDimensions(_font, text, scale);
            var color = root.SpecializationIndex == 0 ? BranchLeft : BranchRight;
            handle.DrawRect(
                UIBox2.FromDimensions(
                    anchor - new Vector2(size.X / 2f + 8f * UIScale, 4f * UIScale),
                    size + new Vector2(16f * UIScale, 8f * UIScale)),
                Color.Black.WithAlpha(0.74f));
            handle.DrawString(_font, anchor - new Vector2(size.X / 2f, 0f), text, scale, color);
        }
    }

    private void DrawBranchConnection(DrawingHandleScreen handle, Vector2 from, Vector2 to, Color color)
    {
        var middleX = (from.X + to.X) / 2f;
        var firstCorner = new Vector2(middleX, from.Y);
        var secondCorner = new Vector2(middleX, to.Y);
        handle.DrawLine(from, firstCorner, color);
        handle.DrawLine(firstCorner, secondCorner, color);
        handle.DrawLine(secondCorner, to, color);
        if (MathF.Abs(from.Y - to.Y) > 1f)
            handle.DrawCircle(firstCorner, 1.8f * UIScale, color);
    }

    private void FitTreeToViewport()
    {
        if (_nodes.Count == 0)
            return;

        var minimumX = MathF.Min(0f, _nodes.Values.Min(node => node.Prototype.DisplayPosition.X));
        var maximumX = MathF.Max(0f, _nodes.Values.Max(node => node.Prototype.DisplayPosition.X));
        var minimumY = MathF.Min(0f, _nodes.Values.Min(node => node.Prototype.DisplayPosition.Y));
        var maximumY = MathF.Max(0f, _nodes.Values.Max(node => node.Prototype.DisplayPosition.Y));
        var width = MathF.Max(1f, (maximumX - minimumX) * HorizontalSpacing);
        var height = MathF.Max(1f, (maximumY - minimumY) * VerticalSpacing);
        var availableWidth = MathF.Max(1f, PixelSize.X - 92f * UIScale);
        var availableHeight = MathF.Max(1f, PixelSize.Y - 112f * UIScale);
        var fit = MathF.Min(
            availableWidth / (width * UIScale),
            availableHeight / (height * UIScale));
        _zoom = Math.Clamp(fit, MinimumZoom, MathF.Min(MaximumZoom, 0.78f));

        var center = new Vector2((minimumX + maximumX) / 2f, (minimumY + maximumY) / 2f);
        _pan = -new Vector2(
            center.X * HorizontalSpacing * UIScale * _zoom,
            center.Y * VerticalSpacing * UIScale * _zoom);
        _fitPending = false;
    }

    private void DrawNode(DrawingHandleScreen handle, NodeView node)
    {
        var center = GraphToScreen(node.Prototype.DisplayPosition);
        var radius = (node.Prototype.Order >= GetSpecializationMaxOrder(node.SpecializationIndex) - 1
            ? NodeRadius + 5f
            : NodeRadius) * UIScale * MathF.Sqrt(_zoom);
        var stateColor = GetStateColor(node.State);
        var branchColor = node.SpecializationIndex == 0 ? BranchLeft : BranchRight;
        var highlighted = IsSearchMatch(node);
        var selected = node.Prototype.ID == _selectedSkillId;
        var focused = HasKeyboardFocus() && node.Prototype.ID == _focusedSkillId;
        var hovered = node.Prototype.ID == _hoveredSkillId;

        if (node.Prototype.ID == _confirmedSkillId && _confirmationGlow > 0f)
            handle.DrawCircle(center, radius + (4f + _confirmationGlow * 8f) * UIScale, Gold.WithAlpha(_confirmationGlow * 0.28f));
        if (highlighted)
            handle.DrawCircle(center, radius + 8f * UIScale, Points.WithAlpha(0.22f));
        if (hovered)
            handle.DrawCircle(center, radius + 5f * UIScale, Color.White.WithAlpha(0.12f));

        handle.DrawCircle(center, radius + 3f * UIScale, Color.Black.WithAlpha(0.96f));
        handle.DrawCircle(center, radius + 2f * UIScale, branchColor, filled: false);
        handle.DrawCircle(center, radius, stateColor.WithAlpha(node.State == Wh40kClassSkillNodeState.Purchased ? 0.88f : 0.28f));
        handle.DrawCircle(center, radius, stateColor, filled: false);

        var iconRadius = radius * 0.62f;
        var iconColor = node.State switch
        {
            Wh40kClassSkillNodeState.Purchased or Wh40kClassSkillNodeState.Available =>
                stateColor.WithAlpha(0.94f),
            Wh40kClassSkillNodeState.ContentUnavailable => stateColor.WithAlpha(0.32f),
            _ => branchColor.WithAlpha(0.72f),
        };
        handle.DrawTextureRect(
            node.Icon,
            UIBox2.FromDimensions(center - new Vector2(iconRadius), new Vector2(iconRadius * 2f)),
            iconColor);

        if (node.Prototype.Kind == Wh40kClassSkillKind.Active)
        {
            var marker = radius * 0.42f;
            handle.DrawRect(
                UIBox2.FromDimensions(
                    center + new Vector2(radius - marker, radius - marker),
                    new Vector2(marker)),
                stateColor);
        }

        if (selected)
            handle.DrawCircle(
                center,
                radius + 6f * UIScale,
                node.State == Wh40kClassSkillNodeState.Purchased ? Gold : Selection,
                filled: false);
        else if (focused)
            handle.DrawCircle(center, radius + 4f * UIScale, Selection.WithAlpha(0.52f), filled: false);

        var number = node.Prototype.Order.ToString("00");
        var textScale = 0.56f * UIScale;
        var size = handle.GetDimensions(_font, number, textScale);
        handle.DrawString(
            _font,
            center + new Vector2(-size.X / 2f, radius + 3f * UIScale),
            number,
            textScale,
            stateColor.WithAlpha(0.94f));
    }

    private void Navigate(int horizontal, int vertical)
    {
        var current = _focusedSkillId != null && _nodes.TryGetValue(_focusedSkillId, out var found)
            ? found
            : _nodes.Values.OrderBy(node => node.SpecializationIndex).ThenBy(node => node.Prototype.Order).First();
        NodeView? next;
        if (vertical != 0)
        {
            next = _nodes.Values.FirstOrDefault(node =>
                node.SpecializationIndex == current.SpecializationIndex &&
                node.Prototype.Order == Math.Clamp(
                    current.Prototype.Order + vertical,
                    1,
                    GetSpecializationMaxOrder(current.SpecializationIndex)));
        }
        else
        {
            var targetSpecialization = Math.Clamp(
                current.SpecializationIndex + horizontal,
                0,
                Math.Max(0, (_snapshot?.Tree.Specializations.Count ?? 1) - 1));
            next = _nodes.Values.FirstOrDefault(node =>
                node.SpecializationIndex == targetSpecialization &&
                node.Prototype.Order == current.Prototype.Order);
        }

        if (next == null || next.Prototype.ID == current.Prototype.ID)
            return;

        FocusSkill(next.Prototype.ID, false);
    }

    private void ActivateFocused()
    {
        var skillId = _focusedSkillId ?? GetCurrentSkillId();
        if (skillId == null)
            return;

        if (_selectedSkillId == skillId)
        {
            NodeActivated?.Invoke(skillId);
            return;
        }

        SelectNode(skillId);
    }

    private void SelectNode(string skillId)
    {
        _focusedSkillId = skillId;
        _selectedSkillId = skillId;
        NodeSelected?.Invoke(skillId);
    }

    private void FocusSkill(string skillId, bool select)
    {
        if (!_nodes.TryGetValue(skillId, out var node))
            return;

        _fitPending = false;
        _focusedSkillId = skillId;
        if (select)
        {
            _selectedSkillId = skillId;
            NodeSelected?.Invoke(skillId);
        }

        KeepFocusedNodeVisible(skillId);
        GrabKeyboardFocus();
    }

    private void KeepFocusedNodeVisible(string skillId)
    {
        if (!_nodes.TryGetValue(skillId, out var node))
            return;

        var point = GraphToScreen(node.Prototype.DisplayPosition);
        var margin = 64f * UIScale;
        if (point.X < margin)
            _pan.X += margin - point.X;
        else if (point.X > PixelSize.X - margin)
            _pan.X -= point.X - (PixelSize.X - margin);
        if (point.Y < margin)
            _pan.Y += margin - point.Y;
        else if (point.Y > PixelSize.Y - margin)
            _pan.Y -= point.Y - (PixelSize.Y - margin);
    }

    private string? GetCurrentSkillId()
    {
        return _nodes.Values
            .OrderByDescending(node => node.State == Wh40kClassSkillNodeState.Purchased)
            .ThenBy(node => node.State == Wh40kClassSkillNodeState.Purchased ? -node.Prototype.Order : node.Prototype.Order)
            .ThenBy(node => node.SpecializationIndex)
            .Select(node => node.Prototype.ID)
            .FirstOrDefault();
    }

    private NodeView? FindNode(Vector2 position)
    {
        return _nodes.Values
            .OrderByDescending(node => node.Prototype.Order == GetSpecializationMaxOrder(node.SpecializationIndex))
            .FirstOrDefault(node =>
            {
                var radius = (node.Prototype.Order >= GetSpecializationMaxOrder(node.SpecializationIndex) - 1
                    ? NodeRadius + 5f
                    : NodeRadius) * UIScale * MathF.Sqrt(_zoom);
                return Vector2.DistanceSquared(GraphToScreen(node.Prototype.DisplayPosition), position) <= radius * radius;
            });
    }

    private int GetSpecializationMaxOrder(int specializationIndex)
    {
        return _nodes.Values
            .Where(node => node.SpecializationIndex == specializationIndex)
            .Select(node => node.Prototype.Order)
            .DefaultIfEmpty(1)
            .Max();
    }

    private bool IsSearchMatch(NodeView node)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
            return false;

        return Loc.GetString(node.Prototype.Name).Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase) ||
               Loc.GetString(node.Prototype.Description).Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase);
    }

    private Vector2 GraphToScreen(Vector2 point)
    {
        var scale = UIScale * _zoom;
        return PixelSize / 2f + _pan + new Vector2(
            point.X * HorizontalSpacing * scale,
            point.Y * VerticalSpacing * scale);
    }

    private Vector2 ScreenToGraph(Vector2 point)
    {
        var scale = UIScale * _zoom;
        var relative = point - PixelSize / 2f - _pan;
        return new Vector2(
            relative.X / (HorizontalSpacing * scale),
            relative.Y / (VerticalSpacing * scale));
    }

    private static Color GetStateColor(Wh40kClassSkillNodeState state)
    {
        return state switch
        {
            Wh40kClassSkillNodeState.Purchased => Gold,
            Wh40kClassSkillNodeState.Available => Points,
            Wh40kClassSkillNodeState.InsufficientLevel => LevelBlocked,
            Wh40kClassSkillNodeState.MissingPrerequisite => PrerequisiteBlocked,
            Wh40kClassSkillNodeState.InsufficientPoints => Copper,
            _ => Muted,
        };
    }

    internal static string GetStateLocId(Wh40kClassSkillNodeState state)
    {
        return state switch
        {
            Wh40kClassSkillNodeState.Purchased => "wh40k-class-ui-state-purchased",
            Wh40kClassSkillNodeState.Available => "wh40k-class-ui-state-available",
            Wh40kClassSkillNodeState.ContentUnavailable => "wh40k-class-ui-state-content-unavailable",
            Wh40kClassSkillNodeState.InsufficientLevel => "wh40k-class-ui-state-insufficient-level",
            Wh40kClassSkillNodeState.MissingPrerequisite => "wh40k-class-ui-state-missing-prerequisite",
            Wh40kClassSkillNodeState.InsufficientPoints => "wh40k-class-ui-state-insufficient-points",
            _ => "wh40k-class-ui-state-content-unavailable",
        };
    }

    internal static string GetKindLocId(Wh40kClassSkillKind kind)
    {
        return kind == Wh40kClassSkillKind.Active
            ? "wh40k-class-ui-kind-active"
            : "wh40k-class-ui-kind-passive";
    }

    private static void DrawBoxOutline(DrawingHandleScreen handle, UIBox2 box, Color color)
    {
        handle.DrawLine(box.TopLeft, box.TopRight, color);
        handle.DrawLine(box.TopRight, box.BottomRight, color);
        handle.DrawLine(box.BottomRight, box.BottomLeft, color);
        handle.DrawLine(box.BottomLeft, box.TopLeft, color);
    }

    private void DrawCentered(DrawingHandleScreen handle, string text, Color color)
    {
        var scale = 0.8f * UIScale;
        var size = handle.GetDimensions(_font, text, scale);
        handle.DrawString(_font, (PixelSize - size) / 2f, text, scale, color);
    }

    private sealed record NodeView(
        string SpecializationId,
        int SpecializationIndex,
        Wh40kClassSkillPrototype Prototype,
        Wh40kClassSkillNodeState State,
        Texture Icon);
}
