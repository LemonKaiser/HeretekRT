using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Mapping;

internal sealed class Wh40kMappingLabel : Label
{
    private readonly int _lines;
    private string _fullText = string.Empty;

    public string FullText
    {
        set
        {
            if (_fullText == value)
                return;
            _fullText = value.Replace('\r', ' ').Replace('\n', ' ');
            InvalidateArrange();
        }
    }

    public Wh40kMappingLabel(int lines)
    {
        _lines = lines;
        ClipText = true;
        VAlign = VAlignMode.Center;
    }

    private Font DisplayFont => FontOverride ??
        (TryGetStyleProperty<Font>(StylePropertyFont, out var font) ? font : UserInterfaceManager.ThemeDefaults.LabelFont);

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var font = DisplayFont;
        return new Vector2(0, (font.GetHeight(UIScale) + (_lines - 1) * font.GetLineHeight(UIScale)) / UIScale);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var font = DisplayFont;
        var width = finalSize.X * UIScale;
        var first = Fit(_fullText, width, font);
        var displayed = _fullText;
        if (first < _fullText.Length)
        {
            if (_lines == 1)
            {
                displayed = Ellipsize(_fullText, width, font);
            }
            else
            {
                var space = _fullText.AsSpan(0, first).LastIndexOf(' ');
                var split = space > 0 ? space : first;
                var rest = _fullText[split..].TrimStart();
                displayed = $"{_fullText[..split].TrimEnd()}\n{Ellipsize(rest, width, font)}";
            }
        }
        if (Text != displayed)
            Text = displayed;
        return base.ArrangeOverride(finalSize);
    }

    private string Ellipsize(string text, float width, Font font)
    {
        if (Fit(text, width, font) == text.Length)
            return text;
        var ellipsisWidth = font.GetCharMetrics(new Rune('…'), UIScale)?.Advance ?? 0;
        return text[..Fit(text, width - ellipsisWidth, font)].TrimEnd() + "…";
    }

    private int Fit(string text, float width, Font font)
    {
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width -= font.GetCharMetrics(rune, UIScale)?.Advance ?? 0;
            if (width < 0)
                break;
            index += rune.Utf16SequenceLength;
        }
        return index;
    }
}
