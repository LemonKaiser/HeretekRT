using Content.Shared._WH40K.Merchant;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Merchant;

public sealed class MerchantBoundUserInterface : BoundUserInterface
{
    private MerchantWindow? _window;
    private Action<string>? _buyHandler;
    private Action? _refreshHandler;
    private BoundUserInterfaceMessage? _pending;
    private Guid _operation;
    public MerchantUpdateState? Snapshot { get; private set; }
    public MerchantAppraisalState? Appraisal { get; private set; }
    public MerchantResultMessage? LastResult { get; private set; }

    public MerchantBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindowCenteredLeft<MerchantWindow>();
        _buyHandler = product =>
        {
            if (_pending != null || Snapshot == null) return;
            _operation = Guid.NewGuid();
            _pending = new MerchantBuyMessage(product, Snapshot.CatalogId, _operation);
            _window.SetPending(true);
            SendMessage(_pending);
        };
        _refreshHandler = () => SendMessage(_pending ?? new MerchantRequestRefreshMessage());
        _window.OnAppraise += () => { if (_pending == null) SendMessage(new MerchantAppraiseMessage()); };
        _window.OnSell += () =>
        {
            if (_pending != null || Appraisal == null) return;
            _operation = Guid.NewGuid();
            _pending = new MerchantSellMessage(Appraisal.Quote, _operation);
            _window.SetPending(true);
            SendMessage(_pending);
        };
        _window.OnBuy += _buyHandler;
        _window.OnRequestRefresh += _refreshHandler;
        if (Snapshot != null) _window.UpdateState(Snapshot);
        if (Appraisal != null) _window.SetAppraisal(Appraisal);
        // As in the quest journal, private messages may arrive before the replicated actor list opens the BUI.
        SendMessage(new MerchantRequestRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is MerchantUpdateState merchantState)
        {
            if (Snapshot?.CatalogId != merchantState.CatalogId)
            { Appraisal = null; _window?.SetAppraisal(null); }
            Snapshot = merchantState;
            _window?.UpdateState(merchantState);
        }
        else if (message is MerchantAppraisalState appraisal)
        { Appraisal = appraisal; _window?.SetAppraisal(appraisal); }
        else if (message is MerchantResultMessage result)
        {
            LastResult = result;
            if (result.Operation == _operation)
            { _pending = null; _window?.SetPending(false); Appraisal = null; _window?.SetAppraisal(null); }
            _window?.ShowResult(result.Result);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing || _window == null)
        {
            base.Dispose(disposing);
            return;
        }

        if (_buyHandler != null)
            _window.OnBuy -= _buyHandler;
        if (_refreshHandler != null)
            _window.OnRequestRefresh -= _refreshHandler;

        // CreateWindowCenteredLeft registers the window for disposal in the
        // base BUI, so do not dispose it a second time here.
        base.Dispose(disposing);
    }
}
