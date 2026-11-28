using Content.Client.Stylesheets;
using Content.Shared._WH40K.Construction;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Construction;

/// <summary>
/// Joined row of x2..x50 and All repeat-craft buttons under the construction menu's Craft button.
/// </summary>
public sealed class Wh40kCraftRepeatButtons : BoxContainer
{
    private static readonly int[] Counts = [2, 5, 10, 20, 50];

    public event Action<int>? OnRepeat;

    public Wh40kCraftRepeatButtons()
    {
        Orientation = LayoutOrientation.Horizontal;
        HorizontalExpand = true;

        foreach (var count in Counts)
        {
            AddButton(Loc.GetString("wh40k-craft-repeat-count", ("count", count)),
                Loc.GetString("wh40k-craft-repeat-tooltip", ("count", count)),
                count,
                count == Counts[0] ? StyleBase.ButtonOpenRight : StyleBase.ButtonOpenBoth);
        }

        AddButton(Loc.GetString("wh40k-craft-repeat-all"),
            Loc.GetString("wh40k-craft-repeat-all-tooltip"),
            Wh40kCraftRepeatRequestEvent.MaxCount,
            StyleBase.ButtonOpenLeft);
    }

    private void AddButton(string text, string tooltip, int count, string styleClass)
    {
        var button = new Button
        {
            Text = text,
            ToolTip = tooltip,
            HorizontalExpand = true,
        };
        button.AddStyleClass(styleClass);
        button.OnPressed += _ => OnRepeat?.Invoke(count);
        AddChild(button);
    }
}
