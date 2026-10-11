using Content.Shared._WH40K.Merchant;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Merchant;

public sealed class Wh40kMedicalBoundUserInterface(EntityUid owner, Enum key) : BoundUserInterface(owner, key)
{
    private Wh40kMedicalWindow? _window;
    private Wh40kMedicalConfirm? _pending;
    public Wh40kMedicalQuoteMessage? Snapshot { get; private set; }
    public Wh40kMedicalResult? LastResult { get; private set; }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<Wh40kMedicalWindow>();
        _window.OnRefresh += () => SendMessage(_pending is {} pending ? pending : new Wh40kMedicalRefresh());
        _window.OnConfirm += () =>
        {
            if (_pending != null || Snapshot == null || Snapshot.Quote == Guid.Empty) return;
            _pending = new(Snapshot.Quote, Guid.NewGuid());
            _window.SetPending(true);
            SendMessage(_pending);
        };
        if (Snapshot != null) _window.UpdateQuote(Snapshot);
        // Reuse the merchant handshake: messages may arrive before the replicated BUI opens its window.
        SendMessage(new Wh40kMedicalRefresh());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is Wh40kMedicalQuoteMessage quote)
        { Snapshot = quote; _window?.UpdateQuote(quote); }
        else if (message is Wh40kMedicalResult result)
        {
            LastResult = result;
            if (_pending?.Operation == result.Operation) { _pending = null; _window?.SetPending(false); }
            _window?.ShowResult(result.Result);
        }
    }
}
