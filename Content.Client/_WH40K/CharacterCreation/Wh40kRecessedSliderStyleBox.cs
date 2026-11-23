using System.Numerics;
using Content.Client.Lobby.UI;
using Content.Shared.Humanoid;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.CharacterCreation;

internal sealed class Wh40kRecessedSliderStyleBox : StyleBox
{
    internal enum Part : byte { Background, Fill, Foreground, Grabber }

    private static readonly Color Ink = Color.FromHex("#090806");
    private static readonly Color FillColor = Color.FromHex("#8D7340");
    private static readonly Color Groove = Color.FromHex("#594222");
    private static readonly Color GrooveLight = Color.FromHex("#E2C58B80");
    private static readonly Color[] Metal = CreateMetal();
    private static readonly Color[] SkinTones = CreateSkinTones();
    private readonly Part _part;
    private readonly StyleBox? _gradient;
    private readonly bool _skinTones;
    private readonly LobbyRoundedStyleBox? _rim;

    public Wh40kRecessedSliderStyleBox(Part part, StyleBox? gradient = null, bool skinTones = false)
    {
        _part = part;
        _gradient = gradient;
        _skinTones = skinTones;
        if (part is Part.Foreground or Part.Grabber)
        {
            _rim = new LobbyRoundedStyleBox
            {
                BackgroundColor = part == Part.Grabber ? Color.FromHex("#8B7142") : Color.Transparent,
                BorderColor = Color.FromHex(part == Part.Grabber ? "#F1D89F" : "#5C4D32"),
                BorderThickness = new Thickness(1),
                CornerRadius = part == Part.Grabber ? 2 : 3,
            };
        }
    }

    protected override float GetDefaultContentMargin(Margin margin)
        => _part != Part.Grabber ? 0 : margin is Margin.Left or Margin.Right ? 8 : 12;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        if (box.Width <= 0 || box.Height <= 0)
            return;
        var track = CentredStrip(box, 10 * uiScale);
        switch (_part)
        {
            case Part.Background:
                if (_gradient != null)
                    _gradient.Draw(handle, track, uiScale);
                else if (_skinTones)
                    DrawPalette(handle, track, SkinTones);
                else
                    handle.DrawRect(track, Ink);
                break;
            case Part.Fill:
                if (box.Width > uiScale)
                    handle.DrawRect(new UIBox2(box.Left + uiScale, box.Center.Y - 2 * uiScale,
                        box.Right, box.Center.Y + 2 * uiScale), FillColor);
                break;
            case Part.Foreground:
                _rim!.Draw(handle, track, uiScale);
                var left = track.Left + uiScale;
                var right = track.Right - uiScale;
                if (right <= left)
                    break;
                handle.DrawRect(new UIBox2(left, track.Top + uiScale, right, track.Top + 2 * uiScale),
                    Color.Black.WithAlpha(0.45f));
                handle.DrawRect(new UIBox2(left, track.Top + 2 * uiScale, right, track.Top + 3 * uiScale),
                    Color.Black.WithAlpha(0.18f));
                break;
            case Part.Grabber:
                DrawGrabber(handle, box, uiScale);
                break;
        }
    }

    private void DrawGrabber(DrawingHandleScreen handle, UIBox2 box, float scale)
    {
        var half = Vector2.Min(box.Size, new Vector2(16, 24) * scale) / 2;
        var body = new UIBox2(box.Center - half, box.Center + half);
        handle.DrawRect(new UIBox2(body.Left, body.Top + scale, body.Right, body.Bottom + scale),
            Color.Black.WithAlpha(0.35f));
        _rim!.Draw(handle, body, scale);
        var inset = new Thickness(scale).Deflate(body);
        if (inset.Width <= 0 || inset.Height <= 0)
            return;
        DrawPalette(handle, inset, Metal);
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1f : 1f;
            var x = body.Center.X + side * 3 * scale;
            handle.DrawRect(new UIBox2(x, body.Top + 6 * scale, x + scale, body.Bottom - 6 * scale), Groove);
            handle.DrawRect(new UIBox2(x + scale, body.Top + 6 * scale, x + 2 * scale, body.Bottom - 6 * scale), GrooveLight);
        }
    }

    private static UIBox2 CentredStrip(UIBox2 box, float height)
    {
        var halfHeight = MathF.Min(height, box.Height) / 2;
        return new UIBox2(box.Left, box.Center.Y - halfHeight, box.Right, box.Center.Y + halfHeight);
    }

    private static void DrawPalette(DrawingHandleScreen handle, UIBox2 box, Color[] colors)
    {
        for (var i = 0; i < colors.Length; i++)
        {
            handle.DrawRect(new UIBox2(box.Left + box.Width * i / colors.Length, box.Top,
                box.Left + box.Width * (i + 1) / colors.Length, box.Bottom), colors[i]);
        }
    }

    private static Color[] CreateSkinTones()
    {
        var colors = new Color[101];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = SkinColor.HumanSkinTone(i);
        return colors;
    }

    private static Color[] CreateMetal()
    {
        var colors = new Color[14];
        var left = Color.FromHex("#7A6137");
        var centre = Color.FromHex("#D6B475");
        var right = Color.FromHex("#8B7142");
        for (var i = 0; i < colors.Length; i++)
        {
            var position = i / (float) (colors.Length - 1);
            var a = position <= 0.45f ? left : centre;
            var b = position <= 0.45f ? centre : right;
            var amount = position <= 0.45f ? position / 0.45f : (position - 0.45f) / 0.55f;
            colors[i] = new Color(a.R + (b.R - a.R) * amount, a.G + (b.G - a.G) * amount,
                a.B + (b.B - a.B) * amount, 1);
        }
        return colors;
    }
}
