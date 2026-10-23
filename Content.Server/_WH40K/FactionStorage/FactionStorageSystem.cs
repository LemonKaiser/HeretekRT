using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.PersistentInventory;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Server._WH40K.Progression;
using Content.Server._WH40K.Quests;
using Content.Server.Database;
using Content.Server.Storage.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.FactionStorage;
using Content.Shared._WH40K.Merchant;
using Content.Shared._WH40K.Npcs;
using Content.Shared._WH40K.Quests;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.FactionStorage;

public sealed partial class FactionStorageSystem : EntitySystem
{
    private static readonly PersistentInventoryLimits Limits = new(MaxRoots: 1);

    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private PersistentInventorySnapshotSerializer _serializer = default!;
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WH40KPlayerCultureManager _culture = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<string, Dictionary<int, Wh40kFactionStorageSlot>> _cache = new();
    private readonly HashSet<string> _busy = new();
    private readonly Dictionary<string, TimeSpan> _refreshAt = new();
    private readonly Dictionary<Guid, Transfer> _unresolved = new();
    private TimeSpan _nextUpdate;

    private sealed class Transfer
    {
        public required Wh40kFactionStorageSlot Row;
        public required Guid ExpectedRevision;
        public required EntityUid Terminal;
        public required EntityUid Body;
        public required EntityUid Item;
        public required List<EntityUid> Locked;
        public required bool Deposit;
        public PersistentInventoryRestoreResult? Restored;
        public readonly HashSet<EntityUid> Paused = new();
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FactionStorageComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<FactionStorageComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<FactionStorageComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<FactionStorageComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<FactionStorageComponent, FactionStorageRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<FactionStorageComponent, FactionStorageTransferMessage>(OnTransfer);
    }

    public bool IsMember(EntityUid user, FactionStorageComponent storage)
        => TryComp(user, out CompanyComponent? company) && company.CompanyName == storage.Faction;

    private bool CanAccess(EntityUid terminal, FactionStorageComponent storage, EntityUid user)
        => !TerminatingOrDeleted(terminal) && !TerminatingOrDeleted(user) &&
           IsMember(user, storage) && TryComp(user, out ActorComponent? actor) &&
           actor.PlayerSession.AttachedEntity == user && !_sessions.IsStopping &&
           _interaction.InRangeUnobstructed(user, terminal, popup: false);

    private bool CanTransfer(EntityUid terminal, FactionStorageComponent storage, EntityUid user)
    {
        if (!CanAccess(terminal, storage, user) || !_blocker.CanInteract(user, terminal) ||
            HasComp<PersistentInventoryOperationComponent>(user))
            return false;
        var account = Comp<ActorComponent>(user).PlayerSession.UserId;
        return !EntityManager.System<Wh40kQuestInteractionSystem>().IsBusy(account) &&
               !EntityManager.System<Wh40kQuestSubmissionSystem>().IsBusy(account) &&
               !EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>().IsSelling(account);
    }

