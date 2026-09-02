using System.Text;
using Robust.Shared.Utility;

namespace Content.Shared.Chat;

public enum ChatEmojiCategory : byte
{
    Custom,
    Smileys,
    Nature,
    Food,
    Activities,
    Travel,
    Objects,
    Symbols,
    Flags,
}

/// <summary>
/// A single emoji that can be inserted into chat. The alias is the canonical wire format:
/// <c>:alias:</c>. This keeps player messages independent of client font and platform emoji support.
/// </summary>
public readonly record struct ChatEmojiDefinition(
    string Alias,
    string Value,
    ChatEmojiCategory Category,
    ResPath? RsiPath = null,
    string? RsiState = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Keywords = null)
{
    public bool HasDirectValue => !string.IsNullOrEmpty(Value);
    public string InsertText => $":{Alias}:";
    public ResPath TexturePath => RsiPath ?? ChatEmoji.DefaultEmojiRsiPath;
    public string TextureState => string.IsNullOrWhiteSpace(RsiState) ? Alias : RsiState;
}

/// <summary>
/// Shared parsing and policy helpers. The actual catalogue is supplied by <see cref="ChatEmojiCatalog"/>.
/// </summary>
public static class ChatEmoji
{
    public const string DefaultAllowedChannelsCVar = "Local,LOOC,OOC,Emotes,CollectiveMind,Dead,Admin";
    public static readonly ResPath DefaultEmojiRsiPath = new("/Textures/Interface/Chat/emoji.rsi");

    /// <summary>
    /// Channels intended for regular player conversation. Console commands, radio and whisper use compact
    /// technical input formats, so the picker and emoji rendering deliberately stay disabled for them.
    /// </summary>
    public const ChatSelectChannel DefaultAllowedChannels =
        ChatSelectChannel.Local |
        ChatSelectChannel.LOOC |
        ChatSelectChannel.OOC |
        ChatSelectChannel.Emotes |
        ChatSelectChannel.CollectiveMind |
        ChatSelectChannel.Dead |
        ChatSelectChannel.Admin;

    public const ChatSelectChannel AllUserChannels = DefaultAllowedChannels;

