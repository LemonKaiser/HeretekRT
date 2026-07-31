using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Shared.GameTicking;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Drains the persistent WH40K reward outbox through a fenced DB claim. Every spawned
/// entity carries a delivery/index marker and remains non-interactable until the DB
/// acknowledges the delivery, allowing retries to reconcile instead of spawning twice.
/// </summary>
public sealed class Wh40kRewardDeliverySystem : EntitySystem
{
    private static readonly EntProtoId CashPrototype = "SpaceCash";

    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private ITaskManager _taskManager = default!;

    private readonly ConcurrentDictionary<NetUserId, byte> _activeDeliveries = new();
    private readonly ConcurrentDictionary<NetUserId, DateTime> _retryAt = new();
    private readonly HashSet<EntityUid> _internalPlacement = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, PickupAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, DropAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, ThrowAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, UseAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, AttackAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, InteractionAttemptEvent>(CancelInteraction);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, BeingEquippedAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, IsEquippingAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, BeingUnequippedAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, IsUnequippingAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, ContainerIsInsertingAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, ContainerGettingInsertedAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, ContainerIsRemovingAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, ContainerGettingRemovedAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, StorageInteractAttemptEvent>(CancelStorage);
        SubscribeLocalEvent<Wh40kRewardDeliveryClaimComponent, StorageInteractUsingAttemptEvent>(CancelStorageUsing);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = DateTime.UtcNow;
        foreach (var (userId, retryAt) in _retryAt)
        {
            if (retryAt > now ||
                !_retryAt.TryRemove(new KeyValuePair<NetUserId, DateTime>(userId, retryAt)))
            {
                continue;
            }

            _ = TryDeliverForUserAsync(userId);
        }
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        _ = TryDeliverForUserAsync(args.Player.UserId);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        _ = TryDeliverForUserAsync(args.Player.UserId);
    }

    public async Task TryDeliverForUserAsync(NetUserId userId, CancellationToken cancel = default)
    {
        if (!_activeDeliveries.TryAdd(userId, 0))
            return;

        try
        {
            var attached = await GetAttachedMobAsync(userId);
            if (attached == null)
                return;

            await ReconcileExistingMarkersAsync(userId, cancel);
            var deliveries = await _db.GetPendingWh40kRewardDeliveriesAsync(userId, cancel);
            foreach (var delivery in deliveries)
            {
                attached = await GetAttachedMobAsync(userId);
                if (attached == null)
                    return;

                var claim = await _db.ClaimWh40kRewardDeliveryAsync(userId, delivery.Id, cancel);
                if (claim == null)
                    continue;

                await DeliverClaimAsync(userId, attached.Value, claim, cancel);
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to deliver pending WH40K rewards for {userId}: {exception}");
            ScheduleRetry(userId, TimeSpan.FromSeconds(5));
        }
        finally
        {
            _activeDeliveries.TryRemove(userId, out _);
        }
    }

    private async Task ReconcileExistingMarkersAsync(NetUserId userId, CancellationToken cancel)
    {
        var claims = await RunOnMainThread(() =>
        {
            var found = new Dictionary<long, int>();
            var query = EntityQueryEnumerator<Wh40kRewardDeliveryClaimComponent>();
            while (query.MoveNext(out _, out var marker))
            {
                if (marker.UserId != userId.UserId || marker.DeliveryId <= 0 || marker.ClaimAttempt <= 0)
                    continue;

                found[marker.DeliveryId] = Math.Max(
                    found.GetValueOrDefault(marker.DeliveryId),
                    marker.ClaimAttempt);
            }

            return found;
        });

        foreach (var (deliveryId, claimAttempt) in claims)
        {
            var complete = await RunOnMainThread(() =>
            {
                var entities = FindClaimEntities(userId, deliveryId, out var duplicateIndices);
                return IsCompleteClaimSet(entities, duplicateIndices, claimAttempt);
            });
            if (!complete)
            {
                ScheduleRetry(userId, TimeSpan.FromMinutes(5));
                continue;
            }

            if (!await _db.CompleteWh40kRewardDeliveryClaimAsync(
                    userId,
                    deliveryId,
                    claimAttempt,
                    delivered: true,
                    CancellationToken.None))
            {
                cancel.ThrowIfCancellationRequested();
                ScheduleRetry(userId, TimeSpan.FromSeconds(5));
                continue;
            }

            await RunOnMainThread(() =>
            {
                var entities = FindClaimEntities(userId, deliveryId, out var duplicateIndices);
                if (!IsCompleteClaimSet(entities, duplicateIndices, claimAttempt))
                    return;

                foreach (var (_, entity) in entities)
                    RemCompDeferred<Wh40kRewardDeliveryClaimComponent>(entity);
            });
        }
    }

    private async Task<EntityUid?> GetAttachedMobAsync(NetUserId userId)
    {
        return await RunOnMainThread<EntityUid?>(() =>
        {
            if (!_players.TryGetSessionById(userId, out var session) ||
                session.AttachedEntity is not { Valid: true } mob ||
                !Exists(mob))
            {
                return null;
            }

            return mob;
        });
    }

    private void ScheduleRetry(NetUserId userId, TimeSpan delay)
    {
        var retryAt = DateTime.UtcNow + delay;
        _retryAt.AddOrUpdate(
            userId,
            retryAt,
            (_, current) => current <= retryAt ? current : retryAt);
    }

    private async Task DeliverClaimAsync(
        NetUserId userId,
        EntityUid mob,
        Wh40kRewardDeliveryRecord claim,
        CancellationToken cancel)
    {
        bool materialized;
        try
        {
            materialized = await RunOnMainThread(() => TryMaterializeClaim(userId, mob, claim));
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to materialize WH40K reward delivery {claim.Id}: {exception}");
            await ReleaseClaimAsync(userId, claim, cancel);
            ScheduleRetry(userId, TimeSpan.FromSeconds(5));
            return;
        }

        if (!materialized)
        {
            await ReleaseClaimAsync(userId, claim, cancel);
            return;
        }

        if (!await CompleteClaimWithRetryAsync(userId, claim, cancel))
        {
            Log.Error(
                $"WH40K reward delivery {claim.Id} remains claimed; tagged entities stay locked for reconciliation.");
            ScheduleRetry(userId, TimeSpan.FromSeconds(5));
            return;
        }

        await RunOnMainThread(() =>
        {
            var entities = FindClaimEntities(userId, claim.Id, out var duplicateIndices);
            if (!IsCompleteClaimSet(entities, duplicateIndices, claim.AttemptCount))
                return;

            foreach (var (_, entity) in entities)
                RemCompDeferred<Wh40kRewardDeliveryClaimComponent>(entity);
        });
    }

    private bool TryMaterializeClaim(
        NetUserId userId,
        EntityUid mob,
        Wh40kRewardDeliveryRecord claim)
    {
        if (!Exists(mob))
            throw new InvalidOperationException("Reward recipient no longer exists.");

        var expectedEntities = GetExpectedEntityCount(claim);
        if (expectedEntities == null)
            return false;

        var existing = FindClaimEntities(userId, claim.Id, out var duplicateIndices);
        if (duplicateIndices ||
            existing.Any(pair =>
                pair.Value.Comp.ExpectedEntities != expectedEntities.Value ||
                pair.Key < 0 ||
                pair.Key >= expectedEntities.Value))
        {
            Log.Error($"WH40K reward delivery {claim.Id} has inconsistent ECS claim markers.");
            return false;
        }

        foreach (var (_, entity) in existing)
        {
            entity.Comp.ClaimAttempt = claim.AttemptCount;
            Dirty(entity);
        }

        for (var index = 0; index < expectedEntities.Value; index++)
        {
            if (existing.ContainsKey(index))
                continue;

            var entity = SpawnClaimEntity(mob, claim, index, expectedEntities.Value);
            existing.Add(index, entity);
        }

        return IsCompleteClaimSet(
            existing,
            duplicateIndices: false,
            claimAttempt: claim.AttemptCount);
    }

    private int? GetExpectedEntityCount(Wh40kRewardDeliveryRecord delivery)
    {
        switch (delivery.RewardType)
        {
            case Wh40kLevelRewardCatalog.CurrencyRewardType:
                if (delivery.Amount is <= 0 or > int.MaxValue)
                {
                    Log.Error($"WH40K reward delivery {delivery.Id} has invalid currency amount.");
                    return null;
                }

                return 1;
            case Wh40kLevelRewardCatalog.ItemRewardType:
                if (delivery.PrototypeId == null ||
                    !_prototypes.TryIndex<EntityPrototype>(delivery.PrototypeId, out _) ||
                    delivery.Amount is <= 0 or > int.MaxValue)
                {
                    Log.Error(
                        $"WH40K reward delivery {delivery.Id} references invalid prototype or amount " +
                        $"'{delivery.PrototypeId ?? "<null>"}'.");
                    return null;
                }

                return checked((int) delivery.Amount);
            default:
                Log.Error($"WH40K reward delivery {delivery.Id} has unknown type '{delivery.RewardType}'.");
                return null;
        }
    }

    private Entity<Wh40kRewardDeliveryClaimComponent> SpawnClaimEntity(
        EntityUid mob,
        Wh40kRewardDeliveryRecord claim,
        int index,
        int expectedEntities)
    {
        var prototype = claim.RewardType == Wh40kLevelRewardCatalog.CurrencyRewardType
            ? CashPrototype.Id
            : claim.PrototypeId!;
        var entity = Spawn(prototype, Transform(mob).Coordinates);
        var marker = EnsureComp<Wh40kRewardDeliveryClaimComponent>(entity);
        marker.UserId = claim.UserId.UserId;
        marker.DeliveryId = claim.Id;
        marker.ClaimAttempt = claim.AttemptCount;
        marker.EntityIndex = index;
        marker.ExpectedEntities = expectedEntities;
        Dirty(entity, marker);

        if (claim.RewardType == Wh40kLevelRewardCatalog.CurrencyRewardType)
            _stacks.SetCount(entity, checked((int) claim.Amount));

        _internalPlacement.Add(entity);
        try
        {
            TryPlaceInHandsOrStorage(mob, entity);
        }
        finally
        {
            _internalPlacement.Remove(entity);
        }

        return (entity, marker);
    }

    private Dictionary<int, Entity<Wh40kRewardDeliveryClaimComponent>> FindClaimEntities(
        NetUserId userId,
        long deliveryId,
        out bool duplicateIndices)
    {
        duplicateIndices = false;
        var found = new Dictionary<int, Entity<Wh40kRewardDeliveryClaimComponent>>();
        var query = EntityQueryEnumerator<Wh40kRewardDeliveryClaimComponent>();
        while (query.MoveNext(out var uid, out var marker))
        {
            if (marker.UserId != userId.UserId || marker.DeliveryId != deliveryId)
                continue;

            if (!found.TryAdd(marker.EntityIndex, (uid, marker)))
            {
                duplicateIndices = true;
                Log.Error(
                    $"WH40K reward delivery {deliveryId} has duplicate entity index {marker.EntityIndex}; " +
                    "the claim remains locked.");
            }
        }

        return found;
    }

    private bool IsCompleteClaimSet(
        Dictionary<int, Entity<Wh40kRewardDeliveryClaimComponent>> entities,
        bool duplicateIndices,
        int claimAttempt)
    {
        if (duplicateIndices || entities.Count == 0)
            return false;

        var expectedEntities = entities.First().Value.Comp.ExpectedEntities;
        if (expectedEntities <= 0 || entities.Count != expectedEntities)
            return false;

        for (var index = 0; index < expectedEntities; index++)
        {
            if (!entities.TryGetValue(index, out var entity) ||
                entity.Comp.ExpectedEntities != expectedEntities ||
                entity.Comp.ClaimAttempt != claimAttempt)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> CompleteClaimWithRetryAsync(
        NetUserId userId,
        Wh40kRewardDeliveryRecord claim,
        CancellationToken cancel)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (await _db.CompleteWh40kRewardDeliveryClaimAsync(
                        userId,
                        claim.Id,
                        claim.AttemptCount,
                        delivered: true,
                        CancellationToken.None))
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                Log.Error(
                    $"WH40K reward delivery {claim.Id} completion retry {attempt + 1} failed: " +
                    $"{exception.GetType().Name}.");
            }

            cancel.ThrowIfCancellationRequested();
        }

        return false;
    }

    private async Task ReleaseClaimAsync(
        NetUserId userId,
        Wh40kRewardDeliveryRecord claim,
        CancellationToken cancel)
    {
        try
        {
            await _db.CompleteWh40kRewardDeliveryClaimAsync(
                userId,
                claim.Id,
                claim.AttemptCount,
                delivered: false,
                cancel);
        }
        catch (Exception exception)
        {
            Log.Error(
                $"WH40K reward delivery {claim.Id} claim release failed: {exception.GetType().Name}.");
        }
    }

    private void TryPlaceInHandsOrStorage(EntityUid mob, EntityUid item)
    {
        if (_hands.TryPickupAnyHand(mob, item, checkActionBlocker: false))
            return;

        var slots = _inventory.GetSlotEnumerator(mob);
        while (slots.MoveNext(out var container))
        {
            if (container.ContainedEntity is not { } equipped ||
                !TryComp<StorageComponent>(equipped, out var storage))
            {
                continue;
            }

            if (_storage.Insert(equipped, item, out _, storageComp: storage, playSound: false))
                return;
        }
    }

    private async Task<T> RunOnMainThread<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskManager.RunOnMainThread(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return await completion.Task;
    }

    private async Task RunOnMainThread(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskManager.RunOnMainThread(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        await completion.Task;
    }

    private void Cancel(
        EntityUid uid,
        Wh40kRewardDeliveryClaimComponent component,
        CancellableEntityEventArgs args)
    {
        if (!_internalPlacement.Contains(uid))
            args.Cancel();
    }

    private void CancelInteraction(
        Entity<Wh40kRewardDeliveryClaimComponent> entity,
        ref InteractionAttemptEvent args)
    {
        if (!_internalPlacement.Contains(entity))
            args.Cancelled = true;
    }

    private void CancelStorage(
        Entity<Wh40kRewardDeliveryClaimComponent> entity,
        ref StorageInteractAttemptEvent args)
    {
        if (!_internalPlacement.Contains(entity))
            args.Cancelled = true;
    }

    private void CancelStorageUsing(
        Entity<Wh40kRewardDeliveryClaimComponent> entity,
        ref StorageInteractUsingAttemptEvent args)
    {
        if (!_internalPlacement.Contains(entity))
            args.Cancelled = true;
    }
}
