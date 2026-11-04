using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleLockSystem
{
    private bool HasStoredShuttleOwnerAccess(EntityUid grid, EntityUid user)
    {
        return HasComp<Content.Server._WH40K.ShuttleStorage.ShuttleStorageTransferComponent>(grid) &&
               TryComp(grid, out ShipOwnershipComponent? ownership) &&
               TryComp(user, out ActorComponent? actor) && actor.PlayerSession.AttachedEntity == user &&
               actor.PlayerSession.UserId == ownership.OwnerUserId;
    }

    public void RestoreStoredShuttleAccess(EntityUid grid, EntityUid? user)
    {
        var locked = TryComp(grid, out ShipGridLockComponent? gridLock) && gridLock.Locked;
        SetGridLockState(grid, locked, grid.ToString());
        Stack<EntityUid> remaining = new();
        remaining.Push(grid);
        while (remaining.TryPop(out var entity))
        {
            if (TryComp(entity, out ShuttleConsoleLockComponent? console))
                SetShuttleId(entity, grid.ToString(), console);
            var children = Transform(entity).ChildEnumerator;
            while (children.MoveNext(out var child))
                remaining.Push(child);
        }
        if (user is not { } actor || !TryComp(grid, out ShuttleDeedComponent? shipDeed))
            return;
        foreach (var card in FindAccessibleIdCards(actor))
        {
            if (TryComp(card, out ShuttleDeedComponent? existing) && existing.ShuttleUid is { } other && Exists(other))
                continue;
            var deed = EnsureComp<ShuttleDeedComponent>(card);
            deed.ShuttleUid = grid;
            deed.ShuttleName = shipDeed.ShuttleName;
            deed.ShuttleNameSuffix = shipDeed.ShuttleNameSuffix;
            deed.ShuttleOwner = shipDeed.ShuttleOwner;
            deed.PurchasedWithVoucher = shipDeed.PurchasedWithVoucher;
            deed.PurchaseVoucherUid = null;
            deed.DeedHolder = card;
            shipDeed.DeedHolder = card;
            Dirty(card, deed);
            Dirty(grid, shipDeed);
            break;
        }
    }
}
