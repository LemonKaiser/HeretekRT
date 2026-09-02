using System.Numerics;
using System.Text;
using Content.Shared.Chat;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

/// <summary>
/// Converts only server-normalized emoji shortcodes into inline controls. Direct Unicode is intentionally not
/// parsed here: player input is normalized on the server, while arbitrary text from names and system messages
/// remains text and cannot allocate an unbounded number of controls.
/// </summary>
public static class ChatEmojiRichText
{
    public const string EmojiMarkupTag = "chatemoji";

    private const float InlineEmojiScale = 20f / 72f;
    private const float PickerEmojiScale = 24f / 72f;
    private const float CategoryEmojiScale = 22f / 72f;

    public static FormattedMessage BuildChatLine(
        string markup,
        Color color,
        bool allowEmojiMarkup,
        ChatEmojiCatalog catalog,
        int maxEmoji)
    {
        var parsed = FormattedMessage.FromMarkupOrThrow(markup);
        var emojis = ReplaceEmojiText(parsed, allowEmojiMarkup, catalog, maxEmoji);
        var result = new FormattedMessage(emojis.Count + 2);
        result.PushColor(color);
        result.AddMessage(emojis);
        result.Pop();
        return result;
    }

    public static FormattedMessage ReplaceEmojiText(
        FormattedMessage source,
        bool allowEmojiMarkup,
        ChatEmojiCatalog catalog,
        int maxEmoji)
    {
        if (!allowEmojiMarkup || maxEmoji <= 0)
            return source;

        var builder = new StringBuilder(source.ToMarkup().Length + 32);
        var remaining = maxEmoji;
        var protectedTags = new Stack<string>();
        foreach (var node in source)
        {
            if (node.Name == null && node.Value.TryGetString(out var text) && !string.IsNullOrEmpty(text))
            {
                if (protectedTags.Count == 0)
                    AppendTextWithEmojiMarkup(builder, text, catalog, ref remaining);
                else
                    builder.Append(FormattedMessage.EscapeText(text));
                continue;
            }

            builder.Append(node);
            if (node.Name is not ("Name" or "BubbleHeader"))
                continue;

            if (!node.Closing)
                protectedTags.Push(node.Name);
            else if (protectedTags.TryPeek(out var current) && current == node.Name)
                protectedTags.Pop();
        }

        return builder.Length == 0 ? FormattedMessage.Empty : FormattedMessage.FromMarkupOrThrow(builder.ToString());
    }

    public static TextureRect CreateInlineTextureRect(IResourceCache resources, ChatEmojiDefinition emoji)
        => CreateTextureRect(resources, emoji, InlineEmojiScale, 25f, new Thickness(1f, 2f, 1f, 2f));

    public static TextureRect CreatePickerTextureRect(IResourceCache resources, ChatEmojiDefinition emoji)
        => CreateTextureRect(resources, emoji, PickerEmojiScale, 28f, new Thickness(5f));

    public static TextureRect CreateCategoryTextureRect(IResourceCache resources, ChatEmojiDefinition emoji)
        => CreateTextureRect(resources, emoji, CategoryEmojiScale, 22f, new Thickness(1f));

    public static FormattedMessage BuildPreviewMessage(ChatEmojiDefinition emoji)
    {
        var name = emoji.DisplayName ?? emoji.Alias.Replace('_', ' ');
        return FormattedMessage.FromMarkupOrThrow(
            $"[{EmojiMarkupTag} alias=\"{emoji.Alias}\"/] {FormattedMessage.EscapeText(name)} {FormattedMessage.EscapeText(emoji.InsertText)}");
    }

    private static TextureRect CreateTextureRect(IResourceCache resources, ChatEmojiDefinition emoji, float scale, float minSize, Thickness margin)
    {
        return new TextureRect
        {
            Texture = ResolveTexture(resources, emoji),
            TextureScale = new Vector2(scale, scale),
            Stretch = TextureRect.StretchMode.KeepCentered,
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
            CanShrink = true,
            MinSize = new Vector2(minSize, minSize),
            Margin = margin,
        };
    }

    private static Texture ResolveTexture(IResourceCache resources, ChatEmojiDefinition emoji)
    {
        if (resources.TryGetResource<RSIResource>(emoji.TexturePath, out var rsi) &&
            rsi.RSI.TryGetState(emoji.TextureState, out var state))
        {
            return state.Frame0;
        }

        return resources.GetFallback<TextureResource>().Texture;
    }

    private static void AppendTextWithEmojiMarkup(StringBuilder builder, string text, ChatEmojiCatalog catalog, ref int remaining)
    {
        var plainStart = 0;
        var index = 0;
        while (index < text.Length && remaining > 0)
        {
            if (!ChatEmoji.TryReadAlias(text, index, out var alias, out var aliasLength) || !catalog.TryGet(alias, out var emoji))
            {
                index += char.IsSurrogatePair(text, index) ? 2 : 1;
                continue;
            }

            AppendEscapedPlainText(builder, text, plainStart, index);
            AppendEmojiTag(builder, emoji.Alias);
            remaining--;
            index += aliasLength;
            plainStart = index;
        }

        AppendEscapedPlainText(builder, text, plainStart, text.Length);
    }

    private static void AppendEscapedPlainText(StringBuilder builder, string text, int start, int end)
    {
        if (end > start)
            builder.Append(FormattedMessage.EscapeText(text.Substring(start, end - start)));
    }

    private static void AppendEmojiTag(StringBuilder builder, string alias)
        => builder.Append('[').Append(EmojiMarkupTag).Append(" alias=\"").Append(alias).Append("\"/]");
}
