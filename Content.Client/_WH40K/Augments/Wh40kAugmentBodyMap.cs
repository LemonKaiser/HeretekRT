using System.Numerics;
using Content.Client._WH40K.CharacterCreation;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Augments;

public sealed class Wh40kAugmentBodyMap : Container
{
    private static readonly Color Background = Color.FromHex("#1C1C1C");
    private static readonly Color Grid = Color.FromHex("#303030");
    private static readonly Color Ring = Color.FromHex("#656565");
    private static readonly Color Mannequin = Color.FromHex("#A5A5A5");

    private readonly RSI _human;
    private readonly RSI _augments;
    private readonly (Zone Zone, Button Button)[] _nodes;
    private Zone? _selected;
    private string _selectedState = string.Empty;
    private RsiDirection _facing = RsiDirection.South;

    public event Action<string>? ZoneSelected;
    public bool StackLabels { get; set; }

    public RsiDirection Facing
    {
        get => _facing;
        set
        {
            _facing = value;
            InvalidateArrange();
        }
    }

    private bool SelectedZoneOccluded => _selected is { } zone &&
        ((zone.IsEye && Facing == RsiDirection.North) ||
         (zone.Side is { } side && Facing is (RsiDirection.East or RsiDirection.West) && side != Facing));

    public Wh40kAugmentBodyMap()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _human = cache.GetResource<RSIResource>(new ResPath("/Textures/Mobs/Species/Human/parts.rsi")).RSI;
        _augments = cache.GetResource<RSIResource>(new ResPath(Wh40kAugmentCatalogue.SpritePath)).RSI;
        RectClipContent = true;

        Zone[] zones =
        [
            new("head", false, .14f, new(16, 4.5f)),
            new("right-eye", false, .28f, new(14, 6.5f), RsiDirection.East, IsEye: true),
            new("left-eye", true, .28f, new(17, 6.5f), RsiDirection.West, IsEye: true),
            new("right-arm", false, .48f, new(9, 16), RsiDirection.East, "r_arm", "r_hand", "hand"),
            new("left-arm", true, .48f, new(22, 16), RsiDirection.West, "l_arm", "l_hand", "hand"),
            new("core", true, .67f, new(16, 16)),
            new("right-leg", false, .82f, new(13, 26), RsiDirection.East, "r_leg", "r_foot", "foot"),
            new("left-leg", true, .82f, new(18, 26), RsiDirection.West, "l_leg", "l_foot", "foot"),
        ];

