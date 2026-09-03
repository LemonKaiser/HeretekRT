using System.Globalization;
using System.Text;

namespace Content.Shared._Forge.Text;

/// <summary>
///     Splits text into visible Unicode elements without relying on globalization APIs that are unavailable
///     to sandboxed content assemblies.
/// </summary>
public static class UnicodeTextElements
{
    private const int CarriageReturn = 0x000D;
    private const int LineFeed = 0x000A;
    private const int ZeroWidthJoiner = 0x200D;

    public static IEnumerable<string> Enumerate(string text)
    {
        var start = 0;
        var position = 0;
        var hasElement = false;
        var regionalIndicatorCount = 0;
        Rune previous = default;

        foreach (var rune in text.EnumerateRunes())
        {
            if (hasElement && !ShouldJoin(previous, rune, regionalIndicatorCount))
            {
                yield return text.Substring(start, position - start);
                start = position;
                regionalIndicatorCount = 0;
            }

            if (IsRegionalIndicator(rune))
            {
                regionalIndicatorCount++;
            }
            else if (!IsExtend(rune) && rune.Value != ZeroWidthJoiner)
            {
                regionalIndicatorCount = 0;
            }

            previous = rune;
            position += rune.Utf16SequenceLength;
            hasElement = true;
        }

        if (hasElement)
            yield return text.Substring(start, position - start);
    }

    private static bool ShouldJoin(Rune previous, Rune next, int regionalIndicatorCount)
    {
        return previous.Value == CarriageReturn && next.Value == LineFeed ||
               previous.Value == ZeroWidthJoiner ||
               next.Value == ZeroWidthJoiner ||
               IsExtend(next) ||
               IsRegionalIndicator(next) && regionalIndicatorCount % 2 == 1;
    }

    private static bool IsExtend(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark ||
               rune.Value is >= 0x1F3FB and <= 0x1F3FF || // Emoji skin tone modifier.
               rune.Value is >= 0xE0020 and <= 0xE007F; // Emoji tag sequence.
    }

    private static bool IsRegionalIndicator(Rune rune)
        => rune.Value is >= 0x1F1E6 and <= 0x1F1FF;
}
