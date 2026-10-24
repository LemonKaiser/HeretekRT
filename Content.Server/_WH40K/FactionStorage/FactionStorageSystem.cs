using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.PersistentInventory;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Server._WH40K.Quests;
using Content.Server.Database;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.FactionStorage;
using Content.Shared.ActionBlocker;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.FactionStorage;

public sealed partial class FactionStorageSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private PersistentInventorySnapshotSerializer _serializer = default!;
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WH40KPlayerCultureManager _culture = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    private const string FeedbackDelayId = "wh40k-faction-storage-feedback";

    private readonly Dictionary<string, Depot> _depots = new();
    private readonly HashSet<NetUserId> _busyUsers = new();
    private TimeSpan _nextAccessCheck;

    // A faction has one writer. Other terminals share this cache, never physical containers.
    private sealed class Depot
    {
        public readonly Wh40kFactionStorageSlot?[] Slots = new Wh40kFactionStorageSlot?[FactionStorageComponent.Capacity];
        public FactionDepotStatus Status = FactionDepotStatus.Unavailable;
        public TimeSpan RefreshAt;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FactionStorageComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<FactionStorageComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<FactionStorageComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<FactionStorageComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<FactionStorageComponent, FactionDepotRefresh>(OnRefresh);
        SubscribeLocalEvent<FactionStorageComponent, FactionDepotTransfer>(OnTransfer);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _depots.Clear());
    }

    public bool IsBusy(NetUserId user) => _busyUsers.Contains(user);

    private bool CanAccess(EntityUid terminal, FactionStorageComponent storage, EntityUid user)
        => !TerminatingOrDeleted(terminal) && !TerminatingOrDeleted(user) && !_sessions.IsStopping &&
           TryComp(user, out CompanyComponent? company) && company.CompanyName == storage.Faction &&
           TryComp(user, out ActorComponent? actor) && actor.PlayerSession.AttachedEntity == user &&
           _blocker.CanInteract(user, terminal) && _interaction.InRangeUnobstructed(user, terminal, popup: false);

    private bool CanMove(EntityUid terminal, FactionStorageComponent storage, EntityUid user, bool completing = false)
    {
        if (!CanAccess(terminal, storage, user) || HasComp<PersistentInventoryOperationComponent>(user))
            return false;
        var account = Comp<ActorComponent>(user).PlayerSession.UserId;
        return (completing || !IsBusy(account)) &&
               !EntityManager.System<Wh40kQuestInteractionSystem>().IsBusy(account) &&
               !EntityManager.System<Wh40kQuestSubmissionSystem>().IsBusy(account) &&
               !EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>().IsSelling(account);
    }

    private void OnOpenAttempt(Entity<FactionStorageComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (CanAccess(ent, ent.Comp, args.User))
            return;
        args.Cancel();
        Notify(ent, args.User, "wh40k-faction-storage-denied");
    }

    private void OnInteractHand(EntityUid uid, FactionStorageComponent comp, InteractHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (!TryStartRequest(args.User))
            return;
        if (CanAccess(uid, comp, args.User))
            _ui.TryOpenUi(uid, FactionStorageUiKey.Key, args.User);
        else
            Notify(uid, args.User, "wh40k-faction-storage-denied");
    }

    private void OnOpened(Entity<FactionStorageComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(FactionStorageUiKey.Key))
            return;
        if (!CanAccess(ent, ent.Comp, args.Actor))
        {
            _ui.CloseUi(ent.Owner, FactionStorageUiKey.Key, args.Actor);
            Notify(ent, args.Actor, "wh40k-faction-storage-denied");
            return;
        }
        Refresh(ent.Comp.Faction.Id);
        Publish(ent, ent.Comp);
    }

    private bool IsViewer(EntityUid uid, FactionStorageComponent comp, EntityUid user)
    {
        if (!_ui.GetActors(uid, FactionStorageUiKey.Key).Contains(user))
            return false;
        if (CanAccess(uid, comp, user))
            return true;
        _ui.CloseUi(uid, FactionStorageUiKey.Key, user);
        return false;
    }

    private void OnRefresh(EntityUid uid, FactionStorageComponent comp, FactionDepotRefresh args)
    {
        if (!TryStartRequest(args.Actor, refresh: true) || !IsViewer(uid, comp, args.Actor))
            return;
        Refresh(comp.Faction.Id);
    }

    private void OnInteractUsing(EntityUid uid, FactionStorageComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (!TryStartRequest(args.User) || !CanMove(uid, comp, args.User) || !TryReady(uid, comp, args.User, out var depot))
            return;
        for (var slot = 0; slot < depot.Slots.Length; slot++)
        {
            if (depot.Slots[slot]?.Payload != null)
                continue;
            StartDeposit(uid, comp, args.User, args.Used, slot, depot);
            return;
        }
        Notify(uid, args.User, "wh40k-faction-storage-full");
    }

    private void OnTransfer(EntityUid uid, FactionStorageComponent comp, FactionDepotTransfer args)
    {
        if (!TryStartRequest(args.Actor) || !IsViewer(uid, comp, args.Actor) || !CanMove(uid, comp, args.Actor) ||
            args.Slot is < 0 or >= FactionStorageComponent.Capacity || !TryReady(uid, comp, args.Actor, out var depot))
            return;
        var row = depot.Slots[args.Slot];
        if ((row?.Revision ?? Guid.Empty) != args.Revision)
        {
            Notify(uid, args.Actor, "wh40k-faction-storage-stale");
            Publish(uid, comp);
            return;
        }
        if (args.Deposit && row?.Payload == null)
        {
            if (_hands.TryGetActiveItem(args.Actor, out var item))
                StartDeposit(uid, comp, args.Actor, item.Value, args.Slot, depot);
            else
                Notify(uid, args.Actor, "wh40k-faction-storage-hold-item");
        }
        else if (!args.Deposit && row?.Payload != null)
            StartWithdrawal(uid, args.Actor, row, depot);
    }

    private Depot GetDepot(string faction)
    {
        if (!_depots.TryGetValue(faction, out var depot))
            _depots.Add(faction, depot = new Depot());
        return depot;
    }

    private bool TryStartRequest(EntityUid user, bool refresh = false)
    {
        if (TerminatingOrDeleted(user) || !TryComp(user, out ActorComponent? actor) || actor.PlayerSession.AttachedEntity != user)
            return false;
        if (_useDelay.IsDelayed(user, FactionStorageComponent.RequestDelayId))
            return false;
        _useDelay.SetLength(user, refresh ? FactionStorageComponent.RefreshCooldown : FactionStorageComponent.RequestCooldown,
            FactionStorageComponent.RequestDelayId);
        return _useDelay.TryResetDelay(user, checkDelayed: true, id: FactionStorageComponent.RequestDelayId);
    }

    private bool TryReady(EntityUid uid, FactionStorageComponent comp, EntityUid user, out Depot depot)
    {
        depot = GetDepot(comp.Faction.Id);
        if (depot.Status == FactionDepotStatus.Ready)
            return true;
        if (depot.Status == FactionDepotStatus.Unavailable)
            Refresh(comp.Faction.Id);
        Notify(uid, user, depot.Status == FactionDepotStatus.Unavailable
            ? "wh40k-faction-storage-unavailable" : "wh40k-faction-storage-busy");
        return false;
    }

    private void Refresh(string faction)
    {
        var depot = GetDepot(faction);
        if (_sessions.IsStopping || depot.Status is FactionDepotStatus.Loading or FactionDepotStatus.Busy ||
            depot.RefreshAt > _timing.CurTime)
            return;
        depot.Status = FactionDepotStatus.Loading;
        depot.RefreshAt = _timing.CurTime + TimeSpan.FromSeconds(1);
        Broadcast(faction);
        _sessions.TrackWork(LoadAsync(faction, depot));
    }

    private async Task LoadAsync(string faction, Depot depot)
    {
        List<Wh40kFactionStorageSlot>? rows = null;
        try { rows = await _db.GetWh40kFactionStorageAsync(faction); }
        catch (Exception error) { Log.Error($"Faction depot {faction} could not be loaded: {error}"); }
        await _sessions.OnSimulationThread(() =>
        {
            Array.Clear(depot.Slots);
            if (rows != null)
                foreach (var row in rows.Where(row => row.Slot is >= 0 and < FactionStorageComponent.Capacity))
                    depot.Slots[row.Slot] = row;
            depot.Status = rows == null ? FactionDepotStatus.Unavailable : FactionDepotStatus.Ready;
            Broadcast(faction);
        });
    }

    private void Publish(EntityUid uid, FactionStorageComponent comp)
    {
        var depot = GetDepot(comp.Faction.Id);
        var slots = new FactionDepotSlot[depot.Slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var row = depot.Slots[i];
            slots[i] = new(i, row?.Revision ?? Guid.Empty, row?.Prototype, row?.Name, row?.Description, row?.Count ?? 0);
        }
        _ui.SetUiState(uid, FactionStorageUiKey.Key, new FactionDepotState(comp.Faction.Id, slots, depot.Status));
    }

    private void Broadcast(string faction)
    {
        var query = EntityQueryEnumerator<FactionStorageComponent>();
        while (query.MoveNext(out var uid, out var comp))
            if (comp.Faction.Id == faction)
                Publish(uid, comp);
    }

    private void Notify(EntityUid terminal, EntityUid user, string message)
    {
        if (!TerminatingOrDeleted(terminal) && !TerminatingOrDeleted(user) && HasComp<ActorComponent>(user))
        {
            _useDelay.SetLength(user, TimeSpan.FromSeconds(1), FeedbackDelayId);
            if (_useDelay.TryResetDelay(user, checkDelayed: true, id: FeedbackDelayId))
                _popup.PopupEntity(_culture.GetPlayerString(user, message), terminal, user);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_nextAccessCheck > _timing.CurTime)
            return;
        _nextAccessCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<FactionStorageComponent>();
        while (query.MoveNext(out var uid, out var comp))
            foreach (var user in _ui.GetActors(uid, FactionStorageUiKey.Key).ToArray())
                IsViewer(uid, comp, user);
    }
}
