using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Progression;

internal static class Wh40kClassPalette
{
    public static readonly Color Background = Color.FromHex("#0D0B0A");
    public static readonly Color Panel = Color.FromHex("#171411");
    public static readonly Color RaisedPanel = Color.FromHex("#241F1A");
    public static readonly Color Border = Color.FromHex("#765135");
    public static readonly Color Gold = Color.FromHex("#D3A85D");
    public static readonly Color Points = Color.FromHex("#F2D44F");
    public static readonly Color Text = Color.FromHex("#D7D2C3");
    public static readonly Color MutedText = Color.FromHex("#8F8C82");
    public static readonly Color Danger = Color.FromHex("#C86A55");

    public static StyleBoxFlat CreatePanel(bool raised = false, float border = 1f)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = raised ? RaisedPanel : Panel,
            BorderColor = Border,
            BorderThickness = new Thickness(border),
            ContentMarginLeftOverride = 1f,
            ContentMarginRightOverride = 1f,
            ContentMarginTopOverride = 1f,
            ContentMarginBottomOverride = 1f,
        };
    }

    public static StyleBoxFlat CreateButton(bool accented = false, bool danger = false)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = danger ? Color.FromHex("#3B1C17") : accented ? Color.FromHex("#554421") : RaisedPanel,
            BorderColor = danger ? Danger : accented ? Points : Border,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 9f,
            ContentMarginRightOverride = 9f,
            ContentMarginTopOverride = 5f,
            ContentMarginBottomOverride = 5f,
        };
    }
}
