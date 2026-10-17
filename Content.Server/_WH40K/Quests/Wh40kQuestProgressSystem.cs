using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Dialogue;
using Content.Server._WH40K.Npcs;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Database;
using Content.Shared._WH40K.Quests;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

[RegisterComponent]
public sealed partial class Wh40kQuestVictimTokenComponent : Component
{
    public Guid Token = Guid.NewGuid();
    public bool Credited;
}

[RegisterComponent]
public sealed partial class Wh40kQuestActorComponent : Component
{
    public NetUserId UserId;
}

/// <summary>Indexed account objectives driven by authoritative server events.</summary>
public sealed partial class Wh40kQuestProgressSystem : EntitySystem
{
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;
    [Dependency] private Wh40kNpcAnchorSystem _anchors = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    private readonly Dictionary<NetUserId, List<Subscription>> _accounts = new();
    private readonly Dictionary<string, HashSet<EntityUid>> _locations = new();
    private readonly HashSet<NetUserId> _dirty = new();
    private readonly Dictionary<Guid, Pending> _pending = new();
    private Task _flush = Task.CompletedTask;
    private float _retry;
    private sealed record Subscription(Guid Quest, Wh40kQuestContractSnapshot Snapshot, Wh40kQuestObjectiveSnapshot Objective, int Count);
    private sealed record Pending(Wh40kQuestOwnerKey Owner, Guid Quest, Wh40kQuestFact Fact);