        _nodes = new (Zone, Button)[zones.Length];
        var labelFont = Wh40kOnboardingTypography.Technical(cache, 12);
        for (var i = 0; i < zones.Length; i++)
        {
            var zone = zones[i];
            var button = new Button
            {
                Text = Loc.GetString("wh40k-augments-zone-" + zone.Id),
                ToolTip = Loc.GetString("wh40k-augments-zone-" + zone.Id),
            };
            button.Label.FontOverride = labelFont;
            button.OnPressed += _ => ZoneSelected?.Invoke(zone.Id);
            _nodes[i] = (zone, button);
            AddChild(button);
        }
    }

    public void Select(string zone, string state)
    {
        _selected = Array.Find(_nodes, node => node.Zone.Id == zone).Zone;
        _selectedState = state;
        foreach (var (node, button) in _nodes)
        {
            var selected = node == _selected;
            button.StyleBoxOverride = Wh40kAugmentStyle.Card(4, selected);
            button.Label.FontColorOverride = selected ? Wh40kAugmentStyle.SelectedText : Wh40kAugmentStyle.Text;
        }
    }

    private bool OnRight(Zone zone) => !StackLabels && (zone.OnRight ^ (zone.Side.HasValue && Facing != RsiDirection.South));

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        foreach (var (_, button) in _nodes)
            button.Measure(availableSize);

        return new Vector2(300, StackLabels ? 320 : 375);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        for (var index = 0; index < _nodes.Length; index++)
        {
            var (zone, button) = _nodes[index];
            var width = MathF.Max(62, button.DesiredSize.X + 8);
            const float height = 30;
            var left = OnRight(zone) ? finalSize.X - width - 12 : 12;
            var top = StackLabels
                ? 12 + index * (finalSize.Y - height - 24) / (_nodes.Length - 1)
                : finalSize.Y * zone.Y - height / 2;
            button.Arrange(UIBox2.FromDimensions(new Vector2(left, top), new Vector2(width, height)));
        }

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = PixelSize;
        handle.DrawRect(PixelSizeBox, Background);
        var spacing = MathF.Max(1, 24 * UIScale);
        for (var x = size.X % spacing / 2; x < size.X; x += spacing)
            handle.DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), Grid);
        for (var y = size.Y % spacing / 2; y < size.Y; y += spacing)
            handle.DrawLine(new Vector2(0, y), new Vector2(size.X, y), Grid);

        var center = new Vector2(size.X, size.Y) / 2f;
        var labelWidth = 0f;
        foreach (var (_, nodeButton) in _nodes)
            labelWidth = MathF.Max(labelWidth, nodeButton.PixelSize.X);
        float spriteSize;
        if (StackLabels)
        {
            var labelRail = labelWidth + 27 * UIScale;
            center = new Vector2((size.X + labelRail) * .5f, size.Y * .46f);
            spriteSize = MathF.Min(size.Y * .81f, (size.X - labelRail - 12 * UIScale) * 32 / 20);
        }
        else
        {
            spriteSize = MathF.Min(MathF.Min(size.X * .69f, size.Y * .85f),
                MathF.Max(32, (size.X - 2 * (labelWidth + 24 * UIScale)) * 32 / 18));
        }

        spriteSize = MathF.Max(1, spriteSize);
        var offset = center - new Vector2(spriteSize / 2);
        var spriteRect = UIBox2.FromDimensions(offset, new Vector2(spriteSize));
        handle.DrawCircle(center, spriteSize * .47f, Ring.WithAlpha(.55f), filled: false);
        handle.DrawCircle(center, spriteSize * .40f, Ring.WithAlpha(.45f), filled: false);
        handle.DrawLine(new Vector2(center.X, 10), new Vector2(center.X, size.Y - 10), Ring.WithAlpha(.40f));
        handle.DrawRect(new UIBox2(6, 6, size.X - 6, size.Y - 6), Ring.WithAlpha(.45f), filled: false);
        var inset = 6 * UIScale;
        var topLeft = new Vector2(inset);
        var bottomRight = size - topLeft;
        var tick = 18 * UIScale;
        handle.DrawLine(topLeft, topLeft + new Vector2(tick, 0), Ring);
        handle.DrawLine(topLeft, topLeft + new Vector2(0, tick), Ring);
        handle.DrawLine(bottomRight, bottomRight - new Vector2(tick, 0), Ring);
        handle.DrawLine(bottomRight, bottomRight - new Vector2(0, tick), Ring);

        DrawMannequin(handle, spriteRect);
        if (_selected is not { } selected || SelectedZoneOccluded)
            return;

        var marker = _selectedState == "headimplants" ? new Vector2(16, 4.5f) : selected.Marker;
        if (Facing == RsiDirection.North && selected.Side.HasValue)
            marker.X = 31 - marker.X;
        else if (Facing is RsiDirection.East or RsiDirection.West)
        {
            if (selected.IsEye)
                marker.X = Facing == RsiDirection.East ? 18 : 14;
            else if (selected.Side.HasValue)
                marker.X = selected.ChildState == "hand" ? Facing == RsiDirection.East ? 14 : 18 : 16;
        }

        var point = offset + marker / 32 * spriteSize;
        var button = Array.Find(_nodes, node => node.Zone == selected).Button;
        var right = OnRight(selected);
        var start = button.PixelPosition + new Vector2(right ? 0 : button.PixelSize.X, button.PixelSize.Y / 2);
        var half = 5 * UIScale;
        var end = point + new Vector2(right ? half : -half, 0);
        var bend = new Vector2(start.X + (end.X - start.X) * .45f, start.Y);
        var shoulder = new Vector2(end.X - (end.X - bend.X) * .25f, end.Y);
        var markerColor = Wh40kAugmentStyle.SelectedText;
        handle.DrawLine(start, bend, markerColor);
        handle.DrawLine(bend, shoulder, markerColor);
        handle.DrawLine(shoulder, end, markerColor);
        handle.DrawRect(UIBox2.FromDimensions(point - new Vector2(half), new Vector2(2 * half)), markerColor, filled: false);
    }

    private void DrawMannequin(DrawingHandleScreen handle, UIBox2 rect)
    {
        if (Facing == RsiDirection.East)
            DrawLimbs(handle, rect, "l", legs: true, arms: true);
        else if (Facing == RsiDirection.West)
            DrawLimbs(handle, rect, "r", legs: true, arms: true);
        else
        {
            DrawLimbs(handle, rect, "r", legs: true, arms: false);
            DrawLimbs(handle, rect, "l", legs: true, arms: false);
        }

        DrawLayer(handle, rect, "torso_m");
        if (_selectedState is not ("ironscull" or "sikariiscull"))
            DrawLayer(handle, rect, "head_m");

        if (_selected?.Limb is null && _augments.TryGetState(_selectedState, out var overlay))
            handle.DrawTextureRect(overlay.GetFrame(Facing, 0), rect, Color.White);

        if (Facing == RsiDirection.East)
            DrawLimbs(handle, rect, "r", legs: true, arms: true);
        else if (Facing == RsiDirection.West)
            DrawLimbs(handle, rect, "l", legs: true, arms: true);
        else
        {
            DrawLimbs(handle, rect, "r", legs: false, arms: true);
            DrawLimbs(handle, rect, "l", legs: false, arms: true);
        }
    }

    private void DrawLimbs(DrawingHandleScreen handle, UIBox2 rect, string side, bool legs, bool arms)
    {
        if (legs)
        {
            DrawLayer(handle, rect, side + "_leg");
            DrawLayer(handle, rect, side + "_foot");
        }

        if (arms)
        {
            DrawLayer(handle, rect, side + "_arm");
            DrawLayer(handle, rect, side + "_hand");
        }
    }

    private void DrawLayer(DrawingHandleScreen handle, UIBox2 rect, string layer)
    {
        if (_selected is { } selected)
        {
            if (layer == selected.Limb && _augments.TryGetState(_selectedState + "-part", out var limb))
            {
                handle.DrawTextureRect(limb.GetFrame(Facing, 0), rect, Color.White);
                return;
            }

            if (layer == selected.Child && _augments.TryGetState(_selectedState + "-" + selected.ChildState, out var child))
            {
                handle.DrawTextureRect(child.GetFrame(Facing, 0), rect, Color.White);
                return;
            }
        }

        handle.DrawTextureRect(_human[layer].GetFrame(Facing, 0), rect, Mannequin);
    }

    private sealed record Zone(
        string Id,
        bool OnRight,
        float Y,
        Vector2 Marker,
        RsiDirection? Side = null,
        string? Limb = null,
        string? Child = null,
        string? ChildState = null,
        bool IsEye = false);
}
