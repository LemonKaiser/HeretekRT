using System.Threading.Tasks;
using Content.Server._WH40K.PersistentInventory;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Server.Database;
using Content.Shared._WH40K.FactionStorage;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.FactionStorage;

public sealed partial class FactionStorageSystem
{
    private sealed record Transfer(Depot Depot, EntityUid Terminal, EntityUid User, NetUserId Account,
        EntityUid Item, IReadOnlyList<EntityUid> Entities, Wh40kFactionStorageSlot Write,
        Wh40kFactionStorageSlot? Previous, PersistentInventoryRestoreResult? Restored)
    {
        public bool Deposit => Restored == null;
        public bool Finished;
    }

    private void Begin(Transfer transfer)
    {
        _busyUsers.Add(transfer.Account);
        transfer.Depot.Status = FactionDepotStatus.Busy;
        try
        {
            SetLock(transfer, true);
            Broadcast(transfer.Write.Faction);
        }
        catch
        {
            SetLock(transfer, false);
            _busyUsers.Remove(transfer.Account);
            transfer.Depot.Status = FactionDepotStatus.Ready;
            throw;
        }
        _sessions.TrackWork(RunTransferAsync(transfer));
    }

    private void SetLock(Transfer transfer, bool locked)
    {
        foreach (var item in transfer.Entities)
        {
            if (TerminatingOrDeleted(item))
                continue;
            if (locked)
            {
                var marker = EnsureComp<PersistentInventoryOperationComponent>(item);
                marker.UserId = transfer.Account;
                marker.OperationId = new(transfer.Write.Revision);
            }
            else if (TryComp(item, out PersistentInventoryOperationComponent? marker) && marker.OperationId.Value == transfer.Write.Revision)
                RemComp<PersistentInventoryOperationComponent>(item);
        }
    }

    private bool CanDeliver(Transfer transfer)
        => TryComp(transfer.Terminal, out FactionStorageComponent? storage) && storage.Faction.Id == transfer.Write.Faction &&
           TryComp(transfer.User, out ActorComponent? actor) && actor.PlayerSession.UserId == transfer.Account &&
           CanMove(transfer.Terminal, storage, transfer.User, completing: true) && !TerminatingOrDeleted(transfer.Item);

    private async Task RunTransferAsync(Transfer transfer)
    {
        if (!transfer.Deposit)
        {
            var prepared = false;
            try
            {
                await EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>().ReconcileSalesAsync(transfer.Account);
                await _sessions.OnSimulationThread(() =>
                {
                    SetLock(transfer, false);
                    try
                    {
                        prepared = CanDeliver(transfer) && CanStore(transfer.Entities) && _hands.CanPickupAnyHand(transfer.User, transfer.Item);
                    }
                    finally { SetLock(transfer, true); }
                });
            }
            catch (Exception error) { Log.Error($"Faction depot withdrawal preparation failed: {error}"); }
            if (!prepared)
            {
                await CompleteOnSimulationThread(() => Finish(transfer, null, "wh40k-faction-storage-free-hand"));
                return;
            }
        }

        var committed = await WriteUntilKnownAsync(transfer.Write, transfer.Previous?.Revision ?? Guid.Empty, transfer);
        if (!committed)
        {
            await CompleteOnSimulationThread(() => Finish(transfer, null, "wh40k-faction-storage-stale", reload: true));
            return;
        }
        if (transfer.Deposit)
        {
            await CompleteOnSimulationThread(() =>
            {
                // A break/destruction system may have detached parts while the database was busy.
                // Every captured entity is now owned by the stored snapshot, including those parts.
                foreach (var item in transfer.Entities)
                    if (!TerminatingOrDeleted(item))
                        Del(item);
                Finish(transfer, transfer.Write, "wh40k-faction-storage-deposited");
            });
            return;
        }

        var delivered = false;
        var attemptedPickup = false;
        await CompleteOnSimulationThread(() =>
        {
            if (delivered)
                return;
            SetLock(transfer, false);
            try
            {
                delivered = !TerminatingOrDeleted(transfer.User) && _hands.IsHolding(transfer.User, transfer.Item);
                if (!delivered && CanDeliver(transfer))
                {
                    attemptedPickup = true;
                    delivered = _hands.TryPickupAnyHand(transfer.User, transfer.Item, animate: false);
                }
            }
            finally
            {
                // Callbacks may consume the item, or throw after insertion. Both already transferred ownership.
                delivered |= attemptedPickup && TerminatingOrDeleted(transfer.Item) ||
                             !TerminatingOrDeleted(transfer.User) && _hands.IsHolding(transfer.User, transfer.Item);
                if (!delivered)
                    SetLock(transfer, true);
            }
        });
        if (delivered)
        {
            await CompleteOnSimulationThread(() => Finish(transfer, transfer.Write, "wh40k-faction-storage-withdrawn"));
            return;
        }

        // Hands, access or the actor may have changed while awaiting the database.
        // Restore the cell before deleting the isolated copy; keep the faction locked until confirmed.
        var previous = transfer.Previous!;
        var returned = new Wh40kFactionStorageSlot
        {
            Faction = previous.Faction, Slot = previous.Slot, Revision = Guid.NewGuid(),
            Payload = previous.Payload, Sha256 = previous.Sha256, Prototype = previous.Prototype,
            Name = previous.Name, Description = previous.Description, Count = previous.Count,
        };
        await WriteUntilKnownAsync(returned, transfer.Write.Revision, transfer, mustCommit: true);
        await CompleteOnSimulationThread(() => Finish(transfer, returned, "wh40k-faction-storage-free-hand"));
    }

