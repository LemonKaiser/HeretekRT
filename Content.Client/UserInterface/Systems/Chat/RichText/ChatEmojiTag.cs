using Content.Shared.Chat;
using JetBrains.Annotations;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

[UsedImplicitly]
public sealed partial class ChatEmojiTag : IMarkupTagHandler
{
    private const string TypewriterHiddenAttribute = "typewriterhidden";

    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly ChatEmojiCatalog _emojiCatalog = default!;

    public string Name => ChatEmojiRichText.EmojiMarkupTag;

    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        var font = context.Font.Peek();
        if (TryGetEmoji(node, out var emoji))
        {
            var hide = node.Attributes.TryGetValue(TypewriterHiddenAttribute, out var hidden) &&
                       hidden.TryGetString(out var value) &&
                       bool.TryParse(value, out var isHidden) &&
                       isHidden;
            font = new ChatImageFont(font, ChatEmojiRichText.ResolveTexture(_resourceCache, emoji), hide);
        }

        context.Font.Push(font);
    }

    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context) => context.Font.Pop();

    public string TextBefore(MarkupNode node) => TryGetEmoji(node, out _) ? ChatImageFont.Glyph : string.Empty;

    private bool TryGetEmoji(MarkupNode node, out ChatEmojiDefinition emoji)
    {
        emoji = default;
        return node.Attributes.TryGetValue("alias", out var aliasParameter) &&
               aliasParameter.TryGetString(out var alias) && _emojiCatalog.TryGet(alias, out emoji);
    }
}
