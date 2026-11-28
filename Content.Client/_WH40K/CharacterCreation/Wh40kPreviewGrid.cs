using System.Numerics;
using Content.Client.Clickable;
using Content.Shared.CCVar;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Utility;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.CharacterCreation;

public sealed class Wh40kPreviewGrid : GridContainer
{
    private const float TileWidth = 94;
    private const int Gap = 4;
    private ButtonGroup _group = new();
    private readonly List<SpriteThumbnail> _markingIcons = new();
    private readonly Dictionary<Texture, UIBox2?> _frameBounds = new();
    private readonly IConfigurationManager _config = IoCManager.Resolve<IConfigurationManager>();
    private Direction _direction = Direction.South;
    private float _arrangedWidth;
    public bool LargeTiles { get; set; }
    private float CellWidth => LargeTiles ? 134 : TileWidth;

    public Wh40kPreviewGrid()
    {
        HSeparationOverride = Gap;
        VSeparationOverride = Gap;
    }

    public void ClearTiles()
    {
        RemoveAllChildren();
        _markingIcons.Clear();
        _group = new ButtonGroup();
    }

    public ContainerButton AddTile(string title, Texture? icon, bool selected, Action select)
    {
        return AddTile(title, new TextureRect
        {
            Texture = icon,
            SetHeight = 42,
            CanShrink = true,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MouseFilter = MouseFilterMode.Ignore,
        }, selected, select);
    }

    public ContainerButton AddBubbleTile(string title, RSI.State? state, Action select)
    {
        if (state == null)
        {
            return AddTile(title, (Texture?) null, false, select);
        }

        var icon = new SpriteThumbnail(this, [state], closeUp: true);
        icon.SetDirection(Direction.South);
        icon.Animate(state);
        return AddTile(title, icon, false, select);
    }

    public void AddMarkingTile(string title, IReadOnlyList<SpriteSpecifier> layers, bool selected, Action select, Color tint,
        bool closeUp = false)
    {
        var icon = CreateMarkingPreview(layers, closeUp, 48);
        icon.SetTint(tint);
        _markingIcons.Add(icon);
        AddTile(title, icon, selected, select);
    }

    internal SpriteThumbnail CreateMarkingPreview(IReadOnlyList<SpriteSpecifier> layers, bool closeUp, float height)
    {
        var sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();
        var frames = new IDirectionalTextureProvider[layers.Count];
        for (var i = 0; i < layers.Count; i++)
            frames[i] = sprites.RsiStateLike(layers[i]);

        var icon = new SpriteThumbnail(this, frames, closeUp) { SetHeight = height };
        icon.SetDirection(_direction);
        return icon;
    }

