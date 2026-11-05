using Content.Shared._NF.Bank.Components;
using Content.Shared.Power;
using Content.Shared.VendingMachines;
using Robust.Client.Player;

namespace Content.Client.VendingMachines;

public sealed partial class VendingMachineSystem
{
    [Dependency] private IPlayerManager _keypadPlayers = default!;

    private void InitializeKeypad()
    {
        SubscribeLocalEvent<BankAccountComponent, AfterAutoHandleStateEvent>(OnKeypadBankState);
        SubscribeLocalEvent<VendingMachineComponent, PowerChangedEvent>(OnKeypadPowerChanged);
    }

    private void OnKeypadBankState(Entity<BankAccountComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_keypadPlayers.LocalSession?.AttachedEntity != ent.Owner)
            return;

        var query = EntityQueryEnumerator<VendingMachineComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_uiSystem.TryGetOpenUi<VendingMachineBoundUserInterface>(uid, VendingMachineUiKey.Key, out var bui))
                bui.Update();
        }
    }

    private void OnKeypadPowerChanged(Entity<VendingMachineComponent> ent, ref PowerChangedEvent args)
    {
        if (_uiSystem.TryGetOpenUi<VendingMachineBoundUserInterface>(ent.Owner, VendingMachineUiKey.Key, out var bui))
            bui.Update();
    }
}
