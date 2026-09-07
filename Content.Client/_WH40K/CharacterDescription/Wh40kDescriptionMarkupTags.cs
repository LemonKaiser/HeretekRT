using Content.Client.UserInterface.RichText;
using Robust.Client.UserInterface.RichText;

namespace Content.Client._WH40K.CharacterDescription;

public static class Wh40kDescriptionMarkupTags
{
    public static readonly Type[] Basic =
    [
        typeof(BoldItalicTag),
        typeof(BoldTag),
        typeof(BulletTag),
        typeof(ColorTag),
        typeof(HeadingTag),
        typeof(ItalicTag),
        typeof(MonoTag),
    ];
}
