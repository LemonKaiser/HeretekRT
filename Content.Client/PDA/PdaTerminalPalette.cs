using Robust.Client.Graphics;

namespace Content.Client.PDA;

internal static class PdaTerminalPalette
{
    public static readonly Color Chassis = Color.FromHex("#6F7058");
    public static readonly Color ChassisEdge = Color.FromHex("#A7A891");
    public static readonly Color Bezel = Color.FromHex("#343A34");
    public static readonly Color Screen = Color.FromHex("#171B19");
    public static readonly Color ScreenPanel = Color.FromHex("#222A25");
    public static readonly Color RaisedPanel = Color.FromHex("#2C3730");
    public static readonly Color Rail = Color.FromHex("#536258");
    public static readonly Color Accent = Color.FromHex("#88D2A5");
    public static readonly Color AccentMuted = Color.FromHex("#4B866B");
    public static readonly Color Danger = Color.FromHex("#D56A6A");
    public static readonly Color DangerMuted = Color.FromHex("#6B3235");
    public static readonly Color PrimaryText = Color.FromHex("#D7E0D7");
    public static readonly Color SecondaryText = Color.FromHex("#93A096");
    public static readonly Color DisabledText = Color.FromHex("#667168");

    public static StyleBoxFlat CreatePanel(Color background, Color border, Thickness? borderThickness = null)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = borderThickness ?? new Thickness(1)
        };
    }

    public static StyleBoxFlat CreateButton(bool accented = false, bool danger = false)
    {
        if (danger)
            return CreatePanel(DangerMuted, Danger, new Thickness(1));

        return CreatePanel(
            accented ? AccentMuted : RaisedPanel,
            accented ? Accent : Rail,
            new Thickness(1));
    }
}