    /// <summary>
    /// Parses a server CVar. The <c>all</c> value covers every regular player-facing channel without
    /// accidentally enabling emoji in console, radio or whisper input.
    /// </summary>
    public static ChatSelectChannel ParseAllowedChannels(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultAllowedChannels;

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase) || trimmed == "*")
            return AllUserChannels;
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
            return ChatSelectChannel.None;

        var result = ChatSelectChannel.None;
        var recognized = false;
        foreach (var token in trimmed.Split([',', ';', '|', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase) || token == "*")
                return AllUserChannels;
            if (string.Equals(token, "none", StringComparison.OrdinalIgnoreCase))
            {
                recognized = true;
                continue;
            }

            if (!Enum.TryParse<ChatSelectChannel>(token, true, out var channel))
                continue;

            recognized = true;
            result |= channel & AllUserChannels;
        }

        return recognized ? result : DefaultAllowedChannels;
    }

    public static bool IsAllowed(ChatSelectChannel allowedChannels, ChatSelectChannel channel)
        => channel != ChatSelectChannel.None && channel != ChatSelectChannel.Console && (allowedChannels & channel) != 0;

    public static bool IsAllowed(ChatSelectChannel allowedChannels, ChatChannel channel)
        => TryMapChannel(channel, out var selected) && IsAllowed(allowedChannels, selected);

    /// <summary>
    /// Normalizes permitted emoji into shortcodes, strips direct emoji from disallowed channels and limits
    /// the amount that a message can turn into UI controls. A skin-tone modifier is consumed together with
    /// its base emoji and deliberately resolves to the neutral sprite.
    /// </summary>
    public static string ApplyPolicy(
        string text,
        ChatSelectChannel channel,
        ChatSelectChannel allowedChannels,
        ChatEmojiCatalog catalog,
        int maxEmoji)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var allowed = IsAllowed(allowedChannels, channel);
        var limit = Math.Max(0, maxEmoji);
        var builder = new StringBuilder(text.Length + 16);
        var plainStart = 0;
        var index = 0;
        var used = 0;
        var changed = false;

        while (index < text.Length)
        {
            if (TryReadAlias(text, index, out var alias, out var aliasLength) && catalog.TryGet(alias, out var aliasEmoji))
            {
                builder.Append(text, plainStart, index - plainStart);
                if (allowed && used < limit)
                {
                    builder.Append(aliasEmoji.InsertText);
                    used++;
                    changed |= !text.AsSpan(index, aliasLength).SequenceEqual(aliasEmoji.InsertText.AsSpan());
                }
                else
                {
                    builder.Append(text, index, aliasLength);
                }

                index += aliasLength;
                plainStart = index;
                continue;
            }

            if (catalog.TryMatchDirectEmoji(text, index, out var directEmoji, out var directLength))
            {
                builder.Append(text, plainStart, index - plainStart);
                if (allowed && used < limit)
                {
                    builder.Append(directEmoji.InsertText);
                    used++;
                }
                else if (allowed)
                {
                    builder.Append(text, index, directLength);
                }

                // Direct emoji are removed in disabled channels. Their optional tone modifier is included
                // in directLength, so it cannot remain as a stray coloured glyph.
                changed = true;
                index += directLength;
                plainStart = index;
                continue;
            }

            index += char.IsSurrogatePair(text, index) ? 2 : 1;
        }

        if (!changed)
            return text;

        builder.Append(text, plainStart, text.Length - plainStart);
        return builder.ToString();
    }

    public static bool TryMapChannel(ChatChannel channel, out ChatSelectChannel selected)
    {
        selected = channel switch
        {
            ChatChannel.Local => ChatSelectChannel.Local,
            ChatChannel.Whisper => ChatSelectChannel.Whisper,
            ChatChannel.Radio => ChatSelectChannel.Radio,
            ChatChannel.LOOC => ChatSelectChannel.LOOC,
            ChatChannel.OOC => ChatSelectChannel.OOC,
            ChatChannel.Emotes => ChatSelectChannel.Emotes,
            ChatChannel.CollectiveMind => ChatSelectChannel.CollectiveMind,
            ChatChannel.Dead => ChatSelectChannel.Dead,
            ChatChannel.Admin or ChatChannel.AdminAlert or ChatChannel.AdminChat => ChatSelectChannel.Admin,
            _ => ChatSelectChannel.None,
        };
        return selected != ChatSelectChannel.None;
    }

    public static bool TryReadAlias(string text, int index, out string alias, out int consumedLength)
    {
        alias = string.Empty;
        consumedLength = 0;
        if (index < 0 || index >= text.Length || text[index] != ':')
            return false;

        var end = text.IndexOf(':', index + 1);
        if (end <= index + 1)
            return false;

        var value = text.AsSpan(index + 1, end - index - 1);
        if (!IsValidAlias(value))
            return false;

        alias = value.ToString();
        consumedLength = end - index + 1;
        return true;
    }

    public static bool IsValidAlias(ReadOnlySpan<char> alias)
    {
        if (alias.Length == 0)
            return false;

        foreach (var character in alias)
        {
            if (character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '+' or '-'))
                return false;
        }

        return true;
    }

    public static string DecodeValue(string codePoints)
    {
        var value = new StringBuilder(codePoints.Length);

        foreach (var codePointText in codePoints.Split('-'))
        {
            var codePoint = 0;
            foreach (var digit in codePointText)
            {
                var digitValue = digit switch
                {
                    >= '0' and <= '9' => digit - '0',
                    >= 'a' and <= 'f' => digit - 'a' + 10,
                    >= 'A' and <= 'F' => digit - 'A' + 10,
                    _ => -1,
                };

                if (digitValue < 0)
                    return string.Empty;

                codePoint = (codePoint << 4) + digitValue;
            }

            value.Append(char.ConvertFromUtf32(codePoint));
        }

        return value.ToString();
    }

    public static string GetCategoryIconAlias(ChatEmojiCategory category) => category switch
    {
        ChatEmojiCategory.Smileys => "grinning",
        ChatEmojiCategory.Nature => "herb",
        ChatEmojiCategory.Food => "coffee",
        ChatEmojiCategory.Activities => "video_game",
        ChatEmojiCategory.Travel => "bicycle",
        ChatEmojiCategory.Objects => "hammer_and_wrench",
        ChatEmojiCategory.Symbols => "heart",
        ChatEmojiCategory.Flags => "triangular_flag_on_post",
        _ => "question",
    };
}
