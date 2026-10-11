using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Npcs;
using Content.Server.Database;
using Content.Shared._WH40K.Dialogue;
using Content.Shared._WH40K.Quests;
using Content.Shared.Interaction;
using Content.Shared.GameTicking;
using Robust.Shared.Enums;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

/// <summary>One authenticated gateway shared by dialogue and quest BUIs. The client never supplies an owner.</summary>
public sealed partial class Wh40kQuestInteractionSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private Wh40kNpcAnchorSystem _anchors = default!;
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;
    [Dependency] private Wh40kQuestContentSystem _content = default!;
    [Dependency] private Wh40kQuestOfferSystem _offers = default!;
    [Dependency] private Wh40kQuestSubmissionSystem _submission = default!;
    private readonly HashSet<NetUserId> _busy = new();
    private readonly Dictionary<NetUserId, IReadOnlyList<Wh40kQuestOffer>> _offerCache = new();
    private readonly Dictionary<NetUserId, (EntityUid Viewer, ICommonSession Session, EntityUid Body, Guid Generation)> _refresh = new();
    public event Action<Wh40kQuestInteractionPhase>? PhaseChanged;
    public bool IsBusy(NetUserId user) => _busy.Contains(user);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kQuestUiComponent, Wh40kQuestUiRequest>(OnRequest);
        SubscribeLocalEvent<Wh40kQuestUiComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<Wh40kQuestUiComponent, GetVerbsEvent<ActivationVerb>>(OnVerbs);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _offerCache.Clear(); _refresh.Clear(); });
        _players.PlayerStatusChanged += OnStatus;
        _sessions.AccountLoaded += RefreshWhenReady;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnStatus;
        _sessions.AccountLoaded -= RefreshWhenReady;
        base.Shutdown();
    }

    private void OnStatus(object? sender, SessionStatusEventArgs ev)
    {
        if (ev.NewStatus is SessionStatus.Disconnected or SessionStatus.Zombie)
        { _offerCache.Remove(ev.Session.UserId); _refresh.Remove(ev.Session.UserId); }
    }

    private void OnVerbs(EntityUid uid, Wh40kQuestUiComponent comp, GetVerbsEvent<ActivationVerb> ev)
    {
        if (!ev.CanAccess || !ev.CanInteract) return;
        ev.Verbs.Add(new ActivationVerb { Text = Loc.GetString("wh40k-quest-ui-title"),
            Act = () => OpenLog(ev.User, uid) });
    }

    public bool OpenLog(EntityUid body, EntityUid viewer) =>
        TryComp(viewer, out Wh40kQuestUiComponent? _) && _ui.TryOpenUi(viewer, Wh40kQuestUiKey.Key, body);

    private void OnOpened(Entity<Wh40kQuestUiComponent> ent, ref BoundUIOpenedEvent ev)
    {
        if (!ev.UiKey.Equals(Wh40kQuestUiKey.Key) || !TryComp(ev.Actor, out ActorComponent? actor)) return;
        BeginRequest(ent, actor.PlayerSession, new(Wh40kQuestUiAction.Refresh, Guid.NewGuid()));
    }

    private void OnRequest(EntityUid uid, Wh40kQuestUiComponent comp, Wh40kQuestUiRequest ev)
    {
        if (!TryComp(ev.Actor, out ActorComponent? actor)) return;
        BeginRequest(uid, actor.PlayerSession, ev);
    }

    private bool ValidViewer(EntityUid viewer, ICommonSession session, EntityUid body)
    {
        if (session.AttachedEntity != body || TerminatingOrDeleted(viewer) || !_ui.GetActors(viewer, Wh40kQuestUiKey.Key).Contains(body)) return false;
        var range = _ui.GetUiRange(viewer, Wh40kQuestUiKey.Key);
        return _interaction.InRangeUnobstructed(body, viewer, range > 0 ? range : 2f);
    }

    private void RefreshWhenReady(NetUserId user)
    {
        if (!_sessions.IsReady(user) || _busy.Contains(user) || !_refresh.Remove(user, out var pending)) return;
        if (pending.Generation == _sessions.Generation && ValidViewer(pending.Viewer, pending.Session, pending.Body))
            BeginRequest(pending.Viewer, pending.Session, new(Wh40kQuestUiAction.Refresh, Guid.NewGuid()));
    }

    private void BeginRequest(EntityUid viewer, ICommonSession session, Wh40kQuestUiRequest request)
    {
        if (session.AttachedEntity is not {} body || !ValidViewer(viewer, session, body) ||
            request.Operation == Guid.Empty || !Enum.IsDefined(request.Action) || request.Objective == null || request.Objective.Length > 64) return;
        if (!_sessions.IsReady(session.UserId) || _busy.Contains(session.UserId) ||
            EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>().IsSelling(session.UserId))
        {
            if (request.Action == Wh40kQuestUiAction.Refresh)
                _refresh[session.UserId] = (viewer, session, body, _sessions.Generation);
            if (!_busy.Contains(session.UserId))
                _ui.ServerSendUiMessage(viewer, Wh40kQuestUiKey.Key, new Wh40kQuestUiSnapshot([], [], [], Wh40kQuestOperationResult.Pending,
                    request.Action == Wh40kQuestUiAction.Refresh), body);
            return;
        }
        _busy.Add(session.UserId);
        _ui.ServerSendUiMessage(viewer, Wh40kQuestUiKey.Key, new Wh40kQuestUiSnapshot([], [], [], Wh40kQuestOperationResult.Pending, true), body);
        _sessions.TrackWork(HandleAsync(viewer, session, body, request, _sessions.Generation));
    }

    private async Task HandleAsync(EntityUid viewer, ICommonSession session, EntityUid body, Wh40kQuestUiRequest request, Guid generation)
    {
        var result = Wh40kQuestOperationResult.UnavailableTarget;
        try
        {
            Task<Wh40kQuestResult>? mutation = null;
            await _sessions.OnSimulationThread(() =>
            {
                if (generation != _sessions.Generation || !_sessions.IsReady(session.UserId) || !ValidViewer(viewer, session, body)) return;
                if (request.Action == Wh40kQuestUiAction.Refresh) { result = Wh40kQuestOperationResult.Succeeded; return; }
                PhaseChanged?.Invoke(Wh40kQuestInteractionPhase.Validated);
                mutation = ReplayOrMutateAsync(session.UserId, body, viewer, request, generation);
            });
            if (mutation != null)
            {
                result = (await mutation).Result;
                await _sessions.OnSimulationThread(() => PhaseChanged?.Invoke(Wh40kQuestInteractionPhase.Committed));
            }
            await SendSnapshotAsync(viewer, session, body, result, generation);
        }
        catch (Exception error)
        {
            Log.Warning($"Quest UI operation failed; retry retains durable identity: {error.Message}");
            await _sessions.OnSimulationThread(() =>
            {
                if (generation == _sessions.Generation && ValidViewer(viewer, session, body))
                    _ui.ServerSendUiMessage(viewer, Wh40kQuestUiKey.Key,
                        new Wh40kQuestUiSnapshot([], [], [], Wh40kQuestOperationResult.PersistenceUnavailable), body);
            });
        }
        finally { await _sessions.OnSimulationThread(() => { _busy.Remove(session.UserId); RefreshWhenReady(session.UserId); }); }
    }

    private async Task<Wh40kQuestResult> ReplayOrMutateAsync(NetUserId user, EntityUid body, EntityUid viewer, Wh40kQuestUiRequest request, Guid generation)
    {
        if (request.Action == Wh40kQuestUiAction.Submit)
        {
            var submissionOwner = await _db.ResolveWh40kQuestOwnerAsync(user.UserId, request.Target);
            var previous = await _db.GetWh40kQuestSubmissionAsync(submissionOwner, request.Operation);
            if (previous != null)
                return new(previous.QuestId != request.Target || previous.ObjectiveId != request.Objective ? Wh40kQuestOperationResult.InvalidContent :
                    previous.State == (int) Wh40kQuestSubmissionState.Committed ? Wh40kQuestOperationResult.Succeeded :
                    previous.State is 0 or 1 ? Wh40kQuestOperationResult.Pending : Wh40kQuestOperationResult.NotReady, previous.QuestId, previous.Revision);
        }
        else
        {
            var owner = await _db.ResolveWh40kQuestOwnerAsync(user.UserId, request.Target);
            if (await _db.GetWh40kQuestOperationAsync(owner, request.Operation) != null)
                return await _db.MutateWh40kQuestAsync(owner, user.UserId, new(request.Operation,
                    request.Action == Wh40kQuestUiAction.Accept ? Wh40kQuestMutation.Accept : request.Action == Wh40kQuestUiAction.Abandon ?
                    Wh40kQuestMutation.Abandon : Wh40kQuestMutation.Complete, request.Target, request.Revision), DateTime.UtcNow);
        }
        Task<Wh40kQuestResult> work = Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.UnavailableTarget, null, 0));
        await _sessions.OnSimulationThread(() =>
        {
            if (generation == _sessions.Generation && _sessions.IsReady(user) && _players.TryGetSessionById(user, out var session) && ValidViewer(viewer, session, body))
                work = MutateFromViewerAsync(user, body, viewer, request);
        });
        return await work;
    }

    private Task<Wh40kQuestResult> MutateFromViewerAsync(NetUserId user, EntityUid body, EntityUid viewer, Wh40kQuestUiRequest request)
    {
        var issuer = SourceNpc(viewer);
        if (request.Action != Wh40kQuestUiAction.Abandon && (issuer == null || !_anchors.IsInRange(body, issuer.Value)))
            return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.UnavailableTarget, null, 0));
        if (request.Action == Wh40kQuestUiAction.Accept)
        {
            var offer = _offerCache.GetValueOrDefault(user)?.SingleOrDefault(x => x.Id == request.Target);
            if (offer == null || !Wh40kQuestSnapshotCodec.TryDeserialize(offer.TermsJson, out var terms) ||
                !_anchors.TryGetIdentity(issuer!.Value, out var anchor) || terms!.Issuer != anchor.Id)
                return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.NotOwner, null, 0));
            return _sessions.MutateAsync(user, new(request.Operation, Wh40kQuestMutation.Accept, request.Target),
                () => _players.TryGetSessionById(user, out var live) && ValidViewer(viewer, live, body) && SourceNpc(viewer) == issuer &&
                    !TerminatingOrDeleted(issuer!.Value) && _anchors.IsInRange(body, issuer.Value));
        }
        if (!_sessions.TryGetStatus(user, out var rows)) return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.Pending, null, 0));
        var row = rows.SingleOrDefault(x => x.Contract.Id == request.Target);
        if (row == null) return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.NotOwner, null, 0));
        if (row.Contract.Revision != request.Revision) return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.StaleRevision, request.Target, row.Contract.Revision));
        if (request.Action != Wh40kQuestUiAction.Abandon &&
            (!_anchors.TryGetIdentity(issuer!.Value, out var actual) || row.Snapshot?.Issuer != actual.Id))
            return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.UnavailableTarget, null, 0));
        return request.Action == Wh40kQuestUiAction.Submit
            ? SubmitAsync(user, row, request)
            : _sessions.MutateAsync(user, new(request.Operation, request.Action == Wh40kQuestUiAction.Abandon ?
                Wh40kQuestMutation.Abandon : Wh40kQuestMutation.Complete, request.Target, request.Revision),
                () => _players.TryGetSessionById(user, out var live) && ValidViewer(viewer, live, body) &&
                    (request.Action == Wh40kQuestUiAction.Abandon || SourceNpc(viewer) == issuer && !TerminatingOrDeleted(issuer!.Value) &&
                        _anchors.IsInRange(body, issuer.Value)));
    }

    private async Task<Wh40kQuestResult> SubmitAsync(NetUserId user, Wh40kQuestStoredStatus row, Wh40kQuestUiRequest request)
    {
        var objective = row.Snapshot?.Objectives.SingleOrDefault(x => x.Id == request.Objective && x.Kind.IsSubmission());
        if (objective == null) return new(Wh40kQuestOperationResult.InvalidContent, null, 0);
        var remaining = objective.RequiredCount - row.Objectives.Single(x => x.ObjectiveId == objective.Id).Count;
        var success = await _submission.SubmitAsync(user, row.Contract.Id, objective.Id, request.Operation, remaining);
        Task reload = Task.CompletedTask;
        await _sessions.OnSimulationThread(() => reload = _sessions.LoadAccountAsync(user));
        await reload;
        return new(success ? Wh40kQuestOperationResult.Succeeded : Wh40kQuestOperationResult.NotReady, row.Contract.Id, row.Contract.Revision);
    }

    private EntityUid? SourceNpc(EntityUid viewer)
    {
        if (!TryComp(viewer, out Wh40kQuestUiComponent? component) || component.Pool is not {} id ||
            !_content.IsValidPool(id) || !_prototypes.TryIndex(id, out var pool) || !_anchors.TryGetNpc(pool.Source, out var npc)) return null;
        return npc;
    }

    private async Task SendSnapshotAsync(EntityUid viewer, ICommonSession session, EntityUid body, Wh40kQuestOperationResult result, Guid generation)
    {
        Task<IReadOnlyList<Wh40kQuestOffer>> offerTask = Task.FromResult<IReadOnlyList<Wh40kQuestOffer>>([]);
        var historyLimit = 6;
        await _sessions.OnSimulationThread(() =>
        {
            if (generation != _sessions.Generation || !_sessions.IsReady(session.UserId) || !ValidViewer(viewer, session, body)) return;
            if (Comp<Wh40kQuestUiComponent>(viewer).Pool is {} id && _prototypes.TryIndex(id, out var pool))
            { offerTask = _offers.GenerateAsync(session.UserId, pool); historyLimit = pool.HistoryLimit; }
        });
        var offers = await offerTask;
        Guid[] visible = [];
        await _sessions.OnSimulationThread(() =>
        {
            if (generation != _sessions.Generation || !ValidViewer(viewer, session, body) || !_sessions.TryGetStatus(session.UserId, out var rows)) return;
            visible = rows.Select(x => x.Contract.Id).Concat(_sessions.History(session.UserId).Take(historyLimit).Select(x => x.Id)).Distinct().Take(42).ToArray();
        });
        var intents = (await _db.GetWh40kQuestVisibleRewardsAsync(Wh40kQuestOwnerKey.Account(session.UserId), visible)).ToList();
        Wh40kQuestOwnerKey? partyOwner = null;
        await _sessions.OnSimulationThread(() => partyOwner = _sessions.PartyOwner(session.UserId));
        if (partyOwner is {} sharedOwner)
            intents.AddRange(await _db.GetWh40kQuestVisibleRewardsAsync(sharedOwner, visible, recipient: session.UserId.UserId));
        await _sessions.OnSimulationThread(() =>
        {
            if (generation != _sessions.Generation || !ValidViewer(viewer, session, body) || !_sessions.TryGetStatus(session.UserId, out var rows)) return;
            _offerCache[session.UserId] = offers;
            var source = SourceNpc(viewer);
            string? anchor = source is {} npc && _anchors.TryGetIdentity(npc, out var identity) ? identity.Id : null;
            var offerViews = offers.Where(x => Wh40kQuestSnapshotCodec.TryDeserialize(x.TermsJson, out _)).Select(x =>
            {
                Wh40kQuestSnapshotCodec.TryDeserialize(x.TermsJson, out var terms);
                var view = View(x.Id, 0, terms!, Wh40kQuestState.Active, Wh40kQuestBindingState.Bound, [], intents, terms!.Issuer == anchor, session.UserId.UserId);
                view.OfferExpiry = x.ExpiresAt.ToString("u");
                return view;
            }).ToList();
            var active = rows.Where(x => x.Snapshot != null).Select(x => View(x.Contract.Id, x.Contract.Revision, x.Snapshot!,
                (Wh40kQuestState) x.Contract.State, x.Binding, x.Objectives, intents, x.Snapshot!.Issuer == anchor && anchor != null, session.UserId.UserId)).ToList();
            var history = _sessions.History(session.UserId).Take(historyLimit).Where(x => Wh40kQuestSnapshotCodec.TryDeserialize(x.SnapshotJson, out _)).Select(x =>
            {
                Wh40kQuestSnapshotCodec.TryDeserialize(x.SnapshotJson, out var terms);
                return View(x.Id, x.Revision, terms!, (Wh40kQuestState) x.State, Wh40kQuestBindingState.Bound, [], intents, false, session.UserId.UserId);
            }).ToList();
            _ui.ServerSendUiMessage(viewer, Wh40kQuestUiKey.Key, new Wh40kQuestUiSnapshot(offerViews, active, history, result), body);
        });
    }

    private static Wh40kQuestUiRow View(Guid id, long revision, Wh40kQuestContractSnapshot terms, Wh40kQuestState state,
        Wh40kQuestBindingState binding, IReadOnlyList<Wh40kQuestObjective> objectives, IReadOnlyList<Wh40kQuestRewardIntent> intents, bool canAct, Guid recipient) =>
        new(id, revision, terms.Title, terms.Description, terms.Issuer, terms.Deadline?.ToString("u"), terms.Owner.Scope, state, binding,
            terms.Objectives.Select(x => new Wh40kQuestUiObjective(x.Id, x.Text, x.Kind,
                objectives.FirstOrDefault(o => o.ObjectiveId == x.Id)?.Count ?? (state == Wh40kQuestState.Completed ? x.RequiredCount : 0), x.RequiredCount)).ToList(),
            terms.Rewards.Where(x => terms.RewardAmount(x, recipient) > 0).Select(x => new Wh40kQuestUiReward(x.Kind, terms.RewardAmount(x, recipient), x.Item,
                (Wh40kQuestRewardState) (intents.FirstOrDefault(i => i.QuestId == id && i.EntryId == x.EntryId && i.Recipient == recipient)?.State ?? 0))).ToList(), canAct && terms.Recipients.Contains(recipient));

    public bool Evaluate(NetUserId user, EntityUid target, DialogueConditionPrototype condition)
    {
        if (!_sessions.IsReady(user)) return false;
        if (condition.Type >= DialogueConditionType.PersistentReputation)
        {
            var key = condition.Type switch
            {
                DialogueConditionType.PersistentReputation => "rep:" + condition.Faction?.Id,
                DialogueConditionType.PersistentFlag => "flag:" + condition.Flag,
                DialogueConditionType.PersistentAccess => "access:" + condition.Access?.Id,
                _ => "branch:" + condition.Flag,
            };
            var state = _sessions.AccountState(user).SingleOrDefault(x => x.Key == key);
            var persistentValue = condition.Type == DialogueConditionType.PersistentBranch ? state != null && state.Value == condition.BranchChoice :
                (state?.Amount ?? 0) >= (condition.Type == DialogueConditionType.PersistentReputation ? condition.Amount : 1);
            return persistentValue == condition.Value;
        }
        if (!_sessions.IsReady(user) || condition.Quest is not {} quest || !_content.IsValidQuest(quest) || !_prototypes.TryIndex(quest, out var definition) ||
            !_anchors.TryGetIdentity(target, out var anchor) || definition.Issuer != anchor) return false;
        _sessions.TryGetStatus(user, out var rows);
        var row = rows.SingleOrDefault(x => x.Contract.DefinitionId == quest.Id && x.Snapshot?.Issuer == anchor.Id);
        var value = condition.Type switch
        {
            DialogueConditionType.QuestAvailable => _offerCache.GetValueOrDefault(user)?.Any(x => x.DefinitionId == quest.Id && x.ExpiresAt > DateTime.UtcNow) == true,
            DialogueConditionType.QuestActive => row != null,
            DialogueConditionType.QuestReadyToTurnIn => row?.Contract.State == (int) Wh40kQuestState.ReadyToTurnIn,
            DialogueConditionType.QuestObjectiveComplete => row?.Objectives.Any(x => x.ObjectiveId == condition.Objective && x.Count >= x.RequiredCount) == true,
            DialogueConditionType.QuestCompleted => _sessions.HasCompleted(user, quest.Id),
            DialogueConditionType.QuestHistoryAtLeast => _sessions.History(user).Count(x => x.DefinitionId == quest.Id) >= condition.Amount,
            _ => false,
        };
        return value == condition.Value;
    }

    public async Task<Wh40kQuestResult> ExecuteDialogueAsync(NetUserId user, EntityUid body, EntityUid npc, DialogueActionPrototype action, Guid operation, Func<bool>? authorize = null)
    {
        if (!_sessions.IsReady(user) || EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>().IsSelling(user) ||
            !_players.TryGetSessionById(user, out var session) || session.AttachedEntity != body ||
            !_anchors.TryGetIdentity(npc, out var anchor) || !_anchors.IsInRange(body, npc) || !TryComp(npc, out Wh40kQuestUiComponent? ui))
            return new(Wh40kQuestOperationResult.UnavailableTarget, null, 0);
        var poolId = action.QuestPool ?? ui.Pool;
        if (poolId is not {} id || !_prototypes.TryIndex(id, out var pool) || pool.Source != anchor) return new(Wh40kQuestOperationResult.InvalidContent, null, 0);
        if (action.Type == DialogueActionType.OpenQuestLog)
            return new(OpenLog(body, npc) ? Wh40kQuestOperationResult.Succeeded : Wh40kQuestOperationResult.UnavailableTarget, null, 0);
        if (action.Type is DialogueActionType.OfferQuest or DialogueActionType.AcceptQuest)
        {
            var generated = await _offers.GenerateAsync(user, pool);
            await _sessions.OnSimulationThread(() =>
            { if (_sessions.IsReady(user) && session.AttachedEntity == body) _offerCache[user] = generated; });
            if (action.Type == DialogueActionType.OfferQuest) return new(Wh40kQuestOperationResult.Succeeded, null, 0);
        }
        Task<Wh40kQuestResult> mutation = Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.UnavailableTarget, null, 0));
        await _sessions.OnSimulationThread(() =>
        {
            if (!_sessions.IsReady(user) || session.AttachedEntity != body || TerminatingOrDeleted(npc) || !_anchors.IsInRange(body, npc) || action.Quest is not {} definition) return;
            if (action.Type == DialogueActionType.AcceptQuest)
            {
                var offer = _offerCache.GetValueOrDefault(user)?.FirstOrDefault(x => x.DefinitionId == definition.Id);
                if (offer != null) mutation = _sessions.MutateAsync(user, new(operation, Wh40kQuestMutation.Accept, offer.Id),
                    () => session.AttachedEntity == body && !TerminatingOrDeleted(npc) && _anchors.IsInRange(body, npc) && (authorize?.Invoke() ?? true));
            }
            else if (_sessions.TryGetStatus(user, out var rows))
            {
                var row = rows.SingleOrDefault(x => x.Contract.DefinitionId == definition.Id && x.Snapshot?.Issuer == anchor.Id);
                if (row != null) mutation = _sessions.MutateAsync(user, new(operation, action.Type == DialogueActionType.AbandonQuest ?
                    Wh40kQuestMutation.Abandon : Wh40kQuestMutation.Complete, row.Contract.Id, row.Contract.Revision),
                    () => session.AttachedEntity == body && !TerminatingOrDeleted(npc) && _anchors.IsInRange(body, npc) && (authorize?.Invoke() ?? true));
            }
        });
        var result = await mutation;
        await _sessions.OnSimulationThread(() => PhaseChanged?.Invoke(Wh40kQuestInteractionPhase.Committed));
        return result;
    }
}

public enum Wh40kQuestInteractionPhase : byte { Validated, Committed }
