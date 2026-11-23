using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.UserInterface;

internal sealed class Wh40kContextMenuBody : GridContainer
{
    internal const string SectionLabelStyle = "Wh40kContextMenuSectionLabel";
    private static readonly StyleBoxFlat SectionDivider = new() { BackgroundColor = Color.FromHex("#8F744A55") };

    internal void AddSection(string title)
    {
        var heading = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
            Margin = new Thickness(10, 2, 10, 1),
        };
        if (ChildCount > 0)
        {
            heading.AddChild(new PanelContainer
            {
                PanelOverride = SectionDivider,
                SetHeight = 1,
                Margin = new Thickness(0, 5, 0, 6),
                MouseFilter = MouseFilterMode.Ignore,
            });
        }
        heading.AddChild(new Label
        {
            Text = title,
            StyleClasses = { SectionLabelStyle },
            MouseFilter = MouseFilterMode.Ignore,
        });
        AddChild(heading);
    }
}
