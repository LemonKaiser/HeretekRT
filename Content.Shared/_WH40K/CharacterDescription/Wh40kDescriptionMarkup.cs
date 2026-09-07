using System.Text.RegularExpressions;

namespace Content.Shared._WH40K.CharacterDescription;

/// <summary>
/// Keeps only the lightweight rich-text tags supported by character descriptions.
/// </summary>
public static class Wh40kDescriptionMarkup
{
    private static readonly Regex MarkupTagRegex = new(
        @"(?<!\\)\[/?(?<tag>[a-zA-Z][a-zA-Z0-9-]*)(?:=[^\]\r\n]*)?/?\]",
        RegexOptions.Compiled);

    private static readonly string[] BasicMarkupTags =
    [
        "bolditalic",
        "bold",
        "bullet",
        "color",
        "head",
        "italic",
        "mono",
    ];

    public static string SanitizeBasic(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return MarkupTagRegex.Replace(text, match =>
        {
            var tag = match.Groups["tag"].Value;
            foreach (var allowedTag in BasicMarkupTags)
            {
                if (string.Equals(tag, allowedTag, StringComparison.OrdinalIgnoreCase))
                    return match.Value;
            }

            return string.Empty;
        });
    }
}
