using System.Globalization;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Progression;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Factions;

public sealed partial class FactionCommunicationsPanel : BoxContainer
{
    [Dependency] private IGameTiming _timing = default!;
    private readonly TabContainer _tabs = new() { VerticalExpand = true };
    private readonly BoxContainer _board = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 10 };
    private readonly BoxContainer _messages = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly LobbyFactionButton _publish = FactionSocialUi.Button("wh40k-faction-comms-publish", true);
    private readonly LobbyFactionButton _send = FactionSocialUi.Button("wh40k-faction-comms-send", true);
    private readonly ConfirmButton _sos = new() { Text = Loc.GetString("wh40k-faction-comms-sos"), ConfirmationText = Loc.GetString("wh40k-faction-comms-sos-confirm"), ResetTime = TimeSpan.FromSeconds(5), MinHeight = 32 };
    private readonly LineEdit _message = new() { Name = "FactionMessage", HorizontalExpand = true, PlaceHolder = Loc.GetString("wh40k-faction-comms-message-placeholder") };
    private readonly RichTextLabel _availability = FactionSocialUi.Body("", true);
    private readonly RichTextLabel _distressHelp = FactionSocialUi.Body(Loc.GetString("wh40k-faction-comms-sos-help"), true);
    private FactionAnnouncementEditor? _editor;
    private FactionSocialSnapshot? _snapshot;
    private string? _faction;
    private bool _pending;
    private bool _canPublish;
    private TimeSpan _messageAvailableAt;
    private TimeSpan _distressAvailableAt;
    private int MessageCooldown => (int) Math.Max(0, Math.Ceiling((_messageAvailableAt - _timing.RealTime).TotalSeconds));
    private int DistressCooldown => (int) Math.Max(0, Math.Ceiling((_distressAvailableAt - _timing.RealTime).TotalSeconds));
    private int _displayedMessageCooldown = -1;
    private int _displayedDistressCooldown = -1;
    public event Action<Wh40kFactionUiAction, FactionSocialRequest>? Request;

    public FactionCommunicationsPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        HorizontalExpand = true;
        Margin = new Thickness(0, 12, 0, 0);
        var boardRoot = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 10 };
        var boardHeading = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 12 };
        var heading = FactionSocialUi.Heading(Loc.GetString("wh40k-faction-comms-board-title"), 16);
        heading.HorizontalExpand = true;
        boardHeading.AddChild(heading);
        boardHeading.AddChild(_publish);
        boardRoot.AddChild(boardHeading);
        boardRoot.AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-comms-board-help"), true));
        var boardScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true };
        boardScroll.AddChild(_board);
        boardRoot.AddChild(boardScroll);
        _tabs.AddChild(boardRoot);
        var channel = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        channel.AddChild(_availability);
        var chatScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true };
        chatScroll.AddChild(_messages);
        channel.AddChild(chatScroll);
        var input = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        input.AddChild(_message);
        input.AddChild(_send);
        input.AddChild(_sos);
        channel.AddChild(input);
        channel.AddChild(_distressHelp);
        _tabs.AddChild(channel);
        _tabs.SetTabTitle(0, Loc.GetString("wh40k-faction-comms-board"));
        _tabs.SetTabTitle(1, Loc.GetString("wh40k-faction-comms-channel"));
        AddChild(_tabs);
        _publish.OnPressed += _ => OpenEditor();
        _send.OnPressed += _ => Send(false);
        _message.OnTextEntered += _ => Send(false);
        _message.OnTextChanged += _ => UpdateButtons();
        _sos.OnPressed += _ => Send(true);
    }

    public void UpdateSnapshot(Wh40kFactionUiSnapshot? snapshot, Wh40kFactionUiOperationStatus status, bool pending)
    {
        if (_faction != snapshot?.FactionId)
        {
            _message.Text = string.Empty;
            _editor?.Close();
        }
        _faction = snapshot?.FactionId;
        _snapshot = snapshot?.Social;
        _canPublish = FactionSocialRules.CanPublish(snapshot?.ViewerRole);
        _pending = pending;
        _messageAvailableAt = _timing.RealTime + TimeSpan.FromSeconds(_snapshot?.MessageCooldown ?? 0);
        _distressAvailableAt = _timing.RealTime + TimeSpan.FromSeconds(_snapshot?.DistressCooldown ?? 0);
        if (!_canPublish)
            _editor?.Close();
        _editor?.SetPending(pending);
        if (status == Wh40kFactionUiOperationStatus.AnnouncementPublished)
            _editor?.Close();
        if (status is Wh40kFactionUiOperationStatus.MessageSent or Wh40kFactionUiOperationStatus.DistressSent)
            _message.Text = string.Empty;
        RenderBoard();
        _messages.RemoveAllChildren();
        if (_snapshot?.CanTransmit != true)
            _messages.AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-comms-offline"), true));
        else if (_snapshot.Messages.Length == 0)
            _messages.AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-comms-no-messages"), true));
        else
        {
            // Newest first keeps new messages visible without moving a reader's scroll position.
            for (var i = _snapshot.Messages.Length - 1; i >= 0; i--)
            {
                var entry = _snapshot.Messages[i];
                var card = FactionSocialUi.Card(entry.Distress);
                var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 5 };
                column.AddChild(FactionSocialUi.Heading($"{(entry.Distress ? "SOS · " : "")}{entry.Author} · {entry.CreatedAt.ToLocalTime():HH:mm}", 13));
                column.AddChild(FactionSocialUi.Body(entry.Text));
                if (entry.Distress)
                    column.AddChild(FactionSocialUi.Body(entry.Location, true));
                card.AddChild(column);
                _messages.AddChild(card);
            }
        }
        _availability.SetMessage(Loc.GetString("wh40k-faction-comms-private"), FactionUiPresentation.Muted);
        UpdateButtons();
    }

    public void SetPending(bool pending)
    {
        _pending = pending;
        _editor?.SetPending(pending);
        UpdateButtons();
    }

    private void RenderBoard()
    {
        _board.RemoveAllChildren();
        if (_snapshot?.Announcements.Length is not > 0)
        {
            _board.AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-comms-no-announcements"), true));
            return;
        }
        foreach (var post in _snapshot.Announcements)
        {
            var card = FactionSocialUi.Card();
            var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 7 };
            column.AddChild(FactionSocialUi.Body(post.Title));
            column.AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-comms-author", ("author", post.Author),
                ("date", post.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))), true));
            column.AddChild(FactionSocialUi.Body(post.Text));
            if (_canPublish)
            {
                var delete = new ConfirmButton { Text = Loc.GetString("wh40k-faction-comms-delete"),
                    ConfirmationText = Loc.GetString("wh40k-faction-comms-delete-confirm"), ResetTime = TimeSpan.FromSeconds(5),
                    HorizontalAlignment = HAlignment.Right, Disabled = _pending };
                delete.OnPressed += _ => Request?.Invoke(Wh40kFactionUiAction.RemoveAnnouncement, new FactionSocialRequest(post.Id));
                column.AddChild(delete);
            }
            card.AddChild(column);
            _board.AddChild(card);
        }
    }

    private void OpenEditor()
    {
        if (!_canPublish || _pending)
            return;
        if (_editor != null)
            return;
        _editor = new FactionAnnouncementEditor();
        _editor.OnClose += () => _editor = null;
        _editor.Publish += request => Request?.Invoke(Wh40kFactionUiAction.PublishAnnouncement, request);
        _editor.OpenCentered();
    }

    private void Send(bool distress)
    {
        if (_pending || _snapshot?.CanTransmit != true || distress && _sos.Disabled || !distress && _send.Disabled)
            return;
        var text = distress && string.IsNullOrWhiteSpace(_message.Text) ? Loc.GetString("wh40k-faction-comms-sos-default") : _message.Text.Trim();
        if (!FactionSocialRules.ValidText(text, FactionSocialRules.MaxMessageLength))
            return;
        Request?.Invoke(distress ? Wh40kFactionUiAction.SendDistress : Wh40kFactionUiAction.SendMessage, new FactionSocialRequest(Guid.NewGuid(), text));
    }

    private void UpdateButtons()
    {
        _publish.Visible = _canPublish;
        _publish.Disabled = _pending;
        _message.Editable = !_pending && _snapshot?.CanTransmit == true;
        _displayedMessageCooldown = MessageCooldown;
        _displayedDistressCooldown = DistressCooldown;
        var validText = FactionSocialRules.ValidText(_message.Text, FactionSocialRules.MaxMessageLength);
        _send.Disabled = _pending || _snapshot?.CanTransmit != true || _displayedMessageCooldown > 0 || !validText;
        var invalidDistress = !string.IsNullOrWhiteSpace(_message.Text) && !validText;
        var sosDisabled = _pending || _snapshot?.CanTransmit != true || _displayedDistressCooldown > 0 || invalidDistress;
        if (sosDisabled)
            _sos.IsConfirming = false;
        _sos.Disabled = sosDisabled;
        _sos.ToolTip = Loc.GetString(invalidDistress ? "wh40k-faction-comms-invalid" : "wh40k-faction-comms-sos-help");
        if (!_sos.IsConfirming)
            _sos.Text = _displayedDistressCooldown > 0 ? Loc.GetString("wh40k-faction-comms-sos-cooldown", ("seconds", _displayedDistressCooldown)) : Loc.GetString("wh40k-faction-comms-sos");
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (MessageCooldown != _displayedMessageCooldown || DistressCooldown != _displayedDistressCooldown)
            UpdateButtons();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // The same help remains available on the SOS tooltip in short windows.
        _distressHelp.Visible = availableSize.Y >= 400;
        return base.MeasureOverride(availableSize);
    }

    protected override void ExitedTree()
    {
        _editor?.Close();
        base.ExitedTree();
    }
}

