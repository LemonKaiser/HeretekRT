using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Dialogue.UI;

/// <summary>
/// Keeps the complete line's RichTextLabel layout while revealing its glyphs.
/// Reveal updates can run after the UI layout pass without replacing that layout.
/// </summary>
public sealed class DialogueTypewriterLabel : RichTextLabel
{
    private readonly RevealFont _font;
    private Vector2? _measureSize;
    private string? _layoutText;
    private Color _textColor;

    public DialogueTypewriterLabel()
    {
        _font = new RevealFont(UserInterfaceManager.ThemeDefaults.DefaultFont);
        Stylesheet = new Stylesheet(new[]
        {
            new StyleRule(new SelectorElement(typeof(RichTextLabel), null, null, null), new[]
            {
                new StyleProperty("font", _font)
            })
        });
    }

    public void SetFont(Font font)
    {
        _font.Source = font;
        InvalidateMeasure();
    }

    public void SetRevealedText(string layoutText, int visibleCharacters, Color color)
    {
        if (_layoutText != layoutText || _textColor != color)
        {
            _layoutText = layoutText;
            _textColor = color;
            SetMessage(layoutText, color);
        }

        // This only changes drawing. Replacing the rich-text message here
        // would discard its line breaks after UI controllers finish the layout tick.
        _font.VisibleCharacters = Math.Clamp(visibleCharacters, 0, layoutText.Length);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _measureSize = availableSize;
        return base.MeasureOverride(availableSize);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (_measureSize is not { } measureSize)
            return;

        // A new line or typography may also arrive after layout. Reuse the
        // actual last constraint, including the scroll viewport's reserved width.
        if (!IsMeasureValid)
            Measure(measureSize);

        _font.BeginDraw();
        base.Draw(handle);
    }

    private sealed class RevealFont(Font source) : Font
    {
        public Font Source { get; set; } = source;
        public int VisibleCharacters { get; set; }
        private int _drawnCharacters;

        public void BeginDraw() => _drawnCharacters = 0;

        public override int GetAscent(float scale) => Source.GetAscent(scale);
        public override int GetHeight(float scale) => Source.GetHeight(scale);
        public override int GetDescent(float scale) => Source.GetDescent(scale);
        public override int GetLineHeight(float scale) => Source.GetLineHeight(scale);
        public override CharMetrics? GetCharMetrics(Rune rune, float scale, bool fallback = true)
            => Source.GetCharMetrics(rune, scale, fallback);

        public override float DrawChar(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale, Color color, bool fallback = true)
        {
            _drawnCharacters += rune.Utf16SequenceLength;
            return _drawnCharacters <= VisibleCharacters
                ? Source.DrawChar(handle, rune, baseline, scale, color, fallback)
                : Source.GetCharMetrics(rune, scale, fallback)?.Advance ?? 0;
        }
    }
}