    private void OnOpenAttempt(Entity<FactionStorageComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!CanAccess(ent, ent.Comp, args.User))
        {
            args.Cancel();
            Result(ent, args.User, "wh40k-faction-storage-denied");
        }
    }

    private void OnInteractHand(EntityUid uid, FactionStorageComponent comp, InteractHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (!CanAccess(uid, comp, args.User))
        {
            Result(uid, args.User, "wh40k-faction-storage-denied");
            return;
        }
        _ui.TryOpenUi(uid, FactionStorageUiKey.Key, args.User);
    }

    private void OnOpened(Entity<FactionStorageComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(FactionStorageUiKey.Key))
            return;
        Refresh(ent.Comp.Faction.Id);
        SendState(ent, ent.Comp, args.Actor);
    }

    private void OnRefresh(EntityUid uid, FactionStorageComponent comp, FactionStorageRefreshMessage args)
    {
        if (!ValidViewer(uid, comp, args.Actor))
            return;
        Refresh(comp.Faction.Id);
        SendState(uid, comp, args.Actor);
    }

    private bool ValidViewer(EntityUid uid, FactionStorageComponent comp, EntityUid user)
    {
        if (!_ui.GetActors(uid, FactionStorageUiKey.Key).Contains(user))
            return false;
        if (CanAccess(uid, comp, user))
            return true;
        _ui.CloseUi(uid, FactionStorageUiKey.Key, user);
        return false;
    }

    private void OnInteractUsing(EntityUid uid, FactionStorageComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (!CanTransfer(uid, comp, args.User))
        {
            Result(uid, args.User, "wh40k-faction-storage-denied");
            return;
        }
        if (!TryGetAvailable(uid, comp, args.User, out var rows))
            return;
        // Direct insertion uses the last free numbered cell, without moving existing entries.
        for (var slot = FactionStorageComponent.Capacity - 1; slot >= 0; slot--)
        {
            if (rows.TryGetValue(slot, out var row) && row.Payload != null)
                continue;
            Deposit(uid, comp, args.User, args.Used, slot, row?.Revision ?? Guid.Empty);
            return;
        }
        Result(uid, args.User, "wh40k-faction-storage-full");
    }

    private void OnTransfer(EntityUid uid, FactionStorageComponent comp, FactionStorageTransferMessage args)
    {
        if (!ValidViewer(uid, comp, args.Actor) || !CanTransfer(uid, comp, args.Actor) ||
            args.Slot is < 0 or >= FactionStorageComponent.Capacity ||
            !TryGetAvailable(uid, comp, args.Actor, out var rows))
            return;

        rows.TryGetValue(args.Slot, out var row);
        if ((row?.Revision ?? Guid.Empty) != args.Revision)
        {
            Result(uid, args.Actor, "wh40k-faction-storage-stale");
            SendState(uid, comp, args.Actor);
            return;
        }
        if (args.Deposit)
        {
            if (row?.Payload != null || !_hands.TryGetActiveItem(args.Actor, out var held))
            {
                Result(uid, args.Actor, "wh40k-faction-storage-hold-item");
                return;
            }
            Deposit(uid, comp, args.Actor, held.Value, args.Slot, args.Revision);
        }
        else if (row?.Payload != null)
            Withdraw(uid, args.Actor, row);
    }

    private bool TryGetAvailable(EntityUid uid, FactionStorageComponent comp, EntityUid user,
        out Dictionary<int, Wh40kFactionStorageSlot> rows)
    {
        if (!_busy.Contains(comp.Faction.Id) && _cache.TryGetValue(comp.Faction.Id, out rows!))
            return true;
        rows = default!;
        Refresh(comp.Faction.Id);
        Result(uid, user, "wh40k-faction-storage-busy");
        return false;
    }

    // Apply these restrictions to the complete graph, including contents of otherwise permitted item slots.
    public bool CanStoreGraph(IReadOnlyList<EntityUid> entities, bool restoring = false)
    {
        foreach (var item in entities)
        {
            if (TerminatingOrDeleted(item) || HasComp<StorageComponent>(item) || HasComp<EntityStorageComponent>(item) ||
                HasComp<InventoryComponent>(item) || HasComp<UnremoveableComponent>(item) ||
                HasComp<PersistentInventoryOperationComponent>(item) || HasComp<Wh40kQuestReservationComponent>(item) ||
                HasComp<Wh40kQuestItemComponent>(item) || HasComp<Wh40kRewardDeliveryClaimComponent>(item) ||
                HasComp<Wh40kNpcGearComponent>(item) ||
                !restoring && TryComp(item, out MerchantItemTokenComponent? title) && !title.Verified)
                return false;
        }
        return true;
    }

    private void Deposit(EntityUid uid, FactionStorageComponent comp, EntityUid user, EntityUid item, int slot, Guid revision)
    {
        if (!_hands.IsHolding(user, item, out var hand) || !_hands.CanDropHeld(user, hand) || !HasComp<ItemComponent>(item))
            return;
        if (!CanStoreGraph([item]))
        {
            Result(uid, user, "wh40k-faction-storage-item-rejected");
            return;
        }
        try
        {
            var capture = _serializer.CaptureRoots(
                [new(PersistentInventoryRootKind.Hand, "faction-storage", item)],
                PersistentInventorySnapshotSerializer.DefaultPolicyId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Limits);
            if (!capture.IsSuccess || capture.Payload is not { Roots.Count: 1 } payload || capture.DeniedEntities.Count != 0 ||
                !CanStoreGraph(capture.CapturedEntities))
            {
                Result(uid, user, "wh40k-faction-storage-item-rejected");
                return;
            }

            // Refuse an item that cannot be round-tripped now, rather than accepting an unusable deposit.
            var verification = _serializer.RestoreIsolated(payload, Limits);
            if (!verification.Success || !CanStoreGraph(verification.Entities.Values.ToArray(), restoring: true))
            {
                _serializer.DeleteIsolated(verification);
                Log.Warning($"Faction storage deposit rejected: {verification.Error}");
                Result(uid, user, "wh40k-faction-storage-item-rejected");
                return;
            }
            _serializer.DeleteIsolated(verification);
            var packed = PersistentInventoryPayloadCodec.Pack(payload, Limits);
            var meta = MetaData(item);
            var row = new Wh40kFactionStorageSlot
            {
                Faction = comp.Faction.Id, Slot = slot, Revision = Guid.NewGuid(),
                Payload = packed.Data, Sha256 = packed.Sha256, Prototype = meta.EntityPrototype!.ID,
                Name = meta.EntityName != meta.EntityPrototype.Name ? meta.EntityName : null,
                Description = meta.EntityDescription != meta.EntityPrototype.Description ? meta.EntityDescription : null,
                Count = TryComp(item, out StackComponent? stack) ? stack.Count : 1,
            };
            StartTransfer(new Transfer
            {
                Row = row, ExpectedRevision = revision, Terminal = uid, Body = user, Item = item,
                Locked = capture.CapturedEntities.Append(user).ToList(), Deposit = true,
            });
        }
        catch (Exception error)
        {
            Log.Error($"Faction storage capture failed: {error}");
            Result(uid, user, "wh40k-faction-storage-item-rejected");
        }
    }

    private void Withdraw(EntityUid uid, EntityUid user, Wh40kFactionStorageSlot row)
    {
        if (!_hands.TryGetEmptyHand(user, out var hand))
        {
            Result(uid, user, "wh40k-faction-storage-free-hand");
            return;
        }
        PersistentInventoryRestoreResult? restored = null;
        try
        {
            var payload = PersistentInventoryPayloadCodec.Unpack(row.Payload!, row.Sha256!, Limits);
            restored = _serializer.RestoreIsolated(payload, Limits);
            if (!restored.Success || restored.Roots.Count != 1 || !CanStoreGraph(restored.Entities.Values.ToArray(), restoring: true))
                throw new InvalidDataException(restored.Error ?? "Invalid faction storage item graph.");
            _busy.Add(row.Faction);
            Broadcast(row.Faction);
            _sessions.TrackWork(PrepareWithdrawalAsync(uid, user, row, restored));
        }
        catch (Exception error)
        {
            if (restored != null)
                _serializer.DeleteIsolated(restored);
            Log.Error($"Faction storage item retained in cell {row.Faction}/{row.Slot}: {error}");
            Result(uid, user, "wh40k-faction-storage-restore-failed");
        }
    }

    private async Task PrepareWithdrawalAsync(EntityUid uid, EntityUid user, Wh40kFactionStorageSlot row,
        PersistentInventoryRestoreResult restored)
    {
        var started = false;
        Transfer? transfer = null;
        try
        {
            // Restoring a persistent snapshot deliberately fences merchant titles until their durable state is checked.
            var account = Comp<ActorComponent>(user).PlayerSession.UserId;
            await EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>().ReconcileSalesAsync(account);
            await _sessions.OnSimulationThread(() =>
            {
                var item = restored.Roots[0].Entity;
                if (!TryComp(uid, out FactionStorageComponent? storage) || storage.Faction.Id != row.Faction ||
                    !CanTransfer(uid, storage, user) ||
                    !CanStoreGraph(restored.Entities.Values.ToArray()))
                    return;
                if (!_hands.TryGetEmptyHand(user, out var hand) || !_hands.CanPickupAnyHand(user, item))
                {
                    Result(uid, user, "wh40k-faction-storage-free-hand");
                    return;
                }
                if (row.Name != null)
                    _metadata.SetEntityName(item, row.Name);
                if (row.Description != null)
                    _metadata.SetEntityDescription(item, row.Description);
                transfer = new Transfer
                {
                    Row = new() { Faction = row.Faction, Slot = row.Slot, Revision = Guid.NewGuid() },
                    ExpectedRevision = row.Revision, Terminal = uid, Body = user, Item = item,
                    Locked = restored.Entities.Values.Append(user).ToList(), Deposit = false, Restored = restored,
                };
                Lock(transfer);
                // Normal pickup restrictions were checked above. Force only bypasses our temporary operation locks.
                if (hand.Container == null || !_containers.Insert(item, hand.Container, force: true))
                    return;
                Lock(transfer);
                StartTransfer(transfer, alreadyLocked: true);
                started = true;
            });
        }
        catch (Exception error)
        {
            Log.Error($"Faction storage withdrawal preparation failed: {error}");
            await _sessions.OnSimulationThread(() => Result(uid, user, "wh40k-faction-storage-restore-failed"));
        }
        finally
        {
            if (!started)
            {
                await _sessions.OnSimulationThread(() =>
                {
                    _serializer.DeleteIsolated(restored);
                    if (transfer != null)
                        Unlock(transfer);
                    _busy.Remove(row.Faction);
                    Broadcast(row.Faction);
                });
            }
        }
    }

    private void StartTransfer(Transfer transfer, bool alreadyLocked = false)
    {
        if (!alreadyLocked)
            Lock(transfer);
        _busy.Add(transfer.Row.Faction);
        Broadcast(transfer.Row.Faction);
        // The existing shutdown pipeline drains these tasks before saving player inventories.
        _sessions.TrackWork(transfer.Completion.Task);
        _sessions.TrackWork(CommitAsync(transfer));
    }

    private void Lock(Transfer transfer)
    {
        var user = Comp<ActorComponent>(transfer.Body).PlayerSession.UserId;
        foreach (var item in transfer.Locked)
        {
            var marker = EnsureComp<PersistentInventoryOperationComponent>(item);
            marker.UserId = user;
            marker.OperationId = new(transfer.Row.Revision);
            if (item != transfer.Body && !MetaData(item).EntityPaused)
            {
                transfer.Paused.Add(item);
                _metadata.SetEntityPaused(item, true);
            }
        }
    }

    private void Unlock(Transfer transfer)
    {
        foreach (var item in transfer.Locked)
        {
            if (TryComp(item, out PersistentInventoryOperationComponent? marker) && marker.OperationId.Value == transfer.Row.Revision)
                RemComp<PersistentInventoryOperationComponent>(item);
            if (transfer.Paused.Contains(item) && !TerminatingOrDeleted(item))
                _metadata.SetEntityPaused(item, Transform(item).MapUid is { } map && _maps.IsPaused(map));
        }
    }

    private async Task CommitAsync(Transfer transfer)
    {
        bool committed;
        try
        {
            committed = await _db.WriteWh40kFactionStorageSlotAsync(transfer.Row, transfer.ExpectedRevision);
        }
        catch (Exception error)
        {
            Log.Error($"Faction storage transfer {transfer.Row.Revision} needs reconciliation: {error}");
            try
            {
                var stored = await _db.GetWh40kFactionStorageSlotAsync(transfer.Row.Faction, transfer.Row.Slot);
                committed = stored?.Revision == transfer.Row.Revision;
            }
            catch (Exception recoveryError)
            {
                Log.Error($"Faction storage transfer remains locked: {recoveryError}");
                await _sessions.OnSimulationThread(() =>
                {
                    _unresolved[transfer.Row.Revision] = transfer;
                    Result(transfer.Terminal, transfer.Body, "wh40k-faction-storage-pending");
                });
                return;
            }
        }

        await _sessions.OnSimulationThread(() =>
        {
            if (committed && transfer.Deposit && !TerminatingOrDeleted(transfer.Item))
                Del(transfer.Item);
            else if (!committed && transfer.Restored != null)
                _serializer.DeleteIsolated(transfer.Restored);
            Unlock(transfer);
            _busy.Remove(transfer.Row.Faction);
            _unresolved.Remove(transfer.Row.Revision);
            if (committed)
                _cache[transfer.Row.Faction][transfer.Row.Slot] = transfer.Row;
            else
                _cache.Remove(transfer.Row.Faction);
            transfer.Completion.TrySetResult();
            Result(transfer.Terminal, transfer.Body, committed
                ? transfer.Deposit ? "wh40k-faction-storage-deposited" : "wh40k-faction-storage-withdrawn"
                : "wh40k-faction-storage-stale");
            Broadcast(transfer.Row.Faction);
            if (!committed)
                Refresh(transfer.Row.Faction);
        });
    }

    private void Refresh(string faction)
    {
        if (_sessions.IsStopping ||
            _cache.ContainsKey(faction) && _refreshAt.GetValueOrDefault(faction) > _timing.CurTime || !_busy.Add(faction))
            return;
        _refreshAt[faction] = _timing.CurTime + TimeSpan.FromSeconds(1);
        Broadcast(faction);
        _sessions.TrackWork(LoadAsync(faction));
    }

    private async Task LoadAsync(string faction)
    {
        try
        {
            var rows = await _db.GetWh40kFactionStorageAsync(faction);
            await _sessions.OnSimulationThread(() => _cache[faction] = rows.ToDictionary(row => row.Slot));
        }
        catch (Exception error)
        {
            await _sessions.OnSimulationThread(() => _cache.Remove(faction));
            Log.Error($"Faction storage {faction} unavailable: {error}");
        }
        finally
        {
            await _sessions.OnSimulationThread(() =>
            {
                _busy.Remove(faction);
                Broadcast(faction);
            });
        }
    }

    private void SendState(EntityUid uid, FactionStorageComponent comp, EntityUid user)
    {
        if (!ValidViewer(uid, comp, user))
            return;
        var entries = new List<FactionStorageSlotState>();
        if (_cache.TryGetValue(comp.Faction.Id, out var rows))
        {
            for (var slot = 0; slot < FactionStorageComponent.Capacity; slot++)
            {
                rows.TryGetValue(slot, out var row);
                entries.Add(new(slot, row?.Revision ?? Guid.Empty, row?.Prototype, row?.Name, row?.Description, row?.Count ?? 0));
            }
        }
        _ui.ServerSendUiMessage(uid, FactionStorageUiKey.Key,
            new FactionStorageStateMessage(comp.Faction.Id, entries, _busy.Contains(comp.Faction.Id) || entries.Count == 0), user);
    }

    private void Broadcast(string faction)
    {
        var query = EntityQueryEnumerator<FactionStorageComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Faction.Id != faction)
                continue;
            foreach (var user in _ui.GetActors(uid, FactionStorageUiKey.Key).ToArray())
                SendState(uid, comp, user);
        }
    }

    private void Result(EntityUid uid, EntityUid user, string message)
    {
        if (!TerminatingOrDeleted(uid) && !TerminatingOrDeleted(user) && HasComp<ActorComponent>(user))
            _popup.PopupEntity(_culture.GetPlayerString(user, message), uid, user);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_nextUpdate > _timing.CurTime)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(5);
        foreach (var (operation, transfer) in _unresolved.ToArray())
        {
            _unresolved.Remove(operation);
            _sessions.TrackWork(CommitAsync(transfer));
        }
        var query = EntityQueryEnumerator<FactionStorageComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            foreach (var user in _ui.GetActors(uid, FactionStorageUiKey.Key).ToArray())
                ValidViewer(uid, comp, user);
        }
    }
}
