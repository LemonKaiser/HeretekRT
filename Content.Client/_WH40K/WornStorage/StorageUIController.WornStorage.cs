using Content.Client.Hands.Systems;
using Content.Client.UserInterface.Systems.Hands.Controls;
using Content.Client.UserInterface.Systems.Storage.Controls;
using Content.Shared._WH40K.WornStorage;
using Content.Shared.Storage;
using Robust.Client.UserInterface;

namespace Content.Client.UserInterface.Systems.Storage;

public sealed partial class StorageUIController
{
    private bool TryOpenStoredStorage(EntityUid item, EntityUid storage)
    {
        if (!_configuration.GetCVar(Wh40kWornStorageCVars.ClickOpens)
            || !_storage.NestedStorage
            || !EntityManager.TryGetComponent(item, out StorageComponent? itemStorage)
            || !itemStorage.OpenOnActivate
            || EntityManager.System<HandsSystem>().GetActiveHandEntity() != null)
            return false;

        EntityManager.RaisePredictiveEvent(new OpenNestedStorageEvent(
            EntityManager.GetNetEntity(item),
            EntityManager.GetNetEntity(storage)));
        return true;
    }

    private void DropStoredStorageOutside(Control? target, EntityUid item, EntityUid storage)
    {
        if (!_configuration.GetCVar(Wh40kWornStorageCVars.ClickOpens) || !EntityManager.HasComponent<StorageComponent>(item))
            return;

        HandButton? handButton = null;
        for (var current = target; current != null; current = current.Parent)
        {
            if (current is StorageWindow)
                return;

            handButton ??= current as HandButton;
        }

        TakeOutStoredItem(item, storage, handButton?.SlotName);
    }

    public void TakeOutStoredItem(EntityUid item, EntityUid storage, string? handName = null)
    {
        var handsSystem = EntityManager.System<HandsSystem>();
        if (_player.LocalEntity is not { } player || !handsSystem.TryGetPlayerHands(out var hands))
            return;

        if (handName != null)
        {
            if (!handsSystem.TryGetHand(player, handName, out var hand, hands) || hand.HeldEntity != null)
                return;

            if (hands.ActiveHand != hand)
                handsSystem.UIHandClick(hands, handName);
        }
        else if (hands.ActiveHandEntity != null)
        {
            return;
        }

        EntityManager.RaisePredictiveEvent(new StorageInteractWithItemEvent(
            EntityManager.GetNetEntity(item),
            EntityManager.GetNetEntity(storage)));
    }
}
