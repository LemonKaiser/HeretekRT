using Content.Shared._WH40K.Lathe;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials.OreSilo;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Lathe.UI;

[UsedImplicitly]
public sealed class Wh40kFabricationSiloBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private Wh40kFabricationSiloMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<Wh40kFabricationSiloMenu>();
        _menu.SetEntity(Owner);
        _menu.OnClientEntryPressed += uid => SendPredictedMessage(new ToggleOreSiloClientMessage(uid));
        _menu.OnPartPressed += uid => SendMessage(new EjectWh40kFabricationSiloPartMessage(uid));
        _menu.OnSlotPressed += () =>
            SendMessage(new ItemSlotButtonPressedEvent(Wh40kFabricationSiloComponent.ContainerSlotId));
        _menu.OnWithdrawPressed += (reagent, amount) =>
            SendMessage(new WithdrawWh40kFabricationSiloReagentMessage(reagent, amount));
        _menu.OnDiscardPressed += reagent => SendMessage(new DiscardWh40kFabricationSiloReagentMessage(reagent));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OreSiloBuiState silo)
            _menu?.Update(silo);
    }
}
