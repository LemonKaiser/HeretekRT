using Content.Client._Mono.Economy;
using Content.Client.Power.Components;
using Content.Client.UserInterface.Controls;
using Content.Client.VendingMachines.UI;
using Content.Shared._Mono.Economy.Component;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WH40K.VendingMachines;
using Content.Shared.Access.Systems;
using Content.Shared.VendingMachines;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client.VendingMachines;

public sealed class VendingMachineBoundUserInterface : BoundUserInterface
{
    [Dependency] private CreditReceiverSystem _credit = default!;
    [Dependency] private IPlayerManager _players = default!;

    private VendingMachineMenu? _menu;
    private List<VendingMachineInventoryEntry> _inventory = new();

    public VendingMachineBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindowCenteredLeft<VendingMachineMenu>();
        _menu.Title = EntMan.TryGetComponent(Owner, out MetaDataComponent? meta)
            ? meta.EntityName : Loc.GetString("vending-machine-nf-fallback-title");
        _menu.VendingMachineOwner = Owner;
        _menu.OnCodeEntered += OnCodeEntered;
        _menu.OnAudioPlayed += OnAudioPlayed;
        Update();
    }

    public override void Update()
    {
        if (_menu == null || !EntMan.TryGetComponent<VendingMachineComponent>(Owner, out var vending))
            return;

        var system = EntMan.System<VendingMachineSystem>();
        _inventory = system.GetAllInventory(Owner);
        var player = _players.LocalSession?.AttachedEntity;
        _menu.User = player;
        var balance = 0;
        var hasAccount = false;
        if (player is { } user && !EntMan.HasComponent<IronmanComponent>(user) &&
            EntMan.TryGetComponent<BankAccountComponent>(user, out var bank))
        {
            balance = bank.Balance;
            hasAccount = true;
        }

        int? cash = null;
        if (EntMan.HasComponent<CreditReceiverComponent>(Owner))
        {
            _credit.TryGetCash(Owner, out _, out var amount);
            cash = amount;
        }
        var modifier = EntMan.TryGetComponent<MarketModifierComponent>(Owner, out var market) ? market.Mod : 1f;
        var enabled = !vending.Broken &&
                      (!EntMan.TryGetComponent<ApcPowerReceiverComponent>(Owner, out var receiver) || receiver.Powered);
        _menu.Populate(_inventory, modifier, balance, cash, vending.RequiresCash, enabled, hasAccount, vending.MarketPrices);
    }

    private bool OnCodeEntered(int index)
    {
        if (index < 0 || index >= _inventory.Count || _players.LocalSession?.AttachedEntity is not { } user ||
            !EntMan.System<AccessReaderSystem>().IsAllowed(user, Owner))
            return false;

        var item = _inventory[index];
        var expected = EntMan.TryGetComponent<VendingMachineComponent>(Owner, out var vending) &&
                       vending.MarketPrices.TryGetValue(item.ID, out var price) ? (int?) price : null;
        SendMessage(new VendingMachineEjectMessage(item.Type, item.ID) { ExpectedPrice = expected });
        return true;
    }

    private void OnAudioPlayed(VendingMachineKeypadSound sound, float pitch) =>
        SendMessage(new VendingMachineKeypadAudioMessage(sound, pitch));

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is VendingMachineVendResultMessage result)
        {
            _menu?.CompleteVend(result.Type, result.Id, result.Success);
            Update();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || _menu == null)
            return;
        _menu.OnCodeEntered -= OnCodeEntered;
        _menu.OnAudioPlayed -= OnAudioPlayed;
        _menu.Dispose();
    }
}
