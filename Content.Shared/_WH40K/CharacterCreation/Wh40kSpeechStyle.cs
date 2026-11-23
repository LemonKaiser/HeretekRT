using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.CharacterCreation;

[DataDefinition, Serializable, NetSerializable]
public readonly partial record struct Wh40kSpeechStyle
{
    [DataField] public string? Font { get; init; }
    [DataField] public string? TypingBubble { get; init; }

    public Wh40kSpeechStyle(string? font, string? typingBubble)
    {
        Font = font;
        TypingBubble = typingBubble;
    }

    public static IReadOnlyList<string> Fonts { get; } =
    [
        "Wh40kAlegreya",
        "Wh40kBellota",
        "Wh40kBrygada1918",
        "Wh40kCormorantGaramond",
        "Wh40kExo2",
        "Wh40kJetBrainsMono",
        "Wh40kShantellSans",
        "Wh40kTiny5Mono",
        "Wh40kYsabeau",
    ];

    public static IReadOnlyList<string> Bubbles { get; } =
    [
        "robot", "xenoborg", "alien", "guardian", "holo", "lawyer", "moth", "spider", "vox", "lizard", "slime",
        "gingerbread", "diona", "felinid", "rodentia", "Chitinid", "feroxi", "resomi", "goblin", "pal", "hydrakin",
        "Wh40kVulpkanin",
    ];

    public string? GetFont(bool bold, bool italic)
    {
        if (Font is not { } family)
            return null;

        return (bold, italic) switch
        {
            (true, true) => family + "BoldItalic",
            (true, false) => family + "Bold",
            (false, true) => family + "Italic",
            _ => family,
        };
    }

    public Wh40kSpeechStyle Validated() => new(
        Font == "Monospace" ? "Wh40kJetBrainsMono" : Font != null && Fonts.Contains(Font) ? Font : null,
        TypingBubble != null && Bubbles.Contains(TypingBubble) ? TypingBubble : null);
}
