using System.Globalization;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._WH40K.Dialogue.UI;

/// <summary>
/// A dialogue response that wraps to its available width and grows to fit the
/// complete text. ContainerButton keeps the entire response clickable.
/// </summary>
public sealed class DialogueChoiceButton : ContainerButton
{
    private readonly RichTextLabel _label;
    private Color _textColor = Color.White;
    public string Text { get; }

    public Color TextColor
    {
        get => _textColor;
        set
        {
            if (_textColor == value)
                return;

            _textColor = value;
            _label.SetMessage(FormattedMessage.FromUnformatted(Text), value);
        }
    }

    public DialogueChoiceButton(string text, Font? font = null, int? number = null)
    {
        Text = text;
        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 10
        };
        if (number is { } index)
        {
            row.AddChild(new Label
            {
                Text = index.ToString("00", CultureInfo.InvariantCulture),
                FontOverride = font,
                FontColorOverride = new Color(215, 190, 132),
                MinWidth = 28f,
                VerticalAlignment = VAlignment.Center,
                MouseFilter = MouseFilterMode.Ignore
            });
        }
        _label = new RichTextLabel
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore
        };
        if (font != null)
        {
            _label.Stylesheet = new Stylesheet(new[]
            {
                new StyleRule(new SelectorElement(typeof(RichTextLabel), null, null, null), new[] { new StyleProperty("font", font) })
            });
        }

        _label.SetMessage(FormattedMessage.FromUnformatted(text), _textColor);
        row.AddChild(_label);
        AddChild(row);
    }
}
