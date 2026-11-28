using System.Numerics;
using Content.Client._WH40K.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Input;

namespace Content.Client._WH40K.CharacterCreation;

internal sealed class Wh40kColorPopup : Popup
{
    public const int PanelWidth = 312;
    private readonly LineEdit[] _channels = new LineEdit[6];
    private readonly LineEdit _hex = new() { Name = "HexColor", HorizontalExpand = true };
    private readonly PanelContainer _sample = new() { SetSize = new Vector2(26) };
    private Vector4 _hsv;
    private Color _color;
    private bool _updating;

    public event Action<Color>? OnColorChanged;

    public Wh40kColorPopup(Color initial)
    {
        var stack = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        AddChild(new PanelContainer { MinWidth = PanelWidth, PanelOverride = Wh40kInterfaceTheme.Card(12), Children = { stack } });
        stack.AddChild(new BoxContainer
        {
            SeparationOverride = 8,
            Children =
            {
                new Label
                {
                    Text = Loc.GetString("wh40k-personalization-custom-color"), HorizontalExpand = true,
                    StyleClasses = { "LabelHeading" }, VerticalAlignment = VAlignment.Center,
                },
                _sample,
            },
        });
        stack.AddChild(new ColorSurface(this, hueStrip: false) { MinHeight = 146 });
        stack.AddChild(new ColorSurface(this, hueStrip: true) { MinHeight = 18 });
        stack.AddChild(ChannelRow("RGB", 0, "R", "G", "B"));
        stack.AddChild(ChannelRow("HSV", 3, "H", "S", "V"));
        stack.AddChild(new BoxContainer { SeparationOverride = 6, Children = { Caption("HEX"), _hex } });

        _hex.OnTextChanged += _ =>
        {
            if (_updating)
                return;

            var hex = _hex.Text.Trim().TrimStart('#');
            if (hex.Length == 6 && Color.TryFromHex($"#{hex}", out var color))
                SetExact(color, _hex);
        };
        FinishEditing(_hex);
        SetExact(initial, null, notify: false);
    }

    private BoxContainer ChannelRow(string label, int start, string first, string second, string third)
    {
        var row = new BoxContainer { SeparationOverride = 6 };
        row.AddChild(Caption(label));
        row.AddChild(ChannelField(start, first));
        row.AddChild(ChannelField(start + 1, second));
        row.AddChild(ChannelField(start + 2, third));
        return row;
    }

    private BoxContainer ChannelField(int channel, string caption)
    {
        var input = new LineEdit { MinWidth = 48, HorizontalExpand = true, Name = $"Color{caption}" };
        _channels[channel] = input;
        input.OnTextChanged += _ => EditChannel(channel, input);
        FinishEditing(input);
        return new BoxContainer { HorizontalExpand = true, SeparationOverride = 3, Children = { Caption(caption), input } };
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        VerticalAlignment = VAlignment.Center,
        FontColorOverride = Wh40kInterfaceTheme.MutedText,
    };

    private void FinishEditing(LineEdit input)
    {
        input.OnFocusExit += _ => RefreshFields(null);
        input.OnTextEntered += _ => RefreshFields(null);
    }

    private void EditChannel(int channel, LineEdit field)
    {
        if (_updating || !int.TryParse(field.Text, out var value))
            return;

        if (channel < 3)
        {
            var rgb = new Vector3(_color.R, _color.G, _color.B);
            rgb[channel] = Math.Clamp(value, 0, 255) / 255f;
            SetExact(new Color(rgb.X, rgb.Y, rgb.Z), field);
            return;
        }

        var max = channel == 3 ? 360 : 100;
        var hsv = _hsv;
        hsv[channel - 3] = Math.Clamp(value, 0, max) / (float) max;
        SetHsv(hsv, field);
    }

    private void SetExact(Color color, LineEdit? source, bool notify = true)
    {
        _color = color.WithAlpha(1f);
        var hsv = Color.ToHsv(_color);
        if (hsv.Y == 0 || hsv.Z == 0)
            hsv.X = _hsv.X;
        _hsv = hsv;
        RefreshFields(source);
        if (notify)
            OnColorChanged?.Invoke(_color);
    }

