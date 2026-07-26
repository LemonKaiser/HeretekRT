using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Progression;

/// <summary>
/// Check box whose caption wraps instead of being clipped by the built-in single-line label.
/// </summary>
public sealed class Wh40kMultilineCheckBox : CheckBox
{
    private readonly RichTextLabel _multilineLabel;

    public Wh40kMultilineCheckBox()
    {
        Label.Visible = false;

        var contents = (BoxContainer) TextureRect.Parent!;
        contents.HorizontalExpand = true;

        _multilineLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(6, 0, 0, 0),
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        contents.AddChild(_multilineLabel);
    }

    public void SetMessage(string message, Color color)
    {
        _multilineLabel.SetMessage(message, color);
    }
}
