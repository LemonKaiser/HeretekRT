using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.UserInterface;

/// <summary>Decorates the original label, so native window title updates and clipping continue to work.</summary>
internal sealed class Wh40kWindowTitle : Control
{
    private readonly Label _label;
    private readonly Texture _left;
    private readonly Texture _right;
    private float _textWidth;
    private ReadOnlyMemory<char> _measuredText;
    private Font? _measuredFont;
    private float _measuredScale;
    private float _centre;
    private float _idealCentre = float.NaN;
    private BaseWindow? _window;
    internal float TextCentreGlobal => _label.GlobalPosition.X + _label.Width / 2;

    public Wh40kWindowTitle(Label label, IResourceCache resources)
    {
        _label = label;
        _left = resources.GetResource<TextureResource>("/Textures/_WH40K/Interface/header-laurel.svg.192dpi.png").Texture;
        _right = resources.GetResource<TextureResource>("/Textures/_WH40K/Interface/header-laurel-right.svg.192dpi.png").Texture;
        HorizontalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Ignore;
        _label.ClipText = true;
        _label.Margin = default;
        AddChild(_label);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _label.Measure(availableSize);
        RefreshMetrics();
        return new Vector2(0, _label.DesiredSize.Y);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var changed = RefreshMetrics();
        if (_window == null)
            for (var ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor is BaseWindow window) { _window = window; break; }
        var ideal = _window == null ? Width / 2 : _window.GlobalPosition.X + _window.Width / 2 - GlobalPosition.X;
        if (!float.IsFinite(_idealCentre) || Math.Abs(_idealCentre - ideal) > 0.1f)
        {
            _idealCentre = ideal;
            changed = true;
        }
        if (changed) { InvalidateMeasure(); InvalidateArrange(); }
    }

    private bool RefreshMetrics()
    {
        var font = _label.FontOverride ?? (_label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled : UserInterfaceManager.ThemeDefaults.LabelFont);
        var text = _label.TextMemory;
        if (_measuredText.Equals(text) && _measuredFont == font && _measuredScale == UIScale)
            return false;
        _measuredText = text; _measuredFont = font; _measuredScale = UIScale;
        _textWidth = 0;
        var value = text.ToString();
        foreach (var rune in value.EnumerateRunes())
            _textWidth += font.GetCharMetrics(rune, UIScale)?.Advance ?? 0;
        _label.ToolTip = value;
        return true;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        RefreshMetrics();
        var ideal = float.IsFinite(_idealCentre) ? _idealCentre : finalSize.X / 2;
        if (_textWidth / UIScale > finalSize.X)
        {
            _centre = finalSize.X / 2;
            _label.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(_textWidth / UIScale, finalSize.Y)));
            return finalSize;
        }
        var textWidth = Math.Min(finalSize.X, _textWidth / UIScale);
        var half = textWidth / 2;
        if (finalSize.X >= textWidth + 140)
            half += 70;
        _centre = Math.Clamp(ideal, half, finalSize.X - half);
        _label.Arrange(UIBox2.FromDimensions(new Vector2(_centre - textWidth / 2, 0), new Vector2(textWidth, finalSize.Y)));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var width = 60 * UIScale;
        var height = width * _left.Height / _left.Width;
        var gap = 10 * UIScale;
        if (_textWidth <= 0 || PixelSize.X < _textWidth + 2 * (width + gap))
            return;
        var centre = new Vector2(_centre * UIScale, PixelSize.Y / 2f);
        if (centre.X - _textWidth / 2 - gap - width < 0 || centre.X + _textWidth / 2 + gap + width > PixelSize.X)
            return;
        handle.DrawTextureRect(_left, UIBox2.FromDimensions(
            new Vector2(centre.X - _textWidth / 2 - gap - width, centre.Y - height / 2), new Vector2(width, height)), Color.White.WithAlpha(0.94f));
        handle.DrawTextureRect(_right, UIBox2.FromDimensions(
            new Vector2(centre.X + _textWidth / 2 + gap, centre.Y - height / 2), new Vector2(width, height)), Color.White.WithAlpha(0.94f));
    }
}
