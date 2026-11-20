using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Augments;

public static class Wh40kAugmentStyle
{
    public static readonly Color Text = Color.FromHex("#E8E8E8");
    public static readonly Color SecondaryText = Color.FromHex("#BDBDBD");
    public static readonly Color SelectedText = Color.White;

    internal static StyleBoxFlat Panel(int padding) => Frame("#222222", "#444444", padding);

    internal static StyleBoxFlat Card(int padding, bool selected = false)
        => selected ? Frame("#414141", "#929292", padding) : Frame("#2B2B2B", "#525252", padding);

    private static StyleBoxFlat Frame(string background, string border, int padding)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = padding,
            ContentMarginRightOverride = padding,
            ContentMarginTopOverride = padding,
            ContentMarginBottomOverride = padding,
        };
    }
}
