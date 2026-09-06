using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Shared.Chat;

/// <summary>
/// Single source of truth for the replicated emoji channel policy and per-message rendering cap.
/// Both server message paths and client presentation use this service instead of parsing the CVar independently.
/// </summary>
public sealed partial class ChatEmojiPolicy
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private ChatEmojiCatalog _catalog = default!;

    private ChatSelectChannel _allowedChannels = ChatEmoji.DefaultAllowedChannels;
    private int _maxPerMessage;

    public event Action? Changed;

    public int MaxPerMessage => _maxPerMessage;

    public void Initialize()
    {
        _configuration.OnValueChanged(CCVars.ChatEmojiAllowedChannels, OnAllowedChannelsChanged, true);
        _configuration.OnValueChanged(CCVars.ChatEmojiMaxPerMessage, OnMaxPerMessageChanged, true);
    }

    public bool IsAllowed(ChatSelectChannel channel)
        => ChatEmoji.IsAllowed(_allowedChannels, channel);

    public bool IsAllowed(ChatChannel channel)
        => ChatEmoji.IsAllowed(_allowedChannels, channel);

    public string Apply(string message, ChatSelectChannel channel)
        => ChatEmoji.ApplyPolicy(message, channel, _allowedChannels, _catalog, _maxPerMessage);

    private void OnAllowedChannelsChanged(string raw)
    {
        var parsed = ChatEmoji.ParseAllowedChannels(raw);
        if (_allowedChannels == parsed)
            return;

        _allowedChannels = parsed;
        Changed?.Invoke();
    }

    private void OnMaxPerMessageChanged(int value)
    {
        var clamped = Math.Max(0, value);
        if (_maxPerMessage == clamped)
            return;

        _maxPerMessage = clamped;
        Changed?.Invoke();
    }
}
