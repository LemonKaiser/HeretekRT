using System.Linq;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Augments;

public sealed class Wh40kAugmentModelPicker : OptionButton
{
    public Wh40kAugmentModelPicker()
    {
        PrefixMargin = false;
        OptionsScroll.HScrollEnabled = false;

        foreach (var row in Children.OfType<BoxContainer>())
        {
            foreach (var caption in row.Children.OfType<Label>())
                ConfigureCaption(caption);
        }
    }

    public override void ButtonOverride(Button button)
    {
        ConfigureCaption(button.Label);
        button.ToolTip = button.Text;
    }

    private static void ConfigureCaption(Label caption)
    {
        caption.ClipText = true;
        caption.RemoveStyleClass(StyleClassOptionButton);
        caption.Align = Label.AlignMode.Left;
    }
}
