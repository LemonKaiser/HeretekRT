using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Server._WH40K.Progression;
using Content.Server._WH40K.PersistentInventory;
using Content.Server._WH40K.Quests;
using Content.Shared._WH40K.Merchant;
using Content.Shared._WH40K.Npcs;
using Content.Shared._WH40K.Quests;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Shared.Interaction.Components;

namespace Content.Server._WH40K.Merchant;

public enum MerchantSalePhase { Locked, Reserved, Paid, Released }

public sealed partial class MerchantSystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private PersistentInventorySnapshotSerializer _serializer = default!;
    [Dependency] private Wh40kRewardDeliverySystem _delivery = default!;
    private readonly HashSet<NetUserId> _selling = new();
    private readonly HashSet<EntityUid> _checking = new();
    private readonly Dictionary<NetUserId, Quote> _quotes = new();
    private sealed record Quote(Guid Id, EntityUid Merchant, EntityUid Body, EntityUid Item, Guid Token,
        string Product, int Price, TimeSpan Expires, Guid Catalog);
    public bool IsSelling(NetUserId user) => _selling.Contains(user);
    public event Action<MerchantSalePhase>? SalePhaseChanged;
    private float _saleRecoveryTimer;

    private void InitializeBuyback()
    {
        SubscribeLocalEvent<MerchantComponent, MerchantAppraiseMessage>(OnAppraise);
        SubscribeLocalEvent<MerchantComponent, MerchantSellMessage>(OnSell);
        SubscribeLocalEvent<ItemComponent, MapInitEvent>(AssignTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, GettingPickedUpAttemptEvent>(GuardTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, DropAttemptEvent>(GuardTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, ThrowAttemptEvent>(GuardTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, GettingUsedAttemptEvent>(GuardTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, AttackAttemptEvent>(GuardTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, GettingInteractedWithAttemptEvent>(GuardTitleInteraction);
        SubscribeLocalEvent<MerchantItemTokenComponent, BeingEquippedAttemptEvent>(GuardTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, ContainerGettingInsertedAttemptEvent>(GuardTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, ContainerGettingRemovedAttemptEvent>(GuardTitle);
        SubscribeLocalEvent<MerchantItemTokenComponent, ThrowItemAttemptEvent>(GuardThrow);
        SubscribeLocalEvent<ActorComponent, AttackAttemptEvent>(GuardWeapon);
        SubscribeLocalEvent<PlayerAttachedEvent>(ev => _sessions.TrackWork(ReconcileSalesAsync(ev.Player.UserId)));
    }

    private void AssignTitle(EntityUid uid, ItemComponent comp, MapInitEvent ev)
    {
        if (MetaData(uid).EntityPrototype is not {} proto || !_prototypes.EnumeratePrototypes<MerchantValuationPrototype>()
            .Any(valuation => valuation.Prices.ContainsKey(new EntProtoId(proto.ID)))) return;
        var title = EnsureComp<MerchantItemTokenComponent>(uid);
        if (title.Token == Guid.Empty) title.Token = Guid.NewGuid();
    }
    public void AssignLegacyTitle(EntityUid uid, string snapshotEntity)
    {
        if (MetaData(uid).EntityPrototype is not {} proto || !_prototypes.EnumeratePrototypes<MerchantValuationPrototype>()
            .Any(valuation => valuation.Prices.ContainsKey(new EntProtoId(proto.ID)))) return;
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("merchant-legacy:" + snapshotEntity));
        var title = EnsureComp<MerchantItemTokenComponent>(uid);
        title.Token = new Guid(bytes.AsSpan(0, 16)); title.Verified = false;
    }
    private void GuardTitle(EntityUid uid, MerchantItemTokenComponent comp, CancellableEntityEventArgs ev)
    { if (!comp.Verified && !_submission.IsReservationPlacement(uid)) ev.Cancel(); }
    private void GuardThrow(Entity<MerchantItemTokenComponent> ent, ref ThrowItemAttemptEvent ev)
    { if (!ent.Comp.Verified) ev.Cancelled = true; }
    private void GuardTitleInteraction(Entity<MerchantItemTokenComponent> ent, ref GettingInteractedWithAttemptEvent ev)
    { if (!ent.Comp.Verified) ev.Cancelled = true; }
    private void GuardWeapon(EntityUid uid, ActorComponent comp, AttackAttemptEvent ev)
    {
        if (ev.Weapon is {} weapon && (HasComp<Wh40kQuestReservationComponent>(weapon.Owner) ||
            HasComp<PersistentInventoryOperationComponent>(weapon.Owner) ||
            TryComp(weapon.Owner, out MerchantItemTokenComponent? title) && !title.Verified)) ev.Cancel();
    }

    private bool Eligible(EntityUid body, EntityUid item)
    {
        if (TerminatingOrDeleted(item) || !_hands.IsHolding(body, item, out var hand) ||
            HasComp<Content.Server._WH40K.SectorMap.Components.KoronusStationPropertyOriginComponent>(item) ||
            !_hands.CanDropHeld(body, hand, checkActionBlocker: false) || !TryComp(item, out MerchantItemTokenComponent? title) ||
            !title.Verified || title.Token == Guid.Empty || HasComp<Wh40kQuestReservationComponent>(item) ||
            HasComp<Wh40kQuestItemComponent>(item) || HasComp<Wh40kNpcGearComponent>(item) || HasComp<UnremoveableComponent>(item) ||
            HasComp<Wh40kRewardDeliveryClaimComponent>(item) || HasComp<PersistentInventoryOperationComponent>(item) ||
            HasComp<StackComponent>(item)) return false;
        // Only leaf items: never buy a bag, loaded gun or outfit containing someone else's goods.
        if (TryComp(item, out ContainerManagerComponent? containers) && containers.Containers.Values.Any(c => c.ContainedEntities.Count != 0)) return false;
        if (!TryComp(body, out ActorComponent? actor) || !_sessions.IsReady(actor.PlayerSession.UserId)) return false;
        if (_sessions.TryGetStatus(actor.PlayerSession.UserId, out var states))
        {
            var progress = EntityManager.System<Wh40kQuestProgressSystem>();
            if (states.Any(state => state.Snapshot != null && state.Contract.State is 0 or 1 &&
                state.Snapshot.Objectives.Any(objective => (objective.Kind.IsSubmission() || objective.Kind == Wh40kQuestObjectiveKind.CollectItem) &&
                    progress.Matches(item, objective.Target.SubmissionTarget(), state.Contract.Id, objective.Id)))) return false;
        }
        return true;
    }

    private void OnAppraise(EntityUid uid, MerchantComponent comp, MerchantAppraiseMessage ev)
    {
        if (!ValidViewer(uid, comp, ev.Actor) || !CanTrade(ev.Actor) || comp.Buyback is not {} catalog ||
            !_prototypes.TryIndex(catalog, out var valuation) || !TryComp(ev.Actor, out ActorComponent? actor) ||
            !actor.PlayerSession.Channel.AuthType.HasStaticUserId()) return;
        _quotes.Remove(actor.PlayerSession.UserId);
        var item = _hands.EnumerateHeld(ev.Actor).FirstOrDefault(item => Eligible(ev.Actor, item) &&
            MetaData(item).EntityPrototype is {} proto && valuation.Prices.TryGetValue(new EntProtoId(proto.ID), out var price) && price is > 0 and <= 1000000);
        if (!item.IsValid()) { Result(uid, ev.Actor, Guid.Empty, "wh40k-service-appraisal-refused"); return; }
        var product = MetaData(item).EntityPrototype!.ID;
        var quote = new Quote(Guid.NewGuid(), uid, ev.Actor, item, Comp<MerchantItemTokenComponent>(item).Token,
            product, valuation.Prices[new EntProtoId(product)], _timing.CurTime + TimeSpan.FromSeconds(30), comp.CatalogId);
        _quotes[actor.PlayerSession.UserId] = quote;
        _ui.ServerSendUiMessage(uid, MerchantUiKey.Key, new MerchantAppraisalState(quote.Id, product, quote.Price), ev.Actor);
    }

    private void OnSell(EntityUid uid, MerchantComponent comp, MerchantSellMessage ev)
    {
        if (!ValidViewer(uid, comp, ev.Actor) || !TryComp(ev.Actor, out ActorComponent? actor) || ev.Operation == Guid.Empty ||
            !_quotes.TryGetValue(actor.PlayerSession.UserId, out var quote) || quote.Id != ev.Quote || quote.Merchant != uid ||
            quote.Body != ev.Actor || !CanTrade(ev.Actor) || quote.Expires <= _timing.CurTime || quote.Catalog != comp.CatalogId ||
            !Eligible(ev.Actor, quote.Item) || Comp<MerchantItemTokenComponent>(quote.Item).Token != quote.Token)
        { Result(uid, ev.Actor, ev.Operation, "wh40k-service-stale"); return; }
        var user = actor.PlayerSession.UserId;
        if (!_selling.Add(user)) return;
        var marker = EnsureComp<Wh40kQuestReservationComponent>(quote.Item);
        marker.Submission = ev.Operation; marker.UserId = user.UserId; marker.Units = marker.OriginalCount = 1; marker.Merchant = true;
        _sessions.TrackWork(SellAsync(user, quote, ev.Operation));
    }

    private async Task SellAsync(NetUserId user, Quote quote, Guid operation)
    {
        var generation = _sessions.Generation;
        var paid = false;
        var durable = false;
        try
        {
            Wh40kMerchantSale? sale = null;
            await _sessions.OnSimulationThread(() =>
            {
                SalePhaseChanged?.Invoke(MerchantSalePhase.Locked);
                var capture = _serializer.CaptureRoots([new PersistentInventorySnapshotSerializer.CaptureRoot(
                    PersistentInventoryRootKind.Hand, "merchant", quote.Item)], "Wh40kDefault", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new());
                if (capture.IsSuccess) sale = new() { Token = quote.Token, UserId = user.UserId, Operation = operation,
                    Merchant = MetaData(quote.Merchant).EntityPrototype!.ID, Product = quote.Product, Price = quote.Price,
                    PayloadJson = JsonSerializer.Serialize(capture.Payload), CreatedAt = DateTime.UtcNow };
            });
            if (sale == null) return;
            durable = await _db.ReserveWh40kMerchantSaleAsync(sale);
            if (!durable) return;
            var valid = false;
            await _sessions.OnSimulationThread(() =>
            {
                SalePhaseChanged?.Invoke(MerchantSalePhase.Reserved);
                valid = generation == _sessions.Generation && !_sessions.IsStopping && Exists(quote.Item) &&
                    Exists(quote.Merchant) && TryComp(quote.Merchant, out MerchantComponent? merchant) &&
                    ValidViewer(quote.Merchant, merchant, quote.Body) && _hands.IsHolding(quote.Body, quote.Item) &&
                    !_quests.IsBusy(user) && !_submission.IsBusy(user) &&
                    !HasComp<PersistentInventoryOperationComponent>(quote.Body) &&
                    !_dialogue.IsInDialogue(Comp<ActorComponent>(quote.Body).PlayerSession) &&
                    Comp<Wh40kQuestReservationComponent>(quote.Item).Submission == operation;
            });
            if (valid) paid = await _db.FinishWh40kMerchantSaleAsync(user.UserId, quote.Token, operation, true);
        }
        catch (Exception error) { Log.Error($"Merchant sale {operation} requires reconciliation: {error}"); }
        finally
        {
            var confirmed = false;
            try
            {
                // Read after an ambiguous response. A failed/unconfirmed payment never authorizes item deletion.
                var stored = await _db.GetWh40kMerchantSaleAsync(quote.Token);
                paid = stored is { State: 1 };
                if (stored is { State: 0 } && stored.Operation == operation)
                    confirmed = await _db.FinishWh40kMerchantSaleAsync(user.UserId, quote.Token, operation, false);
                else confirmed = !durable || stored is { State: 1 or 2 };
            }
            catch (Exception error) { Log.Error($"Merchant item remains fenced: {error}"); }
            await _sessions.OnSimulationThread(() =>
            {
                if (confirmed && Exists(quote.Item))
                {
                    if (paid) { SalePhaseChanged?.Invoke(MerchantSalePhase.Paid); QueueDel(quote.Item); }
                    else RemComp<Wh40kQuestReservationComponent>(quote.Item);
                    SalePhaseChanged?.Invoke(MerchantSalePhase.Released);
                }
                _selling.Remove(user); _quotes.Remove(user);
                Result(quote.Merchant, quote.Body, operation, confirmed ? paid ? "wh40k-service-sold" : "wh40k-service-failed" : "wh40k-service-pending");
            });
            if (paid && confirmed) await _delivery.TryDeliverForUserAsync(user);
        }
    }

    private void UpdateSaleRecovery(float frameTime)
    {
        _saleRecoveryTimer += frameTime;
        if (_saleRecoveryTimer < 1 || _sessions.IsStopping) return;
        _saleRecoveryTimer = 0;
        foreach (var (user, quote) in _quotes.ToArray()) if (quote.Expires <= _timing.CurTime) _quotes.Remove(user);
        var query = EntityQueryEnumerator<MerchantItemTokenComponent>();
        var count = 0;
        while (query.MoveNext(out var uid, out var token))
        {
            if (token.Verified && (!TryComp(uid, out Wh40kQuestReservationComponent? marker) || !marker.Merchant)) continue;
            if (TryComp(uid, out Wh40kQuestReservationComponent? reservation) && _selling.Contains(new NetUserId(reservation.UserId))) continue;
            if (!_checking.Add(uid)) continue;
            _sessions.TrackWork(CheckTitleAsync(uid, token.Token));
            if (++count == 32) break;
        }
    }

    private async Task CheckTitleAsync(EntityUid uid, Guid token)
    {
        try
        {
            var sale = await _db.GetWh40kMerchantSaleAsync(token);
            if (sale is { State: 0 } && !await _db.FinishWh40kMerchantSaleAsync(sale.UserId, sale.Token, sale.Operation, false)) return;
            await _sessions.OnSimulationThread(() =>
            {
                if (!TryComp(uid, out MerchantItemTokenComponent? marker) || marker.Token != token) return;
                if (sale is { State: 1 }) QueueDel(uid);
                else
                {
                    marker.Verified = true;
                    if (TryComp(uid, out Wh40kQuestReservationComponent? reservation) && reservation.Merchant)
                        RemComp<Wh40kQuestReservationComponent>(uid);
                }
            });
        }
        catch (Exception error) { Log.Error($"Merchant title recovery retained {token}: {error.GetType().Name}"); }
        finally { await _sessions.OnSimulationThread(() => _checking.Remove(uid)); }
    }

    public async Task ReconcileSalesAsync(NetUserId user)
    {
        var titles = new List<(EntityUid Item, Guid Token)>();
        await _sessions.OnSimulationThread(() =>
        {
            var query = EntityQueryEnumerator<MerchantItemTokenComponent>();
            while (query.MoveNext(out var uid, out var token))
                if ((!token.Verified || TryComp(uid, out Wh40kQuestReservationComponent? marker) && marker.Merchant && marker.UserId == user.UserId) &&
                    !_selling.Contains(user) && _checking.Add(uid)) titles.Add((uid, token.Token));
        });
        foreach (var (item, token) in titles) await CheckTitleAsync(item, token);
    }
}
