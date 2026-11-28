using System.Numerics;
using Content.Shared._WH40K.StructureIntegrity;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.StructureIntegrity;

/// <summary>
/// Styled WH40K structural integrity bar widget inserted into the Examine tooltip for destructible structures.
/// </summary>
public sealed class Wh40kStructureIntegrityPanel : PanelContainer
{
    public const string ControlName = "Wh40kStructureIntegrityPanel";

    private static readonly Color PanelBg = Color.FromHex("#070B10").WithAlpha(224);
    private static readonly Color LabelBrass = Color.FromHex("#C5A55A");
    private static readonly Color ValueMuted = Color.FromHex("#D6CFC2");

    private static readonly Color ColorIntact = Color.FromHex("#52C46C");
    private static readonly Color ColorWorn = Color.FromHex("#D4A94E");
    private static readonly Color ColorDamaged = Color.FromHex("#E07E36");
    private static readonly Color ColorCritical = Color.FromHex("#D94242");

    public Wh40kStructureIntegrityPanel(int current, int max)
    {
        Name = ControlName;
        HorizontalExpand = true;
        Margin = new Thickness(6, 3, 6, 3);

        var clampedMax = Math.Max(1, max);
        var clampedCur = Math.Clamp(current, 0, clampedMax);
        var fraction = clampedCur / (float) clampedMax;
        var percent = (int) MathF.Round(fraction * 100f);
        var accent = GetAccentColor(fraction);
        var statusKey = GetStatusKey(fraction);

        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = PanelBg,
            BorderColor = accent.WithAlpha(0.72f),
            BorderThickness = new Thickness(2, 1, 1, 1),
            ContentMarginLeftOverride = 7,
            ContentMarginRightOverride = 7,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 5,
        };

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };

        var titleLabel = new Label
        {
            Text = Loc.GetString("wh40k-structure-integrity-label"),
            FontColorOverride = LabelBrass,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
        };

        var statusLabel = new Label
        {
            Text = Loc.GetString(statusKey),
            FontColorOverride = accent,
            VerticalAlignment = VAlignment.Center,
        };

        var valueLabel = new Label
        {
            Text = Loc.GetString("wh40k-structure-integrity-values",
                ("current", clampedCur),
                ("max", clampedMax),
                ("percent", percent)),
            FontColorOverride = ValueMuted,
            VerticalAlignment = VAlignment.Center,
        };

        header.AddChild(titleLabel);
        header.AddChild(statusLabel);
        header.AddChild(valueLabel);

        root.AddChild(header);
        root.AddChild(new SegmentedIntegrityBar(fraction, accent));
        AddChild(root);
    }

    private static Color GetAccentColor(float fraction) => fraction switch
    {
        >= 0.75f => ColorIntact,
        >= 0.45f => ColorWorn,
        >= 0.20f => ColorDamaged,
        _ => ColorCritical,
    };

    private static string GetStatusKey(float fraction) => fraction switch
    {
        >= 0.99f => "wh40k-structure-integrity-status-intact",
        >= 0.75f => "wh40k-structure-integrity-status-scratched",
        >= 0.45f => "wh40k-structure-integrity-status-damaged",
        >= 0.20f => "wh40k-structure-integrity-status-heavy",
        _ => "wh40k-structure-integrity-status-critical",
    };

    private sealed class SegmentedIntegrityBar : Control
    {
        private const int Segments = 10;
        private static readonly Color TrackBg = Color.FromHex("#12171E");
        private static readonly Color TrackBorder = Color.FromHex("#4A3F2B");
        private static readonly Color EmptySegment = Color.FromHex("#1E242D");

        private readonly float _fraction;
        private readonly Color _accent;

        public SegmentedIntegrityBar(float fraction, Color accent)
        {
            _fraction = Math.Clamp(fraction, 0f, 1f);
            _accent = accent;
            HorizontalExpand = true;
            MinHeight = 9;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var box = PixelSizeBox;
            if (box.Width <= 2f || box.Height <= 2f)
                return;

            handle.DrawRect(box, TrackBg);
            handle.DrawRect(box, TrackBorder, filled: false);

            var pad = MathF.Max(1f, MathF.Round(1.5f * UIScale));
            var gap = MathF.Max(1f, MathF.Round(2f * UIScale));
            var innerLeft = box.Left + pad;
            var innerTop = box.Top + pad;
            var innerWidth = box.Width - pad * 2f;
            var innerHeight = box.Height - pad * 2f;
            if (innerWidth <= 0f || innerHeight <= 0f)
                return;

            var segWidth = (innerWidth - gap * (Segments - 1)) / Segments;
            if (segWidth <= 1f)
                return;

            var filledUnits = _fraction * Segments;
            for (var i = 0; i < Segments; i++)
            {
                var x0 = innerLeft + i * (segWidth + gap);
                var segBox = new UIBox2(x0, innerTop, x0 + segWidth, innerTop + innerHeight);
                handle.DrawRect(segBox, EmptySegment);

                var segFill = Math.Clamp(filledUnits - i, 0f, 1f);
                if (segFill <= 0f)
                    continue;

                var fillBox = new UIBox2(x0, innerTop, x0 + segWidth * segFill, innerTop + innerHeight);
                handle.DrawRect(fillBox, _accent);

                var highlightHeight = MathF.Max(1f, innerHeight * 0.3f);
                var highlightBox = new UIBox2(x0, innerTop, x0 + segWidth * segFill, innerTop + highlightHeight);
                handle.DrawRect(highlightBox, Color.White.WithAlpha(0.22f));
            }
        }
    }
}
