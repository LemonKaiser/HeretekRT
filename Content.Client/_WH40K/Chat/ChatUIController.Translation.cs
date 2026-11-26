using System.Linq;
using Content.Shared._WH40K.Chat;
using Content.Shared.Chat;
using Content.Shared.Localizations;
using Robust.Shared.Network;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    internal const int PendingTranslationLimit = 256;
    private readonly OrderedDictionary<ulong, ChatMessage> _pendingTranslations = new();

    private void InitializeChatTranslation()
    {
        _net.RegisterNetMessage<MsgChatTranslation>(OnChatTranslation);
        _net.Disconnect += OnTranslationDisconnected;
    }

    private void TrackTranslation(MsgChatMessage packet)
    {
        if (packet.TranslationId == 0 || packet.Message.HideChat || _pendingTranslations.ContainsKey(packet.TranslationId))
            return;

        if (_pendingTranslations.Count == PendingTranslationLimit)
        {
            _pendingTranslations.GetAt(0).Value.TranslationId = 0;
            _pendingTranslations.RemoveAt(0);
        }

        packet.Message.TranslationId = packet.TranslationId;
        _pendingTranslations.Add(packet.TranslationId, packet.Message);
    }

    private void OnChatTranslation(MsgChatTranslation packet)
    {
        if (!_pendingTranslations.Remove(packet.Id, out var message))
            return;

        message.TranslationId = 0;
        var culture = ContentLocalizationManager.ValidateCultureName(_loc.DefaultCulture?.Name);
        if (culture != packet.Culture || packet.Text.Length is 0 or > MsgChatTranslation.MaxTextLength ||
            string.IsNullOrWhiteSpace(packet.Text) || packet.Text.Any(char.IsControl))
            return;

        message.Translation = packet.Text;
        message.TranslationCulture = packet.Culture;
        foreach (var chat in _chats)
            chat.QueueTranslationRefresh(packet.Id);
    }

    private void OnTranslationDisconnected(object? sender, NetDisconnectedArgs args)
    {
        foreach (var message in _pendingTranslations.Values)
            message.TranslationId = 0;

        _pendingTranslations.Clear();
        foreach (var chat in _chats)
            chat.ResetTranslationRows();
    }

    private void RemoveDeletedTranslations(MsgDeleteChatMessagesBy packet)
    {
        for (var i = _pendingTranslations.Count - 1; i >= 0; i--)
        {
            var message = _pendingTranslations.GetAt(i).Value;
            if (message.SenderKey == packet.Key || packet.Entities.Contains(message.SenderEntity))
            {
                message.TranslationId = 0;
                _pendingTranslations.RemoveAt(i);
            }
        }
    }
}
