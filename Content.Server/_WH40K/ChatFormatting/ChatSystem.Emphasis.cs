using System.Text.RegularExpressions;

namespace Content.Server.Chat.Systems;

public partial class ChatSystem
{
    private static readonly Regex Wh40kEmphasisPattern = new(
        @"~([|+^])|([|+^])((?>~[|+^]|(?!\2).)+?)\2",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Converts |italic|, +bold+ and ^small^ delimiters to BBCode; returns input unchanged on regex timeout.</summary>
    public static string ConvertCustomEmphasisToBBCode(string message)
    {
        try
        {
            return ParseEmphasis(message);
        }
        catch (RegexMatchTimeoutException)
        {
            return message;
        }
    }

    private static string ParseEmphasis(string input)
    {
        return Wh40kEmphasisPattern.Replace(input, m =>
        {
            if (m.Groups[1].Success)
                return m.Groups[1].Value;

            var ch = m.Groups[2].Value[0];
            var tag = ch switch
            {
                '|' => "italic",
                '+' => "bold",
                '^' => "small",
                _ => null
            };

            var content = ParseEmphasis(m.Groups[3].Value);

            if (ch == '^')
                return $"[font size=8]{content}[/font]";

            return $"[{tag}]{content}[/{tag}]";
        });
    }
}