internal sealed class FactionAnnouncementEditor : FancyWindow
{
    private readonly LineEdit _title = new() { PlaceHolder = Loc.GetString("wh40k-faction-comms-title-placeholder") };
    private readonly TextEdit _text = new() { VerticalExpand = true, MinHeight = 140 };
    private readonly Label _count = new() { FontColorOverride = FactionUiPresentation.Muted };
    private readonly LobbyFactionButton _submit = FactionSocialUi.Button("wh40k-faction-comms-publish", true);
    private readonly Guid _id = Guid.NewGuid();
    private bool _pending;
    public event Action<FactionSocialRequest>? Publish;

    public FactionAnnouncementEditor()
    {
        Title = Loc.GetString("wh40k-faction-comms-publish");
        MinSize = new System.Numerics.Vector2(480, 340);
        SetSize = new System.Numerics.Vector2(580, 440);
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 12, Margin = new Thickness(16) };
        column.AddChild(FactionSocialUi.Body(Loc.GetString("wh40k-faction-comms-publish-help"), true));
        column.AddChild(_title);
        column.AddChild(_text);
        column.AddChild(_count);
        _submit.HorizontalAlignment = HAlignment.Right;
        column.AddChild(_submit);
        ContentsContainer.AddChild(column);
        _title.OnTextChanged += _ => UpdateForm();
        _text.OnTextChanged += _ => UpdateForm();
        _submit.OnPressed += _ => Publish?.Invoke(new FactionSocialRequest(_id, Rope.Collapse(_text.TextRope).Trim(), _title.Text.Trim()));
        UpdateForm();
    }

    public void SetPending(bool pending)
    {
        _pending = pending;
        _title.Editable = !pending;
        _text.Editable = !pending;
        UpdateForm();
    }

    private void UpdateForm()
    {
        var text = Rope.Collapse(_text.TextRope);
        _count.Text = Loc.GetString("wh40k-faction-comms-count", ("count", text.Length), ("max", FactionSocialRules.MaxAnnouncementLength));
        _submit.Disabled = _pending || !FactionSocialRules.ValidText(_title.Text, FactionSocialRules.MaxTitleLength) ||
            !FactionSocialRules.ValidText(text, FactionSocialRules.MaxAnnouncementLength, true);
    }
}
