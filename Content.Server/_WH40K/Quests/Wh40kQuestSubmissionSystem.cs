using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server._WH40K.Npcs;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Server.Database;
using Content.Server.Stack;
using Content.Shared._WH40K.Quests;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Server._WH40K.PersistentInventory;
using Content.Shared.Stacks;
using Content.Shared.Throwing;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Quests;

public enum Wh40kQuestSubmissionPhase { Locked, Reserved, Escrowed, Captured, Committed, Consumed }

/// <summary>Exact-unit, fenced hand/equipment submission. Unprovable crash custody is quarantined.</summary>
public sealed partial class Wh40kQuestSubmissionSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;
    [Dependency] private Wh40kQuestProgressSystem _progress = default!;
    [Dependency] private Wh40kNpcAnchorSystem _anchors = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private PersistentInventorySnapshotSerializer _serializer = default!;
    private readonly HashSet<NetUserId> _busy = new();
    private readonly HashSet<EntityUid> _internal = new();
    public bool IsBusy(NetUserId user) => _busy.Contains(user);
    public event Action<Wh40kQuestSubmissionPhase>? PhaseChanged;
    private sealed record Reservation(EntityUid Item, int Units, int OriginalCount, BaseContainer Container);

    internal T WithReservationPlacement<T>(IEnumerable<EntityUid> items, Func<T> action)
    {
        var added = items.Where(item => (HasComp<Wh40kQuestReservationComponent>(item) ||
            TryComp(item, out Content.Shared._WH40K.Merchant.MerchantItemTokenComponent? title) && !title.Verified) && _internal.Add(item)).ToArray();
        try { return action(); }
        finally { foreach (var item in added) _internal.Remove(item); }
    }
    internal bool IsReservationPlacement(EntityUid item) => _internal.Contains(item);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kQuestReservationComponent, PickupAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, GettingPickedUpAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, DropAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, ThrowAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, UseAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, GettingUsedAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, AttackAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, InteractionAttemptEvent>(CancelInteraction);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, GettingInteractedWithAttemptEvent>(CancelTargetInteraction);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, BeingEquippedAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, BeingUnequippedAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, ContainerGettingInsertedAttemptEvent>(Cancel);
        SubscribeLocalEvent<Wh40kQuestReservationComponent, ContainerGettingRemovedAttemptEvent>(Cancel);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttached);
    }

    private void Cancel(EntityUid uid, Wh40kQuestReservationComponent component, CancellableEntityEventArgs ev)
    { if (!_internal.Contains(uid)) ev.Cancel(); }
    private void CancelInteraction(EntityUid uid, Wh40kQuestReservationComponent component, InteractionAttemptEvent ev)
    { if (!_internal.Contains(uid)) ev.Cancelled = true; }
    private void CancelTargetInteraction(Entity<Wh40kQuestReservationComponent> ent, ref GettingInteractedWithAttemptEvent ev)
    { if (!_internal.Contains(ent.Owner)) ev.Cancelled = true; }

    public Task<bool> SubmitAsync(NetUserId user, Guid quest, string objective, Guid submission, int amount)
    {
        if (submission == Guid.Empty || amount is <= 0 or > 10000 || !_sessions.IsReady(user) ||
            EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>().IsSelling(user) || !_busy.Add(user)) return Task.FromResult(false);
        return _sessions.TrackWork(SubmitCoreAsync(user, quest, objective, submission, amount));
    }

    private async Task<bool> SubmitCoreAsync(NetUserId user, Guid quest, string objectiveId, Guid submissionId, int amount)
    {
        var owner = await _db.ResolveWh40kQuestOwnerAsync(user.UserId, quest);
        var reserved = new List<Reservation>();
        EntityUid body = default;
        Wh40kQuestSubmission? row = null;
        var committed = false;
        var generation = _sessions.Generation;
        try
        {
            var prior = await _db.GetWh40kQuestSubmissionAsync(owner, submissionId);
            if (prior != null) return prior.QuestId == quest && prior.ObjectiveId == objectiveId && prior.Amount == amount && prior.State == 2;
            await _sessions.OnSimulationThread(() =>
            {
                if (generation != _sessions.Generation || !_sessions.IsReady(user) || !_players.TryGetSessionById(user, out var session) ||
                    session.AttachedEntity is not {} actor || !_sessions.TryGetStatus(user, out var states)) return;
                var status = states.SingleOrDefault(x => x.Contract.Id == quest);
                var objective = status?.Snapshot?.Objectives.SingleOrDefault(x => x.Id == objectiveId);
                if (objective == null || !objective.Kind.IsSubmission() || status!.Binding == Wh40kQuestBindingState.WaitingForContent ||
                    status.Snapshot!.Issuer == null || !_anchors.TryGetNpc(status.Snapshot.Issuer, out var issuer) ||
                    !_anchors.IsInRange(actor, issuer) ||
                    amount > objective.RequiredCount - status.Objectives.Single(x => x.ObjectiveId == objectiveId).Count) return;
                body = actor;
                var remaining = amount;
                foreach (var item in _progress.Possessions(body).Where(item => _progress.Matches(item, objective.Target.SubmissionTarget(), quest, objectiveId) &&
                             (objective.Kind != Wh40kQuestObjectiveKind.CollectEvidence ||
                              TryComp(item, out Wh40kQuestItemComponent? evidence) && evidence.QuestId == quest && evidence.ObjectiveId == objectiveId)))
                {
                    if (!_containers.TryGetContainingContainer(item, out var container) || container.Owner != body) continue;
                    if (_hands.IsHolding(body, item, out var hand))
                    { if (!_hands.CanDropHeld(body, hand, checkActionBlocker: false)) continue; }
                    else if (!_inventory.CanUnequip(body, container.ID, out _)) continue;
                    var original = TryComp(item, out StackComponent? stack) ? stack.Count : 1;
                    var units = Math.Min(remaining, original);
                    reserved.Add(new(item, units, original, container)); remaining -= units;
                    if (remaining == 0) break;
                }
                if (remaining != 0) { reserved.Clear(); return; }
                foreach (var reservation in reserved)
                {
                    var marker = EnsureComp<Wh40kQuestReservationComponent>(reservation.Item);
                    marker.Submission = submissionId; marker.UserId = user.UserId; marker.Units = reservation.Units;
                    marker.OriginalCount = reservation.OriginalCount;
                }
                var capture = _serializer.CaptureRoots(reserved.Select((r, i) => new PersistentInventorySnapshotSerializer.CaptureRoot(
                    PersistentInventoryRootKind.Hand, "quest-" + i, r.Item)).ToArray(), "Wh40kDefault", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new());
                if (!capture.IsSuccess) return;
                row = new() { Id = submissionId, QuestId = quest, ObjectiveId = objectiveId, Amount = amount,
                    PayloadJson = JsonSerializer.Serialize(capture.Payload) };
            });
            if (row == null) return false;
            PhaseChanged?.Invoke(Wh40kQuestSubmissionPhase.Locked);
            if (!await _db.SaveWh40kQuestSubmissionAsync(owner, row, 0)) return false;
            PhaseChanged?.Invoke(Wh40kQuestSubmissionPhase.Reserved);
            var moved = false;
            await _sessions.OnSimulationThread(() =>
            {
                if (generation != _sessions.Generation || !_players.TryGetSessionById(user, out var session) || session.AttachedEntity != body) return;
                foreach (var reservation in reserved)
                {
                    if (!Exists(reservation.Item)) return;
                    _internal.Add(reservation.Item);
                    try
                    {
                        if (!_containers.Remove(reservation.Item, reservation.Container, reparent: false, force: true)) return;
                        EntityManager.System<SharedTransformSystem>().DetachEntity(reservation.Item);
                    }
                    finally { _internal.Remove(reservation.Item); }
                }
                moved = true;
            });
            if (!moved) return false;
            PhaseChanged?.Invoke(Wh40kQuestSubmissionPhase.Escrowed);
            row.State = 1;
            if (!await _db.SaveWh40kQuestSubmissionAsync(owner, row, 1)) return false;
            PhaseChanged?.Invoke(Wh40kQuestSubmissionPhase.Captured);
            committed = await _db.CommitWh40kQuestSubmissionAsync(owner, submissionId, DateTime.UtcNow);
            if (committed) PhaseChanged?.Invoke(Wh40kQuestSubmissionPhase.Committed);
            return committed;
        }
        catch (Exception error) { Log.Error($"Quest submission {submissionId} remains recoverable: {error}"); return false; }
        finally
        {
            // Query the durable outcome after an uncertain commit before changing any escrow units.
            if (row != null)
            {
                try
                {
                    var stored = await _db.GetWh40kQuestSubmissionAsync(owner, submissionId);
                    if (stored != null)
                    {
                        committed = stored.State == 2;
                        if (!committed && stored.State < 2)
                        {
                            stored.State = 3;
                            if (!await _db.SaveWh40kQuestSubmissionAsync(owner, stored, stored.Revision))
                                throw new InvalidOperationException("Submission restoration lost its revision fence.");
                        }
                    }
                    await _sessions.OnSimulationThread(() => Release(reserved, body, committed));
                }
                catch (Exception error) { Log.Error($"Quest escrow retained pending recovery: {error}"); }
            }
            else await _sessions.OnSimulationThread(() => Release(reserved, body, false));
            await _sessions.OnSimulationThread(() =>
            {
                _busy.Remove(user);
                if (_players.TryGetSessionById(user, out _) && generation == _sessions.Generation) _sessions.TrackWork(_sessions.LoadAccountAsync(user));
            });
        }
    }

    private void Release(IEnumerable<Reservation> reserved, EntityUid body, bool consume)
    {
        foreach (var reservation in reserved)
        {
            if (!Exists(reservation.Item)) continue;
            if (!Exists(body)) continue;
            if (consume)
            {
                if (!TryComp(reservation.Item, out StackComponent? stack) || reservation.OriginalCount <= reservation.Units)
                { QueueDel(reservation.Item); continue; }
                // Assign the frozen remainder; retrying recovery cannot subtract the units twice.
                _stacks.SetCount(reservation.Item, reservation.OriginalCount - reservation.Units, stack);
                PhaseChanged?.Invoke(Wh40kQuestSubmissionPhase.Consumed);
            }
            RemComp<Wh40kQuestReservationComponent>(reservation.Item);
            if (_containers.TryGetContainingContainer(reservation.Item, out var existing) && existing.Owner == body) continue;
            EntityManager.System<SharedTransformSystem>().SetCoordinates(reservation.Item, Transform(body).Coordinates);
            _hands.TryPickupAnyHand(body, reservation.Item);
        }
    }

    private void OnAttached(PlayerAttachedEvent ev)
    {
        if (ev.Player.Channel.AuthType.HasStaticUserId()) _sessions.TrackWork(ReconcileAsync(ev.Player.UserId));
    }

    public async Task ReconcileAsync(NetUserId user)
    {
        var available = false;
        await _sessions.OnSimulationThread(() => available = _players.TryGetSessionById(user, out var session) &&
            session.AttachedEntity is {} body && Exists(body) && _busy.Add(user));
        if (!available) return;
        try
        {
            var owner = Wh40kQuestOwnerKey.Account(user);
            var ids = new HashSet<Guid>();
            await _sessions.OnSimulationThread(() =>
            {
                var query = EntityQueryEnumerator<Wh40kQuestReservationComponent>();
                while (query.MoveNext(out _, out var marker))
                    if (!marker.Merchant && marker.UserId == user.UserId) ids.Add(marker.Submission);
            });
            foreach (var pending in await _db.GetWh40kQuestSubmissionsAsync(owner)) ids.Add(pending.Id);
            foreach (var id in ids)
            {
                var row = await _db.GetWh40kQuestSubmissionAsync(owner, id);
                if (row == null) continue;
                var found = false;
                await _sessions.OnSimulationThread(() =>
                {
                    if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not {} body) return;
                    var query = EntityQueryEnumerator<Wh40kQuestReservationComponent>();
                    var reservations = new List<Reservation>();
                    while (query.MoveNext(out var uid, out var marker))
                    {
                        if (marker.Merchant || marker.Submission != row.Id || marker.UserId != user.UserId) continue;
                        found = true;
                        _containers.TryGetContainingContainer(uid, out var container);
                        reservations.Add(new(uid, marker.Units, marker.OriginalCount, container!));
                    }
                    if (found) Release(reservations, body, row.State == 2);
                });
                if (row.State < 2)
                {
                    row.State = found ? 3 : 4;
                    await _db.SaveWh40kQuestSubmissionAsync(owner, row, row.Revision);
                }
            }
        }
        finally { await _sessions.OnSimulationThread(() => _busy.Remove(user)); }
    }
}