    internal SpriteThumbnail AddGalleryTile(string title, IReadOnlyList<SpriteSpecifier> layers, IReadOnlyList<Color>? colors,
        bool selected, bool applied, Action select)
    {
        var icon = CreateMarkingPreview(layers, true, 59);
        icon.SetColors(colors);
        _markingIcons.Add(icon);
        var stage = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat(Color.FromHex("#2C2319")),
            MouseFilter = MouseFilterMode.Ignore,
            Children = { icon },
        };
        var tile = AddTile(title, stage, selected, select);
        if (applied)
            tile.AddChild(new Label
            {
                Text = "✓", HorizontalAlignment = HAlignment.Right, VerticalAlignment = VAlignment.Top,
                Margin = new Thickness(0, 4, 5, 0), FontColorOverride = Color.FromHex("#E1C17E"),
                MouseFilter = MouseFilterMode.Ignore,
            });
        return icon;
    }

    public void SetMarkingTint(Color tint)
    {
        foreach (var icon in _markingIcons)
            icon.SetTint(tint);
    }

    public void SetDirection(Direction direction)
    {
        if (_direction == direction)
            return;

        _direction = direction;
        foreach (var icon in _markingIcons)
            icon.SetDirection(direction);
    }

    private ContainerButton AddTile(string title, Control icon, bool selected, Action select)
    {
        Control caption;
        if (LargeTiles)
        {
            var text = new RichTextLabel { MouseFilter = MouseFilterMode.Ignore, RectClipContent = true };
            text.SetMessage(FormattedMessage.FromUnformatted(title));
            caption = text;
        }
        else
        {
            caption = new Label { Text = title, ClipText = true, StyleClasses = { "LabelSmall" }, MouseFilter = MouseFilterMode.Ignore };
        }

        var button = new ContainerButton
        {
            SetSize = new Vector2(CellWidth, LargeTiles ? 118 : 82),
            ToggleMode = true,
            Group = _group,
            Pressed = selected,
            ToolTip = title,
            StyleClasses = { ContainerButton.StyleClassButton },
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = LargeTiles ? 4 : 0,
                    MouseFilter = MouseFilterMode.Ignore,
                    Children =
                    {
                        icon,
                        caption,
                    },
                },
            },
        };
        button.OnPressed += _ => select();
        AddChild(button);
        return button;
    }

    public void SetDisabled(bool disabled)
    {
        foreach (var child in Children)
            ((ContainerButton) child).Disabled = disabled;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = _arrangedWidth > 0 ? Math.Min(availableSize.X, _arrangedWidth) : availableSize.X;
        UpdateColumns(width);
        return base.MeasureOverride(availableSize);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        // A scroll container can leave less room at arrange time once the scrollbar is visible.
        _arrangedWidth = finalSize.X;
        UpdateColumns(finalSize.X);
        return base.ArrangeOverride(finalSize);
    }

    private void UpdateColumns(float width)
    {
        if (!float.IsFinite(width))
            return;

        var columns = Math.Clamp((int) ((width + Gap) / (CellWidth + Gap)), 1, Math.Max(1, ChildCount));
        if (Columns != columns)
            Columns = columns;
    }

    private UIBox2? FrameBounds(IDirectionalTextureProvider sprite, Direction direction, Texture texture, int frame = 0)
    {
        var halfSize = (Vector2) texture.Size / 2;
        if (sprite is not RSI.State state)
            return new UIBox2(-halfSize, halfSize);

        if (_frameBounds.TryGetValue(texture, out var cached))
            return cached;

        // The click map already has the frame's alpha mask in CPU memory.
        var clickMap = IoCManager.Resolve<IClickMapManager>();
        var rsiDirection = direction.Convert(state.RsiDirections);
        var min = texture.Size;
        var max = Vector2i.Zero;
        for (var y = 0; y < texture.Height; y++)
        {
            for (var x = 0; x < texture.Width; x++)
            {
                if (!clickMap.IsOccluding(state.RSI, state.StateId, rsiDirection, frame, new Vector2i(x, y), precise: true))
                    continue;

                min = new Vector2i(Math.Min(min.X, x), Math.Min(min.Y, y));
                max = new Vector2i(Math.Max(max.X, x + 1), Math.Max(max.Y, y + 1));
            }
        }

        UIBox2? bounds = min.X < max.X ? new UIBox2((Vector2) min - halfSize, (Vector2) max - halfSize) : null;
        _frameBounds.Add(texture, bounds);
        return bounds;
    }

    internal sealed class SpriteThumbnail : Control
    {
        private readonly Wh40kPreviewGrid _grid;
        private readonly IDirectionalTextureProvider[] _sprites;
        private readonly TextureRect[] _layers;
        private readonly bool _closeUp;
        private UIBox2? _visibleBounds;
        private Texture[]? _animation;
        private float[]? _frameDelays;
        private int _frame;
        private float _frameTime;
        private float _duration;

        public SpriteThumbnail(Wh40kPreviewGrid grid, IDirectionalTextureProvider[] sprites, bool closeUp)
        {
            _grid = grid;
            _sprites = sprites;
            _closeUp = closeUp;
            SetHeight = 48;
            MouseFilter = MouseFilterMode.Ignore;
            RectClipContent = closeUp;
            _layers = new TextureRect[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                _layers[i] = new TextureRect
                {
                    CanShrink = true,
                    Stretch = closeUp ? TextureRect.StretchMode.Scale : TextureRect.StretchMode.KeepAspectCentered,
                    MouseFilter = MouseFilterMode.Ignore,
                };
                AddChild(_layers[i]);
            }
        }

        public void SetDirection(Direction direction)
        {
            SetFrames(direction);
            if (_grid.LargeTiles && _visibleBounds == null)
            {
                foreach (var fallback in new[] { Direction.South, Direction.East, Direction.West, Direction.North })
                {
                    if (fallback == direction)
                        continue;
                    SetFrames(fallback);
                    if (_visibleBounds != null)
                        break;
                }
            }

            if (_closeUp)
                InvalidateArrange();
        }

        private void SetFrames(Direction direction)
        {
            _visibleBounds = null;
            for (var i = 0; i < _layers.Length; i++)
            {
                var texture = _sprites[i].TextureFor(direction);
                _layers[i].Texture = texture;
                if (!_closeUp || _grid.FrameBounds(_sprites[i], direction, texture) is not { } bounds)
                    continue;

                IncludeBounds(bounds);
            }
        }

        public void Animate(RSI.State state)
        {
            var frames = state.GetFrames(Direction.South.Convert(state.RsiDirections));
            if (frames.Length <= 1)
                return;

            _animation = frames;
            _frameDelays = state.GetDelays();
            for (var i = 0; i < frames.Length; i++)
            {
                _duration += _frameDelays[i];
                if (_grid.FrameBounds(state, Direction.South, frames[i], i) is { } bounds)
                    IncludeBounds(bounds);
            }
        }

        private void IncludeBounds(UIBox2 bounds)
        {
            _visibleBounds = _visibleBounds is { } previous
                ? new UIBox2(Vector2.Min(previous.TopLeft, bounds.TopLeft), Vector2.Max(previous.BottomRight, bounds.BottomRight))
                : bounds;
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            if (_animation == null || _frameDelays == null || _duration <= 0 || !VisibleInTree ||
                _grid._config.GetCVar(CCVars.ReducedMotion))
                return;

            _frameTime += args.DeltaSeconds % _duration;
            var oldFrame = _frame;
            while (_frameTime >= _frameDelays[_frame])
            {
                _frameTime -= _frameDelays[_frame];
                _frame = (_frame + 1) % _animation.Length;
            }

            if (oldFrame != _frame)
                _layers[0].Texture = _animation[_frame];
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            if (!_closeUp)
                return base.ArrangeOverride(finalSize);

            var bounds = _visibleBounds ?? new UIBox2(-16, -16, 16, 16);
            var room = Vector2.Max(finalSize - new Vector2(8), Vector2.Zero);
            var scale = MathF.Min(4, MathF.Min(room.X / bounds.Width, room.Y / bounds.Height));
            var origin = finalSize / 2 - bounds.Center * scale;
            foreach (var layer in _layers)
            {
                var size = (Vector2) layer.Texture!.Size * scale;
                layer.Arrange(UIBox2.FromDimensions(origin - size / 2, size));
            }

            return finalSize;
        }

        public void SetTint(Color color)
        {
            foreach (var layer in _layers)
                layer.ModulateSelfOverride = color;
        }

        public void SetColors(IReadOnlyList<Color>? colors)
        {
            for (var i = 0; i < _layers.Length; i++)
                _layers[i].ModulateSelfOverride = colors != null && i < colors.Count ? colors[i] : Color.White;
        }
    }
}
