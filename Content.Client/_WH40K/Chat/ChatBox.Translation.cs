using System.Linq;
using Content.Shared.Chat;
using Content.Shared.Localizations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.Widgets;

public partial class ChatBox
{
    private readonly OrderedDictionary<ulong, TranslationRow> _translationRows = new();
    private readonly HashSet<ulong> _readyTranslations = new();
    private VScrollBar? _translationScrollBar;

    internal void QueueTranslationRefresh(ulong id)
    {
        if (_translationRows.ContainsKey(id))
            _readyTranslations.Add(id);
    }

    internal void ResetTranslationRows()
    {
        _translationRows.Clear();
        _readyTranslations.Clear();
    }

    private void AppendTranslatableLine(ChatMessage message, Color color)
    {
        var firstChild = Contents.ChildCount;
        var lineIndex = Contents.EntryCount;
        Contents.AddMessage(BuildTranslatableLine(message, color), tagsAllowed: null);
        if (message.TranslationId == 0)
            return;

        if (_translationRows.Count == ChatUIController.PendingTranslationLimit)
        {
            _readyTranslations.Remove(_translationRows.GetAt(0).Key);
            _translationRows.RemoveAt(0);
        }

        // Rich text can own actual child controls, e.g. an in-game language or a link.
        var controls = new Control[Contents.ChildCount - firstChild];
        for (var i = 0; i < controls.Length; i++)
            controls[i] = Contents.GetChild(firstChild + i);

        _translationRows[message.TranslationId] = new TranslationRow(message, lineIndex, color, controls);
    }

    private FormattedMessage BuildTranslatableLine(ChatMessage message, Color color)
    {
        var formatted = FormatLine(message.WrappedMessage, color, 0, _controller.IsEmojiAllowed(message.Channel));
        var culture = ContentLocalizationManager.ValidateCultureName(_loc.DefaultCulture?.Name);
        if (message.Translation is { } translation && message.TranslationCulture == culture)
        {
            formatted.PushColor(color);
            formatted.AddText($"\n↳ {_loc.GetString("wh40k-chat-translation-label")} {translation}");
            formatted.Pop();
        }

        return formatted;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_readyTranslations.Count == 0 || !VisibleInTree)
            return;

        _translationScrollBar ??= Contents.Children.OfType<VScrollBar>().FirstOrDefault();
        // A layout change above the viewport can move what the reader sees even when the scrollbar value is unchanged.
        // Only replace visible-history rows while the reader is following the live tail.
        if (_translationScrollBar?.IsAtEnd != true)
            return;

        for (var i = 0; i < 8 && _readyTranslations.Count > 0; i++)
        {
            var id = _readyTranslations.First();
            _readyTranslations.Remove(id);
            if (!_translationRows.Remove(id, out var row) || row.Index >= Contents.EntryCount)
                continue;

            foreach (var control in row.Controls)
                control.Orphan();

            Contents.SetMessage(row.Index, BuildTranslatableLine(row.Message, row.Color), tagsAllowed: null);
        }
    }

    private sealed record TranslationRow(ChatMessage Message, int Index, Color Color, Control[] Controls);
}