    private async Task<bool> WriteUntilKnownAsync(Wh40kFactionStorageSlot row, Guid expected, Transfer transfer, bool mustCommit = false)
    {
        var reported = false;
        while (true)
        {
            bool? committed = null;
            try { committed = await _db.WriteWh40kFactionStorageSlotAsync(row, expected); }
            catch (Exception error)
            {
                if (!reported)
                    Log.Error($"Faction depot write {row.Revision} requires reconciliation: {error}");
                try
                {
                    var stored = await _db.GetWh40kFactionStorageSlotAsync(row.Faction, row.Slot);
                    if (stored?.Revision == row.Revision)
                        committed = true;
                    else if ((stored?.Revision ?? Guid.Empty) != expected)
                        committed = false;
                }
                catch { /* Retry the same revision: a lost response can follow a successful commit. */ }
            }
            if (committed == true || committed == false && !mustCommit)
                return committed.Value;
            if (!reported)
            {
                reported = true;
                await CompleteOnSimulationThread(() => Notify(transfer.Terminal, transfer.User, "wh40k-faction-storage-pending"));
            }
            await Task.Delay(1000);
        }
    }

    private async Task CompleteOnSimulationThread(Action action)
    {
        var reported = false;
        while (true)
        {
            try
            {
                await _sessions.OnSimulationThread(action);
                return;
            }
            catch (Exception error)
            {
                if (!reported)
                    Log.Error($"Faction depot completion will be retried: {error}");
                reported = true;
                await Task.Delay(1000);
            }
        }
    }

    private void Finish(Transfer transfer, Wh40kFactionStorageSlot? written, string message, bool reload = false)
    {
        if (!transfer.Finished)
        {
            if (transfer.Restored != null && (written == null || written.Payload != null))
                _serializer.DeleteIsolated(transfer.Restored);
            SetLock(transfer, false);
            if (written != null)
                transfer.Depot.Slots[written.Slot] = written;
            _busyUsers.Remove(transfer.Account);
            transfer.Depot.Status = reload ? FactionDepotStatus.Unavailable : FactionDepotStatus.Ready;
            transfer.Finished = true;
        }
        Broadcast(transfer.Write.Faction);
        Notify(transfer.Terminal, transfer.User, message);
        if (reload)
        {
            transfer.Depot.RefreshAt = TimeSpan.Zero;
            Refresh(transfer.Write.Faction);
        }
    }
}
