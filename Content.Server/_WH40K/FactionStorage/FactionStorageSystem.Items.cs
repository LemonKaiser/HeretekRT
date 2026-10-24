using System.IO;
using System.Linq;
using Content.Server._WH40K.PersistentInventory;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Server._WH40K.Progression;
using Content.Server.Database;
using Content.Server.Storage.Components;
using Content.Shared._WH40K.FactionStorage;
using Content.Shared._WH40K.Merchant;
using Content.Shared._WH40K.Npcs;
using Content.Shared._WH40K.Quests;
using Content.Shared.Explosion.Components;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.Player;

namespace Content.Server._WH40K.FactionStorage;

public sealed partial class FactionStorageSystem
{
    private static readonly PersistentInventoryLimits ItemLimits = new(MaxRoots: 1);

    private bool CanStore(IReadOnlyList<EntityUid> entities, bool restoring = false)
    {
        foreach (var item in entities)
        {
            if (TerminatingOrDeleted(item) || !HasComp<ItemComponent>(item) ||
                HasComp<StorageComponent>(item) || HasComp<EntityStorageComponent>(item) ||
                HasComp<InventoryComponent>(item) || HasComp<UnremoveableComponent>(item) ||
                HasComp<ActiveTimerTriggerComponent>(item) || HasComp<PersistentInventoryOperationComponent>(item) ||
                HasComp<Wh40kQuestReservationComponent>(item) || HasComp<Wh40kQuestItemComponent>(item) ||
                HasComp<Wh40kRewardDeliveryClaimComponent>(item) || HasComp<Wh40kNpcGearComponent>(item) ||
                !restoring && TryComp(item, out MerchantItemTokenComponent? title) && !title.Verified)
                return false;
        }
        return true;
    }

    private void StartDeposit(EntityUid terminal, FactionStorageComponent storage, EntityUid user, EntityUid item, int slot, Depot depot)
    {
        if (!_hands.TryGetActiveItem(user, out var held) || held != item ||
            !_hands.IsHolding(user, item, out var hand) || !_hands.CanDropHeld(user, hand))
            return;
        try
        {
            if (!CanStore([item]))
                throw new InvalidDataException("The item cannot be stored.");
            var capture = _serializer.CaptureRoots(
                [new(PersistentInventoryRootKind.Hand, "faction-storage", item)],
                PersistentInventorySnapshotSerializer.DefaultPolicyId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ItemLimits);
            if (!capture.IsSuccess || capture.Payload is not { Roots.Count: 1 } payload ||
                capture.DeniedEntities.Count != 0 || capture.OmittedComponents.Count != 0 || !CanStore(capture.CapturedEntities))
                throw new InvalidDataException(capture.Error ?? "The complete item cannot be serialized.");

            var verification = _serializer.RestoreIsolated(payload, ItemLimits);
            try
            {
                if (!verification.Success || !CanStore(verification.Entities.Values.ToArray(), restoring: true))
                    throw new InvalidDataException(verification.Error ?? "The item cannot be restored.");
            }
            finally { _serializer.DeleteIsolated(verification); }

            var packed = PersistentInventoryPayloadCodec.Pack(payload, ItemLimits);
            var meta = MetaData(item);
            var row = new Wh40kFactionStorageSlot
            {
                Faction = storage.Faction.Id, Slot = slot, Revision = Guid.NewGuid(),
                Payload = packed.Data, Sha256 = packed.Sha256, Prototype = meta.EntityPrototype!.ID,
                Name = meta.EntityName != meta.EntityPrototype.Name ? meta.EntityName : null,
                Description = meta.EntityDescription != meta.EntityPrototype.Description ? meta.EntityDescription : null,
                Count = TryComp(item, out StackComponent? stack) ? stack.Count : 1,
            };
            if (row.Name is { Length: > 4096 } || row.Description is { Length: > 16384 } || row.Count <= 0)
                throw new InvalidDataException("The item metadata exceeds storage limits.");
            Begin(new Transfer(depot, terminal, user, Comp<ActorComponent>(user).PlayerSession.UserId,
                item, capture.CapturedEntities, row, depot.Slots[slot], null));
        }
        catch (Exception error)
        {
            if (error is InvalidDataException)
                Log.Debug($"Faction depot rejected {ToPrettyString(item)}: {error.Message}");
            else
                Log.Error($"Faction depot capture failed for {ToPrettyString(item)}: {error}");
            Notify(terminal, user, "wh40k-faction-storage-item-rejected");
        }
    }

    private void StartWithdrawal(EntityUid terminal, EntityUid user, Wh40kFactionStorageSlot row, Depot depot)
    {
        if (!_hands.TryGetEmptyHand(user, out _))
        {
            Notify(terminal, user, "wh40k-faction-storage-free-hand");
            return;
        }
        PersistentInventoryRestoreResult? restored = null;
        try
        {
            var payload = PersistentInventoryPayloadCodec.Unpack(row.Payload!, row.Sha256!, ItemLimits);
            restored = _serializer.RestoreIsolated(payload, ItemLimits);
            if (!restored.Success || restored.Roots.Count != 1 || !CanStore(restored.Entities.Values.ToArray(), restoring: true))
                throw new InvalidDataException(restored.Error ?? "The stored item is invalid.");
            var item = restored.Roots[0].Entity;
            if (MetaData(item).EntityPrototype?.ID != row.Prototype ||
                (TryComp(item, out StackComponent? stack) ? stack.Count : 1) != row.Count)
                throw new InvalidDataException("The item differs from its slot metadata.");
            if (row.Name != null)
                _metadata.SetEntityName(item, row.Name);
            if (row.Description != null)
                _metadata.SetEntityDescription(item, row.Description);
            var empty = new Wh40kFactionStorageSlot { Faction = row.Faction, Slot = row.Slot, Revision = Guid.NewGuid() };
            Begin(new Transfer(depot, terminal, user, Comp<ActorComponent>(user).PlayerSession.UserId,
                item, restored.Entities.Values.ToArray(), empty, row, restored));
        }
        catch (Exception error)
        {
            if (restored != null)
                _serializer.DeleteIsolated(restored);
            Log.Error($"Faction depot retained {row.Faction}/{row.Slot}: {error}");
            Notify(terminal, user, "wh40k-faction-storage-restore-failed");
        }
    }
}
