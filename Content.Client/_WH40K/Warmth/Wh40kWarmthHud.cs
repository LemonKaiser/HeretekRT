using System.Numerics;
using Content.Shared._WH40K.Warmth;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Warmth;

/// <summary>
/// WH40K Heritage-styled Thermal Auspex HUD badge displaying ambient air temperature,
/// body thermal feel, wetness/fire status badges, and a segmented thermal gauge.
/// </summary>
public sealed class Wh40kWarmthHud : PanelContainer
{
    public const float PanelWidth = 172f;
    public const float MaxPanelWidth = 236f;
    public const float PanelHeight = 52f;

    internal const int ColdAirThreshold = 8;
    internal const int HotAirThreshold = 37;

    private static readonly Color BgObsidian = Color.FromHex("#120F0CE6");
    private static readonly Color BrassBorder = Color.FromHex("#75613D");
    private static readonly Color BrassMuted = Color.FromHex("#56452D");
    private static readonly Color GoldHeader = Color.FromHex("#E1C17E");
    private static readonly Color ParchmentText = Color.FromHex("#E9E1D0");
    private static readonly Color ColdAirText = Color.FromHex("#88C8F2");
    private static readonly Color HotAirText = Color.FromHex("#F0A44B");
    private static readonly Color VacuumText = Color.FromHex("#AD9F86");
    private static readonly Color WetBadgeColor = Color.FromHex("#7CC6EE");
    private static readonly Color FireBadgeColor = Color.FromHex("#F0A44B");

    private readonly StyleBoxFlat _panelStyle;
    private readonly PanelContainer _accentStripe;
    private readonly StyleBoxFlat _accentStyle;
    private readonly Label _hereLabel;
    private readonly Label _feelLabel;
    private readonly Label _metaBadge;
    private readonly Wh40kWarmthGauge _gauge;

    public Wh40kWarmthHud(IResourceCache resources)
    {
        MinSize = new Vector2(PanelWidth, PanelHeight);
        MaxWidth = MaxPanelWidth;
        MouseFilter = MouseFilterMode.Ignore;

        _panelStyle = new StyleBoxFlat
        {
            BackgroundColor = BgObsidian,
            BorderColor = BrassBorder,
            BorderThickness = new Thickness(1),
        };
        _panelStyle.SetContentMarginOverride(StyleBox.Margin.Left, 0);
        _panelStyle.SetContentMarginOverride(StyleBox.Margin.Right, 8);
        _panelStyle.SetContentMarginOverride(StyleBox.Margin.Top, 5);
        _panelStyle.SetContentMarginOverride(StyleBox.Margin.Bottom, 5);
        PanelOverride = _panelStyle;

        var titleFont = new VectorFont(resources.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 8);
        var monoFont = new VectorFont(resources.GetResource<FontResource>("/Fonts/_WH40K/Speech/JetBrainsMono/JetBrainsMono-Bold.ttf"), 9);
        var feelFont = new VectorFont(resources.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 10);
        var badgeFont = new VectorFont(resources.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 8);

        var shell = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 7,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(shell);

        _accentStyle = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#88C98F"),
        };
        _accentStripe = new PanelContainer
        {
            MinWidth = 3,
            MaxWidth = 3,
            VerticalExpand = true,
            PanelOverride = _accentStyle,
            MouseFilter = MouseFilterMode.Ignore,
        };
        shell.AddChild(_accentStripe);

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        shell.AddChild(body);

        // Header row: THERMAL AUSPEX + ambient reading
        var headerRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        headerRow.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-warmth-hud-title"),
            FontOverride = titleFont,
            FontColorOverride = GoldHeader,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        });

        _hereLabel = new Label
        {
            FontOverride = monoFont,
            FontColorOverride = ParchmentText,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        headerRow.AddChild(_hereLabel);
        body.AddChild(headerRow);

        // Subtle antique brass hairline separator
        body.AddChild(new PanelContainer
        {
            MinHeight = 1,
            MaxHeight = 1,
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = BrassMuted.WithAlpha(0.75f) },
            MouseFilter = MouseFilterMode.Ignore,
        });

        // Body feel + status badge row
        var feelRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _feelLabel = new Label
        {
            FontOverride = feelFont,
            FontColorOverride = Color.FromHex("#88C98F"),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _metaBadge = new Label
        {
            FontOverride = badgeFont,
            FontColorOverride = FireBadgeColor,
            VerticalAlignment = VAlignment.Center,
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };
        feelRow.AddChild(_feelLabel);
        feelRow.AddChild(_metaBadge);
        body.AddChild(feelRow);

        _gauge = new Wh40kWarmthGauge();
        body.AddChild(_gauge);
    }

    public void UpdateReadout(Wh40kWarmthComponent comp)
    {
        var feelColor = SharedWh40kWarmthSystem.FeelColor(comp.Feel);
        _accentStyle.BackgroundColor = feelColor;
        _panelStyle.BorderColor = comp.Strain == Wh40kWarmthStrain.None
            ? BrassBorder
            : Color.InterpolateBetween(BrassBorder, feelColor, 0.45f);

        var hereText = SharedWh40kWarmthSystem.HereText(comp);
        if (_hereLabel.Text != hereText)
            _hereLabel.Text = hereText;

        _hereLabel.FontColorOverride = comp.HereCelsius switch
        {
            null => VacuumText,
            <= ColdAirThreshold => ColdAirText,
            >= HotAirThreshold => HotAirText,
            _ => ParchmentText,
        };

        var feelText = Loc.GetString(SharedWh40kWarmthSystem.FeelLoc(comp.Feel));
        if (_feelLabel.Text != feelText)
            _feelLabel.Text = feelText;
        _feelLabel.FontColorOverride = feelColor;

        if (comp.NearWarmth)
        {
            _metaBadge.Text = Loc.GetString("wh40k-warmth-near");
            _metaBadge.FontColorOverride = FireBadgeColor;
            _metaBadge.Visible = true;
        }
        else if (comp.Wetness >= 0.6f)
        {
            _metaBadge.Text = Loc.GetString("wh40k-warmth-badge-soaked");
            _metaBadge.FontColorOverride = WetBadgeColor;
            _metaBadge.Visible = true;
        }
        else if (comp.Wetness >= 0.2f)
        {
            _metaBadge.Text = Loc.GetString("wh40k-warmth-badge-damp");
            _metaBadge.FontColorOverride = WetBadgeColor;
            _metaBadge.Visible = true;
        }
        else
        {
            _metaBadge.Visible = false;
        }

        _gauge.Set(comp.Gauge, comp.Trend);
    }
}
