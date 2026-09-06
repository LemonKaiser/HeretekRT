using System.Diagnostics.CodeAnalysis;
using Content.Shared.Chat;
using JetBrains.Annotations;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

[UsedImplicitly]
public sealed partial class ChatEmojiTag : IMarkupTagHandler
{
    private const string TypewriterHiddenAttribute = "typewriterhidden";

    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private ChatEmojiCatalog _emojiCatalog = default!;

    public string Name => ChatEmojiRichText.EmojiMarkupTag;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!node.Attributes.TryGetValue("alias", out var aliasParameter) ||
            !aliasParameter.TryGetString(out var alias) ||
            !_emojiCatalog.TryGet(alias, out var emoji))
        {
            return false;
        }

        control = ChatEmojiRichText.CreateInlineTextureRect(_resourceCache, emoji);
        if (node.Attributes.TryGetValue(TypewriterHiddenAttribute, out var hidden) &&
            hidden.TryGetString(out var value) &&
            bool.TryParse(value, out var isHidden) &&
            isHidden)
        {
            control.ModulateSelfOverride = Color.Transparent;
        }

        return true;
    }
}
