using System.Numerics;
using Content.Shared._WH40K.Warmth;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Warmth;

/// <summary>
/// WH40K Imperial Auspex segmented thermal bar with a glowing position marker and directional trend chevron.
/// </summary>
public sealed class Wh40kWarmthGauge : Control
{
    private const int Segments = 14;
    private const float ArrowAreaWidth = 14f;

    private static readonly Color TrackBg = Color.FromHex("#0A0807");
    private static readonly Color TrackBorder = Color.FromHex("#56452D");
    private static readonly Color CenterNotch = Color.FromHex("#E1C17E");
    private static readonly Color MarkerGold = Color.FromHex("#F6E4B4");
    private static readonly Color SteadyColor = Color.FromHex("#AD9F86");
    private static readonly Color WarmingColor = Color.FromHex("#E89242");
    private static readonly Color CoolingColor = Color.FromHex("#6BB5FF");

    private static readonly (float At, Color Color)[] Ramp =
    {
        (0.00f, Color.FromHex("#4B96EB")),
        (0.28f, Color.FromHex("#67C3CE")),
        (0.50f, Color.FromHex("#6EBA7E")),
        (0.72f, Color.FromHex("#DFBA63")),
        (0.86f, Color.FromHex("#E48836")),
        (1.00f, Color.FromHex("#D94242")),
    };

    private readonly List<Vector2> _arrowVerts = new(4);

    public int Gauge { get; private set; } = 50;
    public Wh40kWarmthTrend Trend { get; private set; }

    public Wh40kWarmthGauge()
    {
        MinSize = new Vector2(0f, 14f);
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    public void Set(int gauge, Wh40kWarmthTrend trend)
    {
        Gauge = Math.Clamp(gauge, 0, 100);
        Trend = trend;
    }

    private static Color SampleRamp(float t)
    {
        for (var i = 1; i < Ramp.Length; i++)
        {
            if (t > Ramp[i].At)
                continue;

            var (from, to) = (Ramp[i - 1], Ramp[i]);
            return Color.InterpolateBetween(from.Color, to.Color, (t - from.At) / (to.At - from.At));
        }

        return Ramp[^1].Color;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var scale = UIScale;
        var width = PixelSize.X;
        var height = PixelSize.Y;
        var arrowSpace = ArrowAreaWidth * scale;
        var trackRight = width - arrowSpace;
        if (trackRight <= 10f || height <= 4f)
            return;

        var trackHeight = 8f * scale;
        var trackTop = (height - trackHeight) * 0.5f;
        var trackBox = new UIBox2(0f, trackTop, trackRight, trackTop + trackHeight);

        handle.DrawRect(trackBox, TrackBg);
        handle.DrawRect(trackBox, TrackBorder, filled: false);

        var pad = MathF.Max(1f, MathF.Round(1.5f * scale));
        var gap = MathF.Max(1f, MathF.Round(1.5f * scale));
        var innerLeft = trackBox.Left + pad;
        var innerTop = trackBox.Top + pad;
        var innerWidth = trackBox.Width - pad * 2f;
        var innerHeight = trackBox.Height - pad * 2f;

        var gaugeNorm = Gauge / 100f;
        if (innerWidth > 2f && innerHeight > 1f)
        {
            var segWidth = (innerWidth - gap * (Segments - 1)) / Segments;
            if (segWidth > 0.5f)
            {
                for (var i = 0; i < Segments; i++)
                {
                    var segCenter = (i + 0.5f) / Segments;
                    var dist = MathF.Abs(segCenter - gaugeNorm);
                    // Segments near the current thermal reading glow brighter; distant ones stay muted.
                    var brightness = dist < 0.14f ? 1.0f : dist < 0.28f ? 0.65f : 0.30f;
                    var col = SampleRamp(segCenter);
                    var drawCol = new Color(col.R * brightness, col.G * brightness, col.B * brightness, 0.95f);

                    var x0 = innerLeft + i * (segWidth + gap);
                    var segBox = new UIBox2(x0, innerTop, x0 + segWidth, innerTop + innerHeight);
                    handle.DrawRect(segBox, drawCol);

                    if (brightness >= 0.9f)
                    {
                        var hlH = MathF.Max(1f, innerHeight * 0.32f);
                        handle.DrawRect(new UIBox2(x0, innerTop, x0 + segWidth, innerTop + hlH), Color.White.WithAlpha(0.24f));
                    }
                }
            }

            // Subtle brass comfort-zone center tick
            var midX = innerLeft + innerWidth * 0.5f;
            handle.DrawRect(
                new UIBox2(midX - 0.5f * scale, trackBox.Top - 1f * scale, midX + 0.5f * scale, trackBox.Bottom + 1f * scale),
                CenterNotch.WithAlpha(0.45f));

            // Glowing golden indicator needle for current body thermal level
            var markerX = innerLeft + innerWidth * gaugeNorm;
            handle.DrawRect(
                new UIBox2(markerX - 1.25f * scale, trackBox.Top - 2f * scale, markerX + 1.25f * scale, trackBox.Bottom + 2f * scale),
                MarkerGold);
        }

        // Trend chevron on right side
        var cx = trackRight + arrowSpace * 0.5f;
        var cy = height * 0.5f;
        var half = 3.2f * scale;
        if (Trend == Wh40kWarmthTrend.Steady)
        {
            handle.DrawRect(new UIBox2(cx - half * 0.7f, cy - 0.75f * scale, cx + half * 0.7f, cy + 0.75f * scale), SteadyColor);
            return;
        }

        var warming = Trend == Wh40kWarmthTrend.Warming;
        _arrowVerts.Clear();
        _arrowVerts.Add(new Vector2(cx, cy + (warming ? -half : half)));
        _arrowVerts.Add(new Vector2(cx - half, cy + (warming ? half : -half)));
        _arrowVerts.Add(new Vector2(cx + half, cy + (warming ? half : -half)));
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _arrowVerts, warming ? WarmingColor : CoolingColor);
    }
}
