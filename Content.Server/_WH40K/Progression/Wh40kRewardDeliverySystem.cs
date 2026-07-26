using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Drains the persistent WH40K reward outbox when an account has a live mob.
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

    private readonly HashSet<NetUserId> _activeDeliveries = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    private async void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        await TryDeliverForUserAsync(args.Player.UserId);
    }

    private async void OnPlayerAttached(PlayerAttachedEvent args)
    {
        await TryDeliverForUserAsync(args.Player.UserId);
    }

    public async Task TryDeliverForUserAsync(NetUserId userId, CancellationToken cancel = default)
    {
        if (!_activeDeliveries.Add(userId))
            return;

        try
        {
            if (!_players.TryGetSessionById(userId, out var session) ||
                session.AttachedEntity is not { Valid: true } mob ||
                !Exists(mob))
            {
                return;
            }

            var deliveries = await _db.GetPendingWh40kRewardDeliveriesAsync(userId, cancel);
            foreach (var delivery in deliveries)
            {
                if (session.AttachedEntity != mob || !Exists(mob))
                    return;

                await DeliverAsync(userId, mob, delivery, cancel);
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to deliver pending WH40K rewards for {userId}: {exception}");
        }
        finally
        {
            _activeDeliveries.Remove(userId);
        }
    }

    private async Task DeliverAsync(
        NetUserId userId,
        EntityUid mob,
        Wh40kRewardDeliveryRecord delivery,
        CancellationToken cancel)
    {
        var spawned = new List<EntityUid>();
        try
        {
            switch (delivery.RewardType)
            {
                case Wh40kLevelRewardCatalog.CurrencyRewardType:
                    SpawnCurrency(mob, checked((int) delivery.Amount), spawned);
                    break;
                case Wh40kLevelRewardCatalog.ItemRewardType:
                    if (delivery.PrototypeId == null ||
                        !_prototypes.TryIndex<EntityPrototype>(delivery.PrototypeId, out _))
                    {
                        await _db.RecordWh40kRewardDeliveryAttemptAsync(userId, delivery.Id, false, cancel);
                        Log.Error(
                            $"WH40K reward delivery {delivery.Id} references missing prototype " +
                            $"'{delivery.PrototypeId ?? "<null>"}'.");
                        return;
                    }

                    for (var index = 0; index < delivery.Amount; index++)
                        SpawnItem(mob, delivery.PrototypeId, spawned);
                    break;
                default:
                    await _db.RecordWh40kRewardDeliveryAttemptAsync(userId, delivery.Id, false, cancel);
                    Log.Error($"WH40K reward delivery {delivery.Id} has unknown type '{delivery.RewardType}'.");
                    return;
            }

            if (await _db.RecordWh40kRewardDeliveryAttemptAsync(userId, delivery.Id, true, cancel))
                return;
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to process WH40K reward delivery {delivery.Id}: {exception}");
        }

        foreach (var entity in spawned)
        {
            if (Exists(entity))
                QueueDel(entity);
        }
    }

    private void SpawnCurrency(EntityUid mob, int amount, ICollection<EntityUid> spawned)
    {
        var cash = Spawn(CashPrototype, Transform(mob).Coordinates);
        spawned.Add(cash);
        _stacks.SetCount(cash, amount);
        TryPlaceInHandsOrStorage(mob, cash);
    }

    private void SpawnItem(EntityUid mob, string prototypeId, ICollection<EntityUid> spawned)
    {
        var item = Spawn(prototypeId, Transform(mob).Coordinates);
        spawned.Add(item);
        TryPlaceInHandsOrStorage(mob, item);
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

        // The entity was spawned at the mob's coordinates and deliberately remains there.
    }
}
