using Content.Shared._WH40K.Quests;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Quests;

public sealed partial class Wh40kQuestBoundUserInterface : BoundUserInterface
{
    private Wh40kQuestWindow? _window;
    public Wh40kQuestUiSnapshot? Snapshot { get; private set; }
    public Wh40kQuestBoundUserInterface(EntityUid owner, Enum key) : base(owner, key) { }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<Wh40kQuestWindow>();
        _window.Request += OnRequest;
        if (Snapshot != null) _window.ShowSnapshot(Snapshot);
        // Private messages can arrive before the replicated actor list creates this BUI.
        // Request the authoritative snapshot after the client has actually opened it.
        SendMessage(new Wh40kQuestUiRequest(Wh40kQuestUiAction.Refresh, Guid.NewGuid()));
    }

    private void OnRequest(Wh40kQuestUiRequest request) => SendMessage(request);

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is not Wh40kQuestUiSnapshot state) return;
        Snapshot = state;
        _window?.ShowSnapshot(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null) _window.Request -= OnRequest;
        Snapshot = null;
        base.Dispose(disposing);
    }
}
