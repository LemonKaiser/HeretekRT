using System.Globalization;
using System.Text;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    public const int MaxChatTabs = 10;
    public const int MaxChatTabNameLength = 24;
    public const ChatChannel AllChatChannels = (ChatChannel) uint.MaxValue;

    private const ChatChannel BaseConfigurableChannels =
        ChatChannel.Local |
        ChatChannel.Whisper |
        ChatChannel.Emotes |
        ChatChannel.Radio |
        ChatChannel.CollectiveMind |
        ChatChannel.Notifications |
        ChatChannel.LOOC |
        ChatChannel.OOC |
        ChatChannel.Dead |
        ChatChannel.Server;

    private readonly List<ChatTabEntry> _tabs = new();
    private bool _tabsInitialized;

    public int ActiveTabIndex { get; private set; }

    public bool CanAddTab
    {
        get
        {
            EnsureTabsInitialized();
            return _tabs.Count < MaxChatTabs;
        }
    }

    public bool CanDeleteTab
    {
        get
        {
            EnsureTabsInitialized();
            return _tabs.Count > 1;
        }
    }

    public ChatTabSnapshot ActiveTab
    {
        get
        {
            EnsureTabsInitialized();
            return ToSnapshot(_tabs[ActiveTabIndex]);
        }
    }

    public event Action? TabsChanged;
    public event Action? TabUnreadChanged;

    private void EnsureTabsInitialized()
    {
        if (_tabsInitialized)
            return;

        _tabsInitialized = true;
        var raw = _config.GetCVar(CCVars.ChatTabs);
        if (!string.IsNullOrWhiteSpace(raw))
            DeserializeTabs(raw, _tabs);

        if (_tabs.Count == 0)
        {
            _tabs.Add(new ChatTabEntry(_loc.GetString("hud-chatbox-tab-all"), AllChatChannels));
            _tabs.Add(new ChatTabEntry(_loc.GetString("hud-chatbox-tab-local"), ChatChannel.Local | ChatChannel.Whisper | ChatChannel.Emotes));
            _tabs.Add(new ChatTabEntry(_loc.GetString("hud-chatbox-tab-radio"), ChatChannel.Radio | ChatChannel.CollectiveMind));
            _tabs.Add(new ChatTabEntry(_loc.GetString("hud-chatbox-tab-ooc"), ChatChannel.OOC | ChatChannel.LOOC));
        }

        ActiveTabIndex = 0;
    }

    public IReadOnlyList<ChatTabSnapshot> GetTabs()
    {
        EnsureTabsInitialized();
        var result = new ChatTabSnapshot[_tabs.Count];
        for (var i = 0; i < _tabs.Count; i++)
        {
            result[i] = ToSnapshot(_tabs[i]);
        }

        return result;
    }

    public bool IsChannelVisibleInActiveTab(ChatChannel channel)
    {
        EnsureTabsInitialized();
        return (_tabs[ActiveTabIndex].Channels & channel) != 0;
    }

    public ChatChannel GetConfigurableTabChannels()
    {
        var channels = BaseConfigurableChannels;
        if (_admin.HasFlag(AdminFlags.Adminchat))
            channels |= ChatChannel.AdminRelated;

        return channels;
    }

    public void SelectTab(int index)
    {
        EnsureTabsInitialized();
        if (index < 0 || index >= _tabs.Count || index == ActiveTabIndex)
            return;

        ActiveTabIndex = index;
        ClearReadChannels(_tabs[ActiveTabIndex].Channels);
        TabsChanged?.Invoke();
    }

    public bool TryAddTab(string name, ChatChannel channels)
    {
        EnsureTabsInitialized();
        if (_tabs.Count >= MaxChatTabs)
            return false;

        var normalized = NormalizeTabName(name);
        if (normalized.Length == 0)
            return false;

        _tabs.Add(new ChatTabEntry(normalized, channels));
        ActiveTabIndex = _tabs.Count - 1;
        ClearReadChannels(channels);
        SaveTabs();
        TabsChanged?.Invoke();
        return true;
    }

    public bool TryUpdateTab(int index, string name, ChatChannel channels)
    {
        EnsureTabsInitialized();
        if (index < 0 || index >= _tabs.Count)
            return false;

        var normalized = NormalizeTabName(name);
        if (normalized.Length == 0)
            return false;

        var entry = _tabs[index];
        entry.Name = normalized;
        entry.Channels = channels;
        entry.UnreadChannels &= channels;

        if (index == ActiveTabIndex)
            ClearReadChannels(channels);

        SaveTabs();
        TabsChanged?.Invoke();
        return true;
    }

    public bool TryRemoveTab(int index)
    {
        EnsureTabsInitialized();
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count)
            return false;

        _tabs.RemoveAt(index);
        if (index < ActiveTabIndex)
            ActiveTabIndex--;
        ActiveTabIndex = Math.Clamp(ActiveTabIndex, 0, _tabs.Count - 1);

        ClearReadChannels(_tabs[ActiveTabIndex].Channels);
        SaveTabs();
        TabsChanged?.Invoke();
        return true;
    }

    public void SetActiveTabChannel(ChatChannel channel, bool active)
    {
        EnsureTabsInitialized();
        var entry = _tabs[ActiveTabIndex];
        var nextChannels = active ? entry.Channels | channel : entry.Channels & ~channel;
        if (entry.Channels == nextChannels)
            return;

        entry.Channels = nextChannels;
        entry.UnreadChannels &= nextChannels;
        if (active)
            ClearReadChannels(channel);

        SaveTabs();
        TabsChanged?.Invoke();
    }

    private void ClearReadChannels(ChatChannel visibleChannels)
    {
        foreach (var tab in _tabs)
        {
            tab.UnreadChannels &= ~visibleChannels;
        }

        ClearUnfilteredUnreads(visibleChannels);
    }

    private void NotifyTabsOfMessage(ChatMessage msg)
    {
        EnsureTabsInitialized();
        if (IsChannelVisibleInActiveTab(msg.Channel))
            return;

        var changed = false;
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (i == ActiveTabIndex)
                continue;

            var tab = _tabs[i];
            if ((tab.Channels & msg.Channel) == 0 || (tab.UnreadChannels & msg.Channel) != 0)
                continue;

            tab.UnreadChannels |= msg.Channel;
            changed = true;
        }

        if (changed)
            TabUnreadChanged?.Invoke();
    }

    private void SaveTabs()
    {
        _config.SetCVar(CCVars.ChatTabs, SerializeTabs(_tabs));
        _config.SaveToFile();
    }

    private static ChatTabSnapshot ToSnapshot(ChatTabEntry entry)
        => new(entry.Name, entry.Channels, entry.UnreadChannels != ChatChannel.None);

    public static string NormalizeTabName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        return LimitTabNameLength(trimmed).TrimEnd();
    }

    public static string LimitTabNameLength(string name)
    {
        if (name.Length <= MaxChatTabNameLength)
            return name;

        var length = MaxChatTabNameLength;
        if (char.IsSurrogatePair(name, length - 1))
            length--;

        return name[..length];
    }

    private static string SerializeTabs(List<ChatTabEntry> tabs)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < tabs.Count; i++)
        {
            if (i > 0)
                sb.Append(';');

            AppendEscaped(sb, tabs[i].Name);
            sb.Append('|');
            sb.Append(((uint) tabs[i].Channels).ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static void AppendEscaped(StringBuilder sb, string value)
    {
        foreach (var ch in value)
        {
            if (ch is '\r' or '\n')
                continue;

            if (ch is '\\' or '|' or ';')
                sb.Append('\\');

            sb.Append(ch);
        }
    }

    private static void DeserializeTabs(string raw, List<ChatTabEntry> target)
    {
        var nameBuilder = new StringBuilder();
        var maskBuilder = new StringBuilder();
        var readingMask = false;
        var escaped = false;

        void FlushToken()
        {
            if (target.Count >= MaxChatTabs)
            {
                nameBuilder.Clear();
                maskBuilder.Clear();
                readingMask = false;
                return;
            }

            if (readingMask &&
                uint.TryParse(maskBuilder.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var mask))
            {
                var name = NormalizeTabName(nameBuilder.ToString());
                if (name.Length > 0)
                    target.Add(new ChatTabEntry(name, (ChatChannel) mask));
            }

            nameBuilder.Clear();
            maskBuilder.Clear();
            readingMask = false;
        }

        foreach (var ch in raw)
        {
            if (escaped)
            {
                (readingMask ? maskBuilder : nameBuilder).Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == ';' && !readingMask)
            {
                nameBuilder.Clear();
                continue;
            }

            if (ch == ';')
            {
                FlushToken();
                if (target.Count >= MaxChatTabs)
                    break;
                continue;
            }

            if (ch == '|' && !readingMask)
            {
                readingMask = true;
                continue;
            }

            (readingMask ? maskBuilder : nameBuilder).Append(ch);
        }

        if (target.Count < MaxChatTabs)
            FlushToken();
    }

    public readonly record struct ChatTabSnapshot(string Name, ChatChannel Channels, bool HasUnread);

    private sealed class ChatTabEntry
    {
        public string Name;
        public ChatChannel Channels;
        public ChatChannel UnreadChannels;

        public ChatTabEntry(string name, ChatChannel channels)
        {
            Name = name;
            Channels = channels;
        }
    }
}