    private void SetHsv(Vector4 hsv, LineEdit? source = null)
    {
        _hsv = hsv;
        _color = Color.FromHsv(hsv);
        RefreshFields(source);
        OnColorChanged?.Invoke(_color);
    }

    private void RefreshFields(LineEdit? source)
    {
        _updating = true;
        _sample.PanelOverride = new StyleBoxFlat(_color);
        SetText(_channels[0], _color.RByte.ToString());
        SetText(_channels[1], _color.GByte.ToString());
        SetText(_channels[2], _color.BByte.ToString());
        SetText(_channels[3], MathF.Round(_hsv.X * 360).ToString("0"));
        SetText(_channels[4], MathF.Round(_hsv.Y * 100).ToString("0"));
        SetText(_channels[5], MathF.Round(_hsv.Z * 100).ToString("0"));
        SetText(_hex, _color.ToHexNoAlpha());
        _updating = false;

        void SetText(LineEdit field, string text)
        {
            if (field != source)
                field.Text = text;
        }
    }

    private sealed class ColorSurface : Control
    {
        private readonly Wh40kColorPopup _owner;
        private readonly bool _hueStrip;
        private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[4];
        private bool _dragging;

        public ColorSurface(Wh40kColorPopup owner, bool hueStrip)
        {
            _owner = owner;
            _hueStrip = hueStrip;
            MouseFilter = MouseFilterMode.Stop;
            RectClipContent = true;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var box = PixelSizeBox;
            if (box.Width < 2 * UIScale || box.Height <= 0)
                return;

            var hsv = _owner._hsv;
            if (_hueStrip)
            {
                for (var i = 0; i < 6; i++)
                {
                    var left = Color.FromHsv(new Vector4(i / 6f, 1, 1, 1));
                    var right = Color.FromHsv(new Vector4((i + 1) / 6f, 1, 1, 1));
                    var segment = new UIBox2(box.Left + box.Width * i / 6, box.Top,
                        box.Left + box.Width * (i + 1) / 6, box.Bottom);
                    Gradient(handle, segment, left, right, right, left);
                }

                var x = Math.Clamp(box.Left + box.Width * hsv.X, box.Left + UIScale, box.Right - UIScale);
                handle.DrawRect(new UIBox2(x - 2 * UIScale, box.Top, x + 2 * UIScale, box.Bottom), Color.Black);
                handle.DrawRect(new UIBox2(x - UIScale, box.Top, x + UIScale, box.Bottom), Color.White);
                return;
            }

            var hue = Color.FromHsv(new Vector4(hsv.X, 1, 1, 1));
            Gradient(handle, box, Color.White, hue, hue, Color.White);
            var transparent = Color.Black.WithAlpha(0f);
            Gradient(handle, box, transparent, transparent, Color.Black, Color.Black);
            var position = new Vector2(box.Left + box.Width * hsv.Y, box.Top + box.Height * (1 - hsv.Z));
            handle.DrawCircle(position, 7 * UIScale, Color.Black, false);
            handle.DrawCircle(position, 5 * UIScale, Color.White, false);
        }

        private void Gradient(DrawingHandleScreen handle, UIBox2 box, Color topLeft, Color topRight, Color bottomRight, Color bottomLeft)
        {
            _vertices[0] = new(box.TopLeft, Vector2.Zero, topLeft);
            _vertices[1] = new(box.TopRight, Vector2.UnitX, topRight);
            _vertices[2] = new(box.BottomRight, Vector2.One, bottomRight);
            _vertices[3] = new(box.BottomLeft, Vector2.UnitY, bottomLeft);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White, _vertices);
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            _dragging = true;
            Pick(args.RelativePixelPosition);
            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            if (args.Function == EngineKeyFunctions.UIClick)
                _dragging = false;
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            if (_dragging)
                Pick(args.RelativePixelPosition);
        }

        private void Pick(Vector2 position)
        {
            var x = Math.Clamp(position.X / Math.Max(1, PixelSize.X), 0, 1);
            var hsv = _owner._hsv;
            if (_hueStrip)
                hsv.X = x;
            else
            {
                hsv.Y = x;
                hsv.Z = 1 - Math.Clamp(position.Y / Math.Max(1, PixelSize.Y), 0, 1);
            }
            _owner.SetHsv(hsv);
        }
    }
}