    public override void Initialize()
    {
        base.Initialize();
        _sessions.AccountLoaded += Reload;
        _players.PlayerStatusChanged += OnStatus;
        SubscribeLocalEvent<Wh40kQuestLocationComponent, ComponentStartup>(OnLocationStartup);
        SubscribeLocalEvent<Wh40kQuestLocationComponent, ComponentShutdown>(OnLocationShutdown);
        SubscribeLocalEvent<MobStateChangedEvent>(OnDeath);
        SubscribeLocalEvent<Content.Server._WH40K.Dialogue.Components.DialogueInteractableComponent, DialogueCompletedEvent>(OnTalk);
        SubscribeLocalEvent<Wh40kQuestInteractionTargetComponent, InteractHandEvent>(OnInteract, before: [typeof(DialogueSystem)]);
        SubscribeLocalEvent<Wh40kQuestActorComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<StackComponent, StackCountChangedEvent>(OnStack);
        SubscribeLocalEvent<Wh40kQuestItemComponent, StackSplitEvent>(OnQuestItemSplit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _accounts.Clear(); _dirty.Clear(); _subjects.Clear(); _eligibleTime.Clear(); _miners.Clear(); _treatedSubjects.Clear(); });
        InitializeSpecialists();
    }

    public override void Shutdown()
    {
        _sessions.AccountLoaded -= Reload;
        _sessions.AccountLoaded -= ApplyAccountAccess;
        _sessions.AccountLoaded -= BindSubjects;
        _players.PlayerStatusChanged -= OnStatus;
        base.Shutdown();
    }

    private void OnStatus(object? sender, SessionStatusEventArgs ev)
    {
        if (ev.NewStatus is SessionStatus.Disconnected or SessionStatus.Zombie)
        { _accounts.Remove(ev.Session.UserId); _dirty.Remove(ev.Session.UserId); }
    }
    private void OnLocationStartup(EntityUid uid, Wh40kQuestLocationComponent component, ComponentStartup ev)
    {
        if (!_locations.TryGetValue(component.Key, out var markers)) _locations[component.Key] = markers = new();
        markers.Add(uid);
    }
    private void OnLocationShutdown(EntityUid uid, Wh40kQuestLocationComponent component, ComponentShutdown ev)
    { if (_locations.TryGetValue(component.Key, out var markers)) markers.Remove(uid); }

    private void Reload(NetUserId user)
    {
        var subscriptions = new List<Subscription>();
        if (_sessions.TryGetStatus(user, out var statuses))
            foreach (var status in statuses.Where(x => x.Binding != Wh40kQuestBindingState.WaitingForContent && x.Snapshot != null))
                foreach (var objective in status.Snapshot!.Objectives)
                {
                    if (!status.Snapshot.Recipients.Contains(user.UserId)) continue;
                    subscriptions.Add(new(status.Contract.Id, status.Snapshot, objective,
                        status.Objectives.Single(row => row.ObjectiveId == objective.Id).Count));
                }
        _accounts[user] = subscriptions;
        RetireSpecialistRuntime(_sessions.History(user).Where(x => x.State > 1).Select(x => x.Id).ToHashSet());
        if (_players.TryGetSessionById(user, out var session) && session.AttachedEntity is {} body)
            EnsureComp<Wh40kQuestActorComponent>(body).UserId = user;
        _dirty.Add(user);
    }

    private bool TryOwner(EntityUid body, out NetUserId user)
    {
        user = default;
        if (_sessions.IsStopping || !_players.TryGetSessionByEntity(body, out var session) || session.Status != SessionStatus.InGame ||
            !session.Channel.AuthType.HasStaticUserId() || !_accounts.ContainsKey(session.UserId)) return false;
        user = session.UserId; return true;
    }

    public bool Matches(EntityUid entity, Wh40kQuestTargetSnapshot target, Guid quest, string objective)
    {
        if (TerminatingOrDeleted(entity) || TryComp(entity, out Wh40kQuestReservationComponent? _) ||
            HasComp<Content.Server._WH40K.SectorMap.Components.KoronusStationPropertyOriginComponent>(entity) ||
            TryComp(entity, out Content.Shared._WH40K.Merchant.MerchantItemTokenComponent? title) && !title.Verified) return false;
        if (TryComp(entity, out Wh40kQuestItemComponent? marker) && (marker.QuestId != quest || marker.ObjectiveId != objective)) return false;
        var id = MetaData(entity).EntityPrototype?.ID;
        return target.Kind switch
        {
            Wh40kQuestTargetKind.Entity => id == target.Entity,
            Wh40kQuestTargetKind.Tag => id != null && target.ApprovedPrototypes.Contains(id) &&
                target.Tag != null && _tags.HasTag(entity, new ProtoId<TagPrototype>(target.Tag)),
            _ => false,
        };
    }

    public IEnumerable<EntityUid> Possessions(EntityUid body)
    {
        var found = new HashSet<EntityUid>(_hands.EnumerateHeld(body));
        var slots = _inventory.GetSlotEnumerator(body);
        while (slots.MoveNext(out var slot)) if (slot.ContainedEntity is {} item) found.Add(item);
        return found;
    }

    private bool IsCurrentStep(NetUserId user, Subscription subscription)
        => subscription.Snapshot.Order != Wh40kQuestOrder.Sequential || subscription.Snapshot.Objectives
            .TakeWhile(objective => objective.Id != subscription.Objective.Id)
            .All(objective => _accounts[user].Any(previous => previous.Quest == subscription.Quest &&
                previous.Objective.Id == objective.Id && previous.Count >= objective.RequiredCount));

    private void OnDeath(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead || ev.OldMobState == MobState.Dead || ev.Origin is not {} origin ||
            !TryOwner(origin, out var user) || _players.TryGetSessionByEntity(ev.Target, out _) ||
            TryComp(ev.Target, out MindContainerComponent? mind) && mind.Mind != null ||
            !TryComp(ev.Target, out NpcFactionMemberComponent? faction) ||
            !faction.Factions.Any(f => _factions.IsFactionHostile(f.Id, origin)) || _factions.IsEntityFriendly(origin, ev.Target)) return;
        var token = EnsureComp<Wh40kQuestVictimTokenComponent>(ev.Target);
        if (token.Credited) return;
        token.Credited = true;
        foreach (var subscription in _accounts[user].Where(x => x.Objective.Kind == Wh40kQuestObjectiveKind.KillEntity && x.Count < x.Objective.RequiredCount &&
                     IsCurrentStep(user, x) &&
                     Matches(ev.Target, x.Objective.Target, x.Quest, x.Objective.Id)))
            Credit(user, subscription, 1, false, $"{_sessions.Generation:N}:{token.Token:N}");
    }

    private void OnTalk(EntityUid uid, Content.Server._WH40K.Dialogue.Components.DialogueInteractableComponent component, DialogueCompletedEvent ev)
    {
        if (!TryOwner(ev.Initiator, out var user) || user != ev.UserId || !_anchors.TryGetIdentity(uid, out var anchor)) return;
        foreach (var subscription in _accounts[user].Where(x => x.Objective.Kind is Wh40kQuestObjectiveKind.TalkToNpc or Wh40kQuestObjectiveKind.FactionDecision && x.Count < x.Objective.RequiredCount &&
                     IsCurrentStep(user, x) &&
                     x.Objective.Target.Anchors.Contains(anchor.Id) && (x.Objective.Target.Dialogue == null || x.Objective.Target.Dialogue == ev.DialogueId)))
            Credit(user, subscription, 1, false, anchor.Id);
    }

    private void OnInteract(EntityUid uid, Wh40kQuestInteractionTargetComponent component, InteractHandEvent ev)
    {
        if (ev.Handled || !TryOwner(ev.User, out var user) || !_interaction.InRangeUnobstructed(ev.User, uid)) return;
        foreach (var subscription in _accounts[user].Where(x => x.Objective.Kind == Wh40kQuestObjectiveKind.InteractWithEntity && x.Count < x.Objective.RequiredCount &&
                     IsCurrentStep(user, x) &&
                     Matches(uid, x.Objective.Target, x.Quest, x.Objective.Id)))
            Credit(user, subscription, 1, false, $"{_sessions.Generation:N}:{EnsureComp<Wh40kQuestVictimTokenComponent>(uid).Token:N}");
        ev.Handled = true;
    }

    private void OnMove(EntityUid uid, Wh40kQuestActorComponent component, ref MoveEvent ev) => _dirty.Add(component.UserId);
    private void OnInserted(EntInsertedIntoContainerMessage ev) { if (TryOwner(ev.Container.Owner, out var user)) _dirty.Add(user); }
    private void OnRemoved(EntRemovedFromContainerMessage ev) { if (TryOwner(ev.Container.Owner, out var user)) _dirty.Add(user); }
    private void OnStack(EntityUid uid, StackComponent component, StackCountChangedEvent ev)
    {
        if (TryComp(uid, out TransformComponent? transform) && TryOwner(transform.ParentUid, out var user)) _dirty.Add(user);
    }

    private void OnQuestItemSplit(EntityUid uid, Wh40kQuestItemComponent component, ref StackSplitEvent ev)
    {
        var marker = EnsureComp<Wh40kQuestItemComponent>(ev.NewId);
        marker.QuestId = component.QuestId;
        marker.ObjectiveId = component.ObjectiveId;
    }

    public bool Reached(EntityUid body, Wh40kQuestTargetSnapshot target)
    {
        var transform = Transform(body);
        if (target.LocationKind == Wh40kQuestLocationKind.Sector)
            return target.Sector != null && _sector.IsMapInSector(target.Sector, transform.MapID);
        if (target.System == null || !_sector.TryGetSystemMap(target.System, out var map) || map != transform.MapID) return false;
        if (target.LocationKind == Wh40kQuestLocationKind.System) return true;
        if (target.LocationKind == Wh40kQuestLocationKind.Coordinates)
            return target.Position is {} position && System.Numerics.Vector2.Distance(EntityManager.System<SharedTransformSystem>().GetWorldPosition(transform), position) <= target.Radius;
        if (!TryLocation(target, out var locationUid)) return false;
        var location = Transform(locationUid);
        return target.LocationKind == Wh40kQuestLocationKind.Grid ? transform.GridUid != null && transform.GridUid == location.GridUid :
            System.Numerics.Vector2.Distance(EntityManager.System<SharedTransformSystem>().GetWorldPosition(location),
                EntityManager.System<SharedTransformSystem>().GetWorldPosition(transform)) <= target.Radius;
    }

    private bool TryLocation(Wh40kQuestTargetSnapshot target, out EntityUid uid)
    {
        uid = default;
        if (target.LocationKey == null || target.System == null || !_sector.TryGetSystemMap(target.System, out var map) ||
            !_locations.TryGetValue(target.LocationKey, out var markers)) return false;
        var valid = markers.Where(marker => Exists(marker) && Transform(marker).MapID == map &&
            Comp<Wh40kQuestLocationComponent>(marker).Kind == target.LocationKind).Take(2).ToArray();
        if (valid.Length != 1) return false;
        uid = valid[0]; return true;
    }

    public bool CanBind(Wh40kQuestObjectiveSnapshot objective)
    {
        if (objective.Kind == Wh40kQuestObjectiveKind.TalkToNpc)
            return objective.Target.Anchors.All(id => _anchors.TryGetNpc(id, out _));
        if (objective.Target.Kind is Wh40kQuestTargetKind.Entity or Wh40kQuestTargetKind.Tag) return true;
        if (objective.Kind is Wh40kQuestObjectiveKind.FactionDecision or Wh40kQuestObjectiveKind.PayDebtOrFee)
            return objective.Target.Anchors.All(id => _anchors.TryGetNpc(id, out _));
        if (objective.Target.Kind == Wh40kQuestTargetKind.Activity) return true;
        if (objective.Kind is not (Wh40kQuestObjectiveKind.ReachLocation or Wh40kQuestObjectiveKind.DiscoverLocation or Wh40kQuestObjectiveKind.InvestigateLocation)) return false;
        if (objective.Target.LocationKind == Wh40kQuestLocationKind.Sector)
            return objective.Target.Sector != null && _sector.HasSector(objective.Target.Sector);
        if (objective.Target.LocationKind is Wh40kQuestLocationKind.Grid or Wh40kQuestLocationKind.PointOfInterest)
            return TryLocation(objective.Target, out _);
        if (objective.Target.System != null) return _sector.TryGetSystemMap(objective.Target.System, out _);
        return false;
    }

    public bool HasRequiredPossessions(NetUserId user, Guid quest)
    {
        if (!_accounts.TryGetValue(user, out var subscriptions) || !_players.TryGetSessionById(user, out var session) ||
            session.AttachedEntity is not {} body) return false;
        return subscriptions.Where(x => x.Quest == quest && x.Objective.Kind is Wh40kQuestObjectiveKind.ProtectTarget or Wh40kQuestObjectiveKind.EscortEntity).All(x =>
                   _subjects.TryGetValue((quest, x.Objective.Target.Entity!), out var target) && SubjectSafe(target, x)) &&
               subscriptions.Where(x => x.Quest == quest && x.Objective.Kind == Wh40kQuestObjectiveKind.CollectItem).All(x =>
            Possessions(body).Where(item => Matches(item, x.Objective.Target, quest, x.Objective.Id))
                .Sum(item => TryComp(item, out StackComponent? stack) ? stack.Count : 1) >= x.Objective.RequiredCount);
    }

    private void Credit(NetUserId user, Subscription subscription, int amount, bool set, string? token = null)
    {
        if (_pending.Count >= 2048) { Log.Error("Quest fact buffer is full; database ingestion must recover."); return; }
        var fact = new Wh40kQuestFact(Guid.NewGuid(), subscription.Objective.Id, amount, set, DateTime.UtcNow, token, Actor: user.UserId);
        _pending.Add(fact.EventId, new(subscription.Snapshot.Owner, subscription.Quest, fact));
    }

    public override void Update(float frameTime)
    {
        if (_sessions.IsStopping) return;
        UpdateSpecialists(frameTime);
        foreach (var user in _dirty.Take(32).ToArray())
        {
            _dirty.Remove(user);
            if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not {} body || !TryOwner(body, out _) ||
                !_accounts.TryGetValue(user, out var subscriptions)) continue;
            foreach (var subscription in subscriptions)
            {
                if (!IsCurrentStep(user, subscription)) continue;
                var objective = subscription.Objective;
                if (objective.Kind == Wh40kQuestObjectiveKind.CollectItem)
                {
                    var count = Math.Min(objective.RequiredCount, Possessions(body).Where(item => Matches(item, objective.Target, subscription.Quest, objective.Id))
                        .Sum(item => TryComp(item, out StackComponent? stack) ? stack.Count : 1));
                    if (count != subscription.Count) Credit(user, subscription, count, true);
                }
                else if (objective.Kind is Wh40kQuestObjectiveKind.ReachLocation or Wh40kQuestObjectiveKind.DiscoverLocation or Wh40kQuestObjectiveKind.InvestigateLocation &&
                    subscription.Count < objective.RequiredCount && Reached(body, objective.Target))
                    Credit(user, subscription, 1, false, objective.Target.LocationKey ?? objective.Target.System ?? objective.Target.Sector);
            }
        }
        _retry += frameTime;
        if (_pending.Count > 0 && _flush.IsCompleted && _retry >= 1f)
        { _retry = 0; _flush = _sessions.TrackWork(FlushAsync(_pending.Values.Take(64).ToArray())); }
    }

    private async Task FlushAsync(Pending[] batch)
    {
        foreach (var entry in batch)
        {
            try
            {
                if (await _db.EnqueueWh40kQuestFactAsync(entry.Owner, entry.Quest, entry.Fact))
                    await _sessions.OnSimulationThread(() => _pending.Remove(entry.Fact.EventId));
                else if (entry.Owner.Scope == Wh40kQuestOwnerScope.Party && entry.Fact.Actor is {} actor &&
                         await _db.ResolveWh40kQuestOwnerAsync(actor, entry.Quest) != entry.Owner)
                    await _sessions.OnSimulationThread(() => _pending.Remove(entry.Fact.EventId));
            }
            catch (Exception error) { Log.Error($"Quest fact remains buffered for retry: {error}"); return; }
        }
    }

    internal async Task DrainFactsAsync()
    {
        await _flush;
        while (_pending.Count > 0)
        {
            var before = _pending.Count;
            await FlushAsync(_pending.Values.Take(64).ToArray());
            if (_pending.Count >= before)
            {
                // Free durable inbox capacity before retrying buffered events during shutdown.
                async Task<bool> ContentAvailable(Wh40kQuestContractSnapshot snapshot)
                {
                    var valid = false;
                    await _sessions.OnSimulationThread(() => valid = EntityManager.System<Wh40kQuestContentSystem>().IsCompatibleContract(snapshot));
                    return valid;
                }
                if (await _db.ProcessWh40kQuestInboxAsync(DateTime.UtcNow, contentAvailable: ContentAvailable) == 0)
                    throw new InvalidOperationException("Quest inbox ingestion failed during drain; world cleanup must wait.");
            }
        }
    }
}
