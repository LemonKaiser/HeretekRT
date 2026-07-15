using System.Numerics;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.NPC.Components;
using Content.Server._WH40K.Dialogue.Components;
using Content.Server.Chat.Systems;
using Content.Server.NameIdentifier;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.GameTicking;
using Content.Server.Store.Systems;
using Content.Server._NF.Bank;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Store.Components;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Asynchronous;
using Content.Shared.Verbs;
using Content.Shared._WH40K.Dialogue;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Dialogue;

public sealed class DialogueSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedGodmodeSystem _godmode = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private NPCSteeringSystem _npcSteering = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ITaskManager _taskManager = default!;
    [Dependency] private StoreSystem _store = default!;
    [Dependency] private DialogueItemSystem _items = default!;
    [Dependency] private DialogueAccessSystem _access = default!;
    [Dependency] private DialogueActionRequirementSystem _requirements = default!;
    [Dependency] private BankSystem _bank = default!;

    private readonly Dictionary<ICommonSession, ActiveDialogueSession> _sessions = new();
    private readonly Dictionary<EntityUid, HashSet<ICommonSession>> _sessionsByTarget = new();
    private readonly Dictionary<NetUserId, SuspendedDialogueSession> _suspendedSessionsByUser = new();
    private readonly Dictionary<EntityUid, HashSet<SuspendedDialogueSession>> _suspendedSessionsByTarget = new();
    private readonly Dictionary<EntityUid, DialogueEntityLease> _entityLeases = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _autoTriggerContactsByUser = new();
    private readonly ConcurrentDictionary<PersistentDialogueMemoryKey, DialoguePersistentMemoryData> _persistentMemoryCache = new();
    private readonly ConcurrentDictionary<PersistentDialogueMemoryKey, byte> _persistentMemoryLoads = new();
    private readonly ConcurrentDictionary<PersistentDialogueMemoryKey, Task> _persistentMemoryLoadTasks = new();
    private readonly object _persistentMemoryWriteSync = new();
    private readonly Dictionary<PersistentDialogueMemoryKey, SemaphoreSlim> _persistentMemoryWriteLocks = new();
    private readonly Dictionary<PersistentDialogueMemoryKey, int> _persistentMemoryPendingWrites = new();
    private readonly ConcurrentDictionary<PersistentDialogueMemoryKey, Task> _persistentMemoryWriteTasks = new();
    private readonly ConcurrentDictionary<PersistentDialogueMemoryKey, int> _persistentMemoryGenerations = new();
    private readonly ConcurrentDictionary<PersistentDialogueMemoryKey, byte> _evictedPersistentMemoryKeys = new();
    private readonly ConcurrentDictionary<PersistentDialogueMemoryKey, byte> _resettingPersistentMemoryKeys = new();
    private volatile bool _shuttingDown;
    private float _maxAutoTriggerRange;
    private int _nextSessionId = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<DialogueAdvanceRequestEvent>(OnAdvanceRequest);
        SubscribeNetworkEvent<DialogueChoiceRequestEvent>(OnChoiceRequest);
        SubscribeNetworkEvent<DialogueCancelRequestEvent>(OnCancelRequest);
        SubscribeLocalEvent<DialogueInteractableComponent, InteractHandEvent>(
            OnInteractHand,
            before: [typeof(InteractionPopupSystem)]);
        SubscribeLocalEvent<DialogueInteractableComponent, ActivateInWorldEvent>(
            OnActivate,
            before: [typeof(InteractionPopupSystem)]);
        SubscribeLocalEvent<DialogueInteractableComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
        SubscribeLocalEvent<DialogueInteractableComponent, ComponentStartup>(OnInteractableStartup);
        SubscribeLocalEvent<DialogueInteractableComponent, ComponentShutdown>(OnInteractableShutdown);
        SubscribeLocalEvent<DialogueInteractableComponent, MoveEvent>(OnInteractableMove);
        SubscribeLocalEvent<DialogueDisplayNameComponent, MapInitEvent>(OnDialogueDisplayNameMapInit, after: [typeof(NameIdentifierSystem)]);
        SubscribeLocalEvent<ActorComponent, MoveEvent>(OnActorMove);
        SubscribeLocalEvent<ActorComponent, ComponentStartup>(OnActorStartup);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnAnyPlayerDetached);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

        ValidateLoadedDialoguePrototypes();
    }

    public override void Shutdown()
    {
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _shuttingDown = true;
        FlushPersistentMemoryOperations();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var dialogue in _sessions.Values.ToArray())
        {
            if (!CanContinueDialogue(dialogue))
            {
                CloseSession(dialogue, sendCloseEvent: true);
                continue;
            }

            KeepDialogueNpcStationary(dialogue);
        }

        foreach (var suspended in _suspendedSessionsByUser.Values.ToArray())
        {
            if (suspended.ExpiresAt <= _timing.CurTime || !CanResumeDialogue(suspended.Dialogue))
                DiscardSuspendedSession(suspended);
        }
    }

    private void OnInteractableStartup(EntityUid uid, DialogueInteractableComponent component, ComponentStartup args)
    {
        ValidatePersistentMemoryConfiguration(uid, component);
        BeginPersistentMemoryLoads(component);
        RecalculateAutoTriggerRange();

        if (component.AutoTrigger)
            UpdateAutoTriggersForAllPlayers();
    }

    private void ValidatePersistentMemoryConfiguration(EntityUid uid, DialogueInteractableComponent component)
    {
        if (component.PersistMemory && string.IsNullOrWhiteSpace(component.PersistentMemoryKey))
        {
            Log.Warning(
                $"Dialogue persistence is enabled on {ToPrettyString(uid)}, but no persistentMemoryKey is configured.");
        }

        if (component.PersistMemory
            && component.PersistenceMode == DialogueMemoryPersistenceMode.Selected
            && component.PersistentFlags.Count == 0
            && component.PersistentCounters.Count == 0
            && component.PersistentCompletedDialogues.Count == 0)
        {
            Log.Warning(
                $"Dialogue persistence is set to Selected on {ToPrettyString(uid)}, but no persistent values are configured.");
        }
    }

    private void BeginPersistentMemoryLoads(NetUserId userId)
    {
        var query = EntityQueryEnumerator<DialogueInteractableComponent>();

        while (query.MoveNext(out _, out var component))
        {
            if (!TryGetPersistentMemoryKey(component, out var memoryKey))
                continue;

            BeginPersistentMemoryLoad(new PersistentDialogueMemoryKey(userId, memoryKey));
        }
    }

    private void BeginPersistentMemoryLoads(DialogueInteractableComponent component)
    {
        if (!TryGetPersistentMemoryKey(component, out var memoryKey))
            return;

        var query = EntityQueryEnumerator<ActorComponent>();

        while (query.MoveNext(out _, out var actor))
        {
            BeginPersistentMemoryLoad(new PersistentDialogueMemoryKey(actor.PlayerSession.UserId, memoryKey));
        }
    }

    private bool TryEnsurePersistentMemoryLoaded(
        EntityUid target,
        NetUserId userId,
        DialogueInteractableComponent component)
    {
        if (!TryGetPersistentMemoryKey(component, out var memoryKey))
            return true;

        var memoryComponent = EnsureComp<DialogueMemoryComponent>(target);
        if (memoryComponent.PersistentPlayersLoaded.Contains(userId))
            return true;

        var persistentKey = new PersistentDialogueMemoryKey(userId, memoryKey);
        _evictedPersistentMemoryKeys.TryRemove(persistentKey, out _);

        if (!_persistentMemoryCache.TryGetValue(persistentKey, out var persistentData))
        {
            BeginPersistentMemoryLoad(persistentKey);
            return false;
        }

        var memory = GetOrCreateDialogueMemory(target, userId);
        DialoguePersistentMemoryFilter.Load(memory, persistentData, component);
        memoryComponent.PersistentPlayersLoaded.Add(userId);
        return true;
    }

    private void BeginPersistentMemoryLoad(PersistentDialogueMemoryKey key)
    {
        if (_shuttingDown || _persistentMemoryCache.ContainsKey(key))
            return;

        _evictedPersistentMemoryKeys.TryRemove(key, out _);

        if (!_persistentMemoryLoads.TryAdd(key, 0))
            return;

        var generation = _persistentMemoryGenerations.GetOrAdd(key, 0);
        var task = LoadPersistentMemoryAsync(key, generation);
        _persistentMemoryLoadTasks[key] = task;

        if (task.IsCompleted)
            _persistentMemoryLoadTasks.TryRemove(key, out _);
    }

    private async Task LoadPersistentMemoryAsync(PersistentDialogueMemoryKey key, int generation)
    {
        try
        {
            var data = await _db.GetDialoguePersistentMemoryAsync(key.UserId, key.MemoryKey).ConfigureAwait(false);
            if (_persistentMemoryGenerations.GetOrAdd(key, 0) == generation)
                _persistentMemoryCache[key] = data ?? new DialoguePersistentMemoryData();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load persistent dialogue memory '{key.MemoryKey}' for {key.UserId}: {e}");
        }
        finally
        {
            _persistentMemoryLoadTasks.TryRemove(key, out _);
            _persistentMemoryLoads.TryRemove(key, out _);
            TryReleaseEvictedPersistentMemory(key);

            if (!_shuttingDown)
                _taskManager.RunOnMainThread(UpdateAutoTriggersForAllPlayers);
        }
    }

    private void SavePersistentMemory(EntityUid target, NetUserId userId)
    {
        if (!TryComp(target, out DialogueInteractableComponent? interactable)
            || !TryGetPersistentMemoryKey(interactable, out var memoryKey)
            || !TryGetDialogueMemory(target, userId, out var memory))
        {
            return;
        }

        var data = DialoguePersistentMemoryFilter.Create(memory!, interactable);
        var persistentKey = new PersistentDialogueMemoryKey(userId, memoryKey);
        if (_resettingPersistentMemoryKeys.ContainsKey(persistentKey))
            return;

        _persistentMemoryCache[persistentKey] = data;
        QueuePersistentMemoryWrite(persistentKey, data, _persistentMemoryGenerations.GetOrAdd(persistentKey, 0));
    }

    private Task QueuePersistentMemoryWrite(
        PersistentDialogueMemoryKey key,
        DialoguePersistentMemoryData data,
        int generation)
    {
        SemaphoreSlim writeLock;

        lock (_persistentMemoryWriteSync)
        {
            if (!_persistentMemoryWriteLocks.TryGetValue(key, out writeLock!))
            {
                writeLock = new SemaphoreSlim(1, 1);
                _persistentMemoryWriteLocks.Add(key, writeLock);
                _persistentMemoryPendingWrites.Add(key, 0);
            }

            _persistentMemoryPendingWrites[key]++;
        }

        var task = SavePersistentMemoryAsync(key, data, generation, writeLock);
        _persistentMemoryWriteTasks[key] = task;
        _ = RemoveCompletedPersistentMemoryWriteAsync(key, task);

        if (task.IsCompleted
            && _persistentMemoryWriteTasks.TryGetValue(key, out var current)
            && ReferenceEquals(current, task))
        {
            _persistentMemoryWriteTasks.TryRemove(key, out _);
        }

        return task;
    }

    private async Task SavePersistentMemoryAsync(
        PersistentDialogueMemoryKey key,
        DialoguePersistentMemoryData data,
        int generation,
        SemaphoreSlim writeLock)
    {
        await writeLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_persistentMemoryGenerations.GetOrAdd(key, 0) == generation)
                await _db.SetDialoguePersistentMemoryAsync(key.UserId, key.MemoryKey, data).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save persistent dialogue memory '{key.MemoryKey}' for {key.UserId}: {e}");
        }
        finally
        {
            writeLock.Release();
            CompletePersistentMemoryWrite(key, writeLock);
        }
    }

    private async Task RemoveCompletedPersistentMemoryWriteAsync(PersistentDialogueMemoryKey key, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            if (_persistentMemoryWriteTasks.TryGetValue(key, out var current) && ReferenceEquals(current, task))
                _persistentMemoryWriteTasks.TryRemove(key, out _);

            TryReleaseEvictedPersistentMemory(key);
        }
    }

    private void CompletePersistentMemoryWrite(PersistentDialogueMemoryKey key, SemaphoreSlim writeLock)
    {
        var disposeLock = false;

        lock (_persistentMemoryWriteSync)
        {
            if (!_persistentMemoryPendingWrites.TryGetValue(key, out var pending))
                return;

            pending--;
            if (pending > 0)
            {
                _persistentMemoryPendingWrites[key] = pending;
                return;
            }

            _persistentMemoryPendingWrites.Remove(key);
            if (_persistentMemoryWriteLocks.TryGetValue(key, out var current) && ReferenceEquals(current, writeLock))
            {
                _persistentMemoryWriteLocks.Remove(key);
                disposeLock = true;
            }
        }

        if (disposeLock)
            writeLock.Dispose();
    }

    private void FlushPersistentMemoryOperations()
    {
        var operations = _persistentMemoryWriteTasks.Values
            .Concat(_persistentMemoryLoadTasks.Values)
            .ToArray();

        if (operations.Length == 0)
            return;

        try
        {
            var operationTasks = Task.WhenAll(operations);
            _taskManager.BlockWaitOnTask(operationTasks);
            operationTasks.GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to flush persistent dialogue memory writes during shutdown: {e}");
        }
    }

    /// <summary>
    /// Deletes one player's persisted memory for a dialogue key and invalidates all matching live NPC memories.
    /// Intended for content-authoring and administrative recovery tools.
    /// </summary>
    public async Task ResetPersistentMemoryAsync(NetUserId userId, string memoryKey)
    {
        var key = new PersistentDialogueMemoryKey(userId, memoryKey);
        _resettingPersistentMemoryKeys[key] = 0;
        var generation = _persistentMemoryGenerations.AddOrUpdate(key, 1, static (_, current) => current + 1);
        var emptyData = new DialoguePersistentMemoryData();
        _persistentMemoryCache[key] = emptyData;
        await QueuePersistentMemoryWrite(key, emptyData, generation).ConfigureAwait(false);
        _taskManager.RunOnMainThread(() =>
        {
            InvalidatePersistentMemory(userId, memoryKey);
            _resettingPersistentMemoryKeys.TryRemove(key, out _);
        });
    }

    private void InvalidatePersistentMemory(NetUserId userId, string memoryKey)
    {
        var matchingTargets = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<DialogueInteractableComponent>();

        while (query.MoveNext(out var uid, out var interactable))
        {
            if (!TryGetPersistentMemoryKey(interactable, out var configuredKey) || configuredKey != memoryKey)
                continue;

            matchingTargets.Add(uid);

            if (TryComp(uid, out DialogueMemoryComponent? memory))
            {
                memory.Players.Remove(userId);
                memory.PersistentPlayersLoaded.Remove(userId);
            }
        }

        foreach (var dialogue in _sessions.Values
                     .Where(dialogue => dialogue.UserId == userId && matchingTargets.Contains(dialogue.Target))
                     .ToArray())
        {
            CloseSession(dialogue, sendCloseEvent: true);
        }

        foreach (var suspended in _suspendedSessionsByUser.Values
                     .Where(suspended => suspended.Dialogue.UserId == userId && matchingTargets.Contains(suspended.Dialogue.Target))
                     .ToArray())
        {
            DiscardSuspendedSession(suspended);
        }
    }

    private static bool TryGetPersistentMemoryKey(DialogueInteractableComponent component, out string memoryKey)
    {
        if (component.PersistMemory && !string.IsNullOrWhiteSpace(component.PersistentMemoryKey))
        {
            memoryKey = component.PersistentMemoryKey;
            return true;
        }

        memoryKey = string.Empty;
        return false;
    }

    private void OnDialogueDisplayNameMapInit(EntityUid uid, DialogueDisplayNameComponent component, MapInitEvent args)
    {
        if (string.IsNullOrWhiteSpace(component.Name))
            return;

        _metaData.SetEntityName(uid, component.Name);
    }

    private void OnActorMove(EntityUid uid, ActorComponent component, ref MoveEvent args)
    {
        if (_maxAutoTriggerRange <= 0f || _sessions.ContainsKey(component.PlayerSession))
            return;

        UpdateAutoTriggers(uid, component.PlayerSession);
    }

    private void OnActorStartup(EntityUid uid, ActorComponent component, ComponentStartup args)
    {
        if (_maxAutoTriggerRange <= 0f || _sessions.ContainsKey(component.PlayerSession))
            return;

        UpdateAutoTriggers(uid, component.PlayerSession);
    }

    private void OnInteractableMove(EntityUid uid, DialogueInteractableComponent component, ref MoveEvent args)
    {
        if (!component.AutoTrigger || _maxAutoTriggerRange <= 0f)
            return;

        UpdateAutoTriggersForAllPlayers();
    }

    private void UpdateAutoTriggersForAllPlayers()
    {
        if (_maxAutoTriggerRange <= 0f)
            return;

        var query = EntityQueryEnumerator<ActorComponent>();

        while (query.MoveNext(out var user, out var actor))
        {
            if (Deleted(user) || _sessions.ContainsKey(actor.PlayerSession))
                continue;

            UpdateAutoTriggers(user, actor.PlayerSession);
        }
    }

    private void RecalculateAutoTriggerRange()
    {
        _maxAutoTriggerRange = 0f;
        var query = EntityQueryEnumerator<DialogueInteractableComponent>();

        while (query.MoveNext(out _, out var component))
        {
            if (!component.AutoTrigger)
                continue;

            _maxAutoTriggerRange = MathF.Max(_maxAutoTriggerRange, component.AutoTriggerRange);
        }
    }

    private void UpdateAutoTriggers(EntityUid user, ICommonSession session)
    {
        if (_maxAutoTriggerRange <= 0f || Deleted(user))
            return;

        if (!_autoTriggerContactsByUser.TryGetValue(user, out var previous))
        {
            previous = new HashSet<EntityUid>();
            _autoTriggerContactsByUser[user] = previous;
        }

        var current = new HashSet<EntityUid>();

        foreach (var ent in _lookup.GetEntitiesInRange<DialogueInteractableComponent>(
                     Transform(user).Coordinates,
                     _maxAutoTriggerRange))
        {
            var target = ent.Owner;
            var component = ent.Comp;
            var resuming = IsResumableSuspendedSession(session, target);

            if (target == user)
                continue;

            if (!component.AutoTrigger
                || component.AutoTriggerRange <= 0f
                || !CanStartInteraction(user, target, component, popup: false, autoTrigger: true)
                || (!resuming && !TryResolveInteraction(user, target, component, out _)))
            {
                continue;
            }

            current.Add(target);

            if (previous.Contains(target))
                continue;

            if (TryStartDialogue(session, user, target, component, autoTrigger: true))
                break;
        }

        previous.Clear();
        previous.UnionWith(current);
    }

    private void OnInteractHand(EntityUid uid, DialogueInteractableComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(args.User, out ActorComponent? actor))
            return;

        if (!TryStartDialogue(actor.PlayerSession, args.User, uid, component, autoTrigger: false))
            return;

        args.Handled = true;
    }

    private void OnActivate(EntityUid uid, DialogueInteractableComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        if (!TryComp(args.User, out ActorComponent? actor))
            return;

        if (!TryStartDialogue(actor.PlayerSession, args.User, uid, component, autoTrigger: false))
            return;

        args.Handled = true;
    }

    private void OnGetVerbs(EntityUid uid, DialogueInteractableComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryComp(args.User, out ActorComponent? actor))
            return;

        if (_sessions.ContainsKey(actor.PlayerSession))
            return;

        var session = actor.PlayerSession;
        var user = args.User;
        var resuming = IsResumableSuspendedSession(session, uid);

        if (_suspendedSessionsByUser.ContainsKey(session.UserId) && !resuming)
            return;

        if (!CanStartInteraction(user, uid, component, popup: false, autoTrigger: false))
            return;

        if (resuming)
        {
            var suspended = _suspendedSessionsByUser[session.UserId];
            if (!CanAcquireTarget(suspended.Dialogue.Target, suspended.Dialogue.InteractionMode, suspended.Dialogue))
                return;
        }
        else
        {
            if (!TryResolveInteraction(user, uid, component, out var interaction)
                || !CanStartResolvedInteraction(uid, interaction))
            {
                return;
            }
        }

        var verbText = Loc.GetString(component.VerbText);

        args.Verbs.Add(new ActivationVerb
        {
            Text = verbText,
            Act = () => TryStartDialogue(session, user, uid, component, autoTrigger: false)
        });
    }

    private void OnAdvanceRequest(DialogueAdvanceRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_sessions.TryGetValue(args.SenderSession, out var session))
            return;

        if (session.SessionId != ev.SessionId)
            return;

        if (!CanProcessDialogueRequest(session))
            return;

        if (!TryGetCurrentStep(session, out var step) || step.Type == DialogueStepType.Choice)
            return;

        if (session.AutoAdvanceNotBefore > _timing.CurTime)
            return;

        if (!ExecuteActions(session, step.Actions))
        {
            CloseSession(session, sendCloseEvent: true);
            return;
        }

        if (session.Closing)
        {
            TrySendNextStateOrClose(session);
            return;
        }

        AdvanceSession(session, step.NextStep);
    }

    private void OnChoiceRequest(DialogueChoiceRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_sessions.TryGetValue(args.SenderSession, out var session))
            return;

        if (session.SessionId != ev.SessionId)
            return;

        if (!CanProcessDialogueRequest(session))
            return;

        if (!TryGetCurrentStep(session, out var step) || step.Type != DialogueStepType.Choice)
            return;

        if (ev.ChoiceIndex < 0 || ev.ChoiceIndex >= step.Choices.Count)
            return;

        var choice = step.Choices[ev.ChoiceIndex];

        if (!AreChoiceConditionsMet(session.Initiator, session.Target, session.UserId, choice))
        {
            PlayChoiceFailure(session, choice);
            return;
        }

        if (!_requirements.AreRequirementsMet(session.Initiator, step.Actions)
            || !_requirements.AreRequirementsMet(session.Initiator, choice.Actions))
        {
            PlayChoiceFailure(session, choice);
            return;
        }

        if (!ExecuteActions(session, step.Actions)
            || !ExecuteActions(session, choice.Actions))
        {
            PlayChoiceFailure(session, choice);
            return;
        }

        if (session.Closing)
        {
            TrySendNextStateOrClose(session);
            return;
        }

        var sequence = session.StepSequences[^1];
        sequence.Index++;

        if (!session.Closing && choice.ResponseSteps.Count > 0)
        {
            // A response is still part of the current dialogue.  Keep the destination
            // on its sequence so its final line can be read before the scene changes.
            session.StepSequences.Add(new ActiveDialogueSequence(
                choice.ResponseSteps,
                choice.NextStep,
                choice.NextDialogue));
        }
        else if (!session.Closing && choice.NextDialogue != null)
        {
            if (!TrySwitchDialogue(session, choice.NextDialogue.Value))
                TrySendNextStateOrClose(session);

            return;
        }
        else if (!session.Closing)
        {
            TryJumpToRootStep(session, choice.NextStep);
        }

        TrySendNextStateOrClose(session);
    }

    private void PlayChoiceFailure(ActiveDialogueSession session, DialogueChoiceOptionPrototype choice)
    {
        if (choice.FailureResponseSteps.Count == 0)
        {
            CloseSession(session, sendCloseEvent: true);
            return;
        }

        session.StepSequences.Add(new ActiveDialogueSequence(choice.FailureResponseSteps));
        TrySendNextStateOrClose(session);
    }

    private bool CanProcessDialogueRequest(ActiveDialogueSession dialogue)
    {
        if (CanContinueDialogue(dialogue))
            return true;

        CloseSession(dialogue, sendCloseEvent: true);
        return false;
    }

    private void OnCancelRequest(DialogueCancelRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_sessions.TryGetValue(args.SenderSession, out var session)
            || session.SessionId != ev.SessionId
            || !session.AllowCancel)
        {
            return;
        }

        if (session.ResumeMode == DialogueResumeMode.Continue)
        {
            var closeEvent = new DialogueCloseEvent(session.SessionId);
            SuspendSession(session);
            RaiseNetworkEvent(closeEvent, args.SenderSession);
            return;
        }

        CloseSession(session, sendCloseEvent: true);
    }

    private void OnInteractableShutdown(EntityUid uid, DialogueInteractableComponent component, ComponentShutdown args)
    {
        if (_sessionsByTarget.TryGetValue(uid, out var sessions))
        {
            foreach (var session in sessions.ToArray())
            {
                if (_sessions.TryGetValue(session, out var active))
                    CloseSession(active, sendCloseEvent: true);
            }
        }

        if (_suspendedSessionsByTarget.TryGetValue(uid, out var suspended))
        {
            foreach (var session in suspended.ToArray())
            {
                DiscardSuspendedSession(session);
            }
        }

        foreach (var contacts in _autoTriggerContactsByUser.Values)
        {
            contacts.Remove(uid);
        }

        RecalculateAutoTriggerRange();
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        BeginPersistentMemoryLoads(args.Player.UserId);
        TryResumeSession(args.Player);
    }

    private void OnAnyPlayerDetached(PlayerDetachedEvent args)
    {
        _autoTriggerContactsByUser.Remove(args.Entity);

        if (!_sessions.TryGetValue(args.Player, out var session))
            return;

        if (args.Player.Status == SessionStatus.Zombie)
            CloseSession(session, sendCloseEvent: true);
        else
            SuspendSession(session);
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent args)
    {
        CloseOrDiscardSessionsForUser(args.PlayerSession.UserId);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        switch (args.NewStatus)
        {
            case SessionStatus.Disconnected:
                if (_sessions.TryGetValue(args.Session, out var session))
                    SuspendSession(session);
                break;
            case SessionStatus.Zombie:
                CloseOrDiscardSessionsForUser(args.Session.UserId);
                break;
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        CloseOrDiscardSessionsForEntity(args.Target);
    }

    private bool TryStartDialogue(
        ICommonSession session,
        EntityUid initiator,
        EntityUid target,
        DialogueInteractableComponent component,
        bool autoTrigger)
    {
        if (_sessions.ContainsKey(session))
            return false;

        if (_suspendedSessionsByUser.TryGetValue(session.UserId, out var suspendedByUser))
        {
            if (suspendedByUser.Dialogue.Target != target
                || suspendedByUser.Dialogue.ResumeMode != DialogueResumeMode.Continue
                || !_actionBlocker.CanInteract(initiator, target)
                || !CanStartInteraction(initiator, target, component, popup: !autoTrigger, autoTrigger))
            {
                return false;
            }

            TryResumeSession(session);
            return _sessions.ContainsKey(session);
        }

        if (!_actionBlocker.CanInteract(initiator, target)
            || !CanStartInteraction(initiator, target, component, popup: !autoTrigger, autoTrigger))
        {
            return false;
        }

        if (!TryResolveInteraction(initiator, target, component, out var interaction))
            return false;

        // Entry actions have no dialogue failure branch. Reject failed transactional requirements before
        // opening a session or emitting standalone chat, rather than letting a later action continue.
        if (!_requirements.AreRequirementsMet(initiator, interaction.Actions))
            return false;

        if (interaction.DialogueId == null)
        {
            if (!CanStartResolvedInteraction(target, interaction))
                return false;

            var executed = ExecuteStandaloneInteraction(session.UserId, initiator, target, interaction);

            if (executed)
                ApplyInteractionCooldown(target, session.UserId, interaction);

            return executed;
        }

        var dialogueId = interaction.DialogueId.Value;

        if (!_prototypeManager.TryIndex(dialogueId, out var prototype))
        {
            Log.Warning($"Failed to start dialogue '{dialogueId}' for {ToPrettyString(target)}: prototype not found.");
            return false;
        }

        if (prototype.Steps.Count == 0)
        {
            Log.Warning($"Failed to start dialogue '{dialogueId}' for {ToPrettyString(target)}: no steps configured.");
            return false;
        }

        if (!TryValidateDialoguePrototype(prototype, out var rootStepIndices))
        {
            return false;
        }

        if (!prototype.Repeatable && HasCompletedDialogue(target, session.UserId, prototype.ID))
            return false;

        if (!CanAcquireTarget(target, prototype.InteractionMode))
            return false;

        var dialogue = new ActiveDialogueSession(
            session,
            _nextSessionId++,
            initiator,
            target,
            dialogueId,
            prototype,
            rootStepIndices,
            MathF.Max(component.InteractionRange, component.MaxDialogueRange),
            component.RequireLineOfSight,
            TimeSpan.FromSeconds(MathF.Max(component.ResumeGracePeriod, 0f)),
            prototype.Scene.AllowCancel,
            prototype.Scene.ResumeMode,
            prototype.InteractionMode);
        ProtectEntity(dialogue, initiator);

        if (target != initiator)
            ProtectEntity(dialogue, target);

        AddActiveSession(dialogue);

        if (!ExecuteActions(dialogue, prototype.StartActions)
            || !ExecuteActions(dialogue, interaction.Actions))
        {
            CloseSession(dialogue, sendCloseEvent: false);
            return false;
        }

        ApplyInteractionCooldown(target, session.UserId, interaction);

        if (dialogue.Closing)
        {
            CloseSession(dialogue, sendCloseEvent: false);
            return false;
        }

        return TryRaiseDialogueOpen(dialogue);
    }

    private bool IsResumableSuspendedSession(ICommonSession session, EntityUid target)
    {
        return _suspendedSessionsByUser.TryGetValue(session.UserId, out var suspended)
               && suspended.Dialogue.Target == target
               && suspended.Dialogue.ResumeMode == DialogueResumeMode.Continue;
    }

    private bool TrySwitchDialogue(ActiveDialogueSession dialogue, ProtoId<DialoguePrototype> dialogueId)
    {
        if (!_prototypeManager.TryIndex(dialogueId, out var prototype))
        {
            Log.Warning($"Failed to switch dialogue '{dialogueId}' for {ToPrettyString(dialogue.Target)}: prototype not found.");
            return false;
        }

        if (prototype.Steps.Count == 0)
        {
            Log.Warning($"Failed to switch dialogue '{dialogueId}' for {ToPrettyString(dialogue.Target)}: no steps configured.");
            return false;
        }

        if (!TryValidateDialoguePrototype(prototype, out var rootStepIndices))
        {
            return false;
        }

        if (!prototype.Repeatable && HasCompletedDialogue(dialogue.Target, dialogue.UserId, prototype.ID))
            return false;

        if (!CanAcquireTarget(dialogue.Target, prototype.InteractionMode, dialogue))
            return false;

        dialogue.DialogueId = dialogueId;
        dialogue.Prototype = prototype;
        dialogue.RootStepIndices = rootStepIndices;
        dialogue.RootSequence = new ActiveDialogueSequence(prototype.Steps);
        dialogue.StepSequences.Clear();
        dialogue.StepSequences.Add(dialogue.RootSequence);
        dialogue.AllowCancel = prototype.Scene.AllowCancel;
        dialogue.ResumeMode = prototype.Scene.ResumeMode;
        dialogue.InteractionMode = prototype.InteractionMode;
        dialogue.Completing = false;
        UpdateTargetConversationState(dialogue.Target);

        if (!ExecuteActions(dialogue, prototype.StartActions))
        {
            CloseSession(dialogue, sendCloseEvent: true);
            return true;
        }

        if (dialogue.Closing)
        {
            CloseSession(dialogue, sendCloseEvent: true);
            return true;
        }

        return TryRaiseDialogueOpen(dialogue);
    }

    private bool TryRaiseDialogueOpen(ActiveDialogueSession dialogue)
    {
        if (!TryGetCurrentStep(dialogue, out var step))
        {
            CompleteAndCloseSession(dialogue);
            return false;
        }

        if (!HasAvailableChoice(dialogue, step))
        {
            Log.Warning($"Dialogue '{dialogue.DialogueId}' has no available choices for {ToPrettyString(dialogue.Target)}.");
            CompleteAndCloseSession(dialogue);
            return false;
        }

        SetAutoAdvanceNotBefore(dialogue, step);

        RaiseNetworkEvent(
            new DialogueOpenEvent(
                dialogue.SessionId,
                GetNetEntity(dialogue.Initiator),
                GetNetEntity(dialogue.Target),
                BuildLineData(dialogue),
                dialogue.Prototype.TypewriterCps,
                BuildSceneData(dialogue.Prototype.Scene)),
            dialogue.Session);

        return true;
    }

    private void ProtectEntity(ActiveDialogueSession dialogue, EntityUid uid)
    {
        if (!dialogue.ProtectedEntities.Add(uid))
            return;

        var applyInputLock = uid == dialogue.Initiator || HasComp<ActorComponent>(uid);

        if (_entityLeases.TryGetValue(uid, out var existing))
        {
            existing.Owners++;

            if (applyInputLock && !existing.AppliedInputLock)
            {
                EnsureComp<DialogueInputLockComponent>(uid);
                existing.AppliedInputLock = true;
                _actionBlocker.UpdateCanMove(uid);
            }

            if (uid == dialogue.Target && uid != dialogue.Initiator)
                KeepDialogueNpcStationary(dialogue);

            return;
        }

        var lease = new DialogueEntityLease(
            owners: 1,
            hadGodmode: HasComp<GodmodeComponent>(uid),
            hadInputLock: HasComp<DialogueInputLockComponent>(uid));
        _entityLeases.Add(uid, lease);

        if (!lease.HadGodmode)
            _godmode.EnableGodmode(uid);

        if (applyInputLock)
        {
            EnsureComp<DialogueInputLockComponent>(uid);
            lease.AppliedInputLock = true;
            _actionBlocker.UpdateCanMove(uid);
        }

        if (TryComp<HTNComponent>(uid, out var htn) && _npc.IsAwake(uid, htn))
        {
            _npc.SleepNPC(uid, htn);
            lease.WakeNpcOnRelease = true;
        }

        if (uid == dialogue.Target && uid != dialogue.Initiator)
            KeepDialogueNpcStationary(dialogue);
    }

    /// <summary>
    /// Dialogue actors are stage participants, not autonomous movers.  HTN sleep alone does not
    /// cancel a steering path which was already registered before a conversation began, so cancel
    /// it and zero the remaining physical momentum as well.  Explicit dialogue movement remains
    /// opt-in through MoveSpeakerToSpeaker.
    /// </summary>
    private void KeepDialogueNpcStationary(ActiveDialogueSession dialogue)
    {
        var target = dialogue.Target;

        if (target == dialogue.Initiator
            || Deleted(target)
            || (!HasComp<HTNComponent>(target) && !HasComp<NPCSteeringComponent>(target))
            || dialogue.ControlledMovers.Any(mover => mover.Entity == target))
        {
            return;
        }

        _npcSteering.Unregister(target);

        if (TryComp<PhysicsComponent>(target, out var physics))
        {
            _physics.SetLinearVelocity(target, Vector2.Zero, body: physics);
            _physics.SetAngularVelocity(target, 0f, body: physics);
        }
    }

    private DialogueLineData BuildLineData(ActiveDialogueSession dialogue)
    {
        if (!TryGetCurrentStep(dialogue, out var step))
            throw new InvalidOperationException($"Dialogue session {dialogue.SessionId} has no current step.");

        var speakerId = GetSpeakerId(step);
        var choices = BuildChoiceData(dialogue, step);

        return new DialogueLineData(
            step.Speaker,
            BuildSpeakerNameData(dialogue, step, speakerId),
            step.LineType,
            BuildTextData(dialogue, step.Text, step.TextArgs),
            BuildActorStageData(dialogue, step, speakerId),
            BuildSceneStateData(step),
            step.AutoAdvanceAfter,
            dialogue.RootSequence.Index,
            dialogue.Prototype.Steps.Count,
            choices,
            BuildSoundCueData(step.Sound),
            BuildSoundCueData(step.Voice),
            BuildMusicCueData(step.Music));
    }

    private List<DialogueChoiceOptionData> BuildChoiceData(ActiveDialogueSession dialogue, DialogueStep step)
    {
        var choices = new List<DialogueChoiceOptionData>();

        if (step.Type != DialogueStepType.Choice)
            return choices;

        for (var index = 0; index < step.Choices.Count; index++)
        {
            var choice = step.Choices[index];

            if (AreChoiceConditionsMet(dialogue.Initiator, dialogue.Target, dialogue.UserId, choice))
                choices.Add(new DialogueChoiceOptionData(BuildTextData(dialogue, choice.Text, choice.TextArgs), index));
        }

        return choices;
    }

    private static string GetSpeakerId(DialogueStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.SpeakerId))
            return step.SpeakerId;

        return step.Speaker == DialogueSpeaker.Initiator ? "initiator" : "npc";
    }

    private DialogueTextData? BuildSpeakerNameData(
        ActiveDialogueSession dialogue,
        DialogueStep step,
        string speakerId)
    {
        if (step.LineType != DialogueLineType.Speech)
            return null;

        var participant = FindParticipant(dialogue.Prototype, speakerId);
        if (participant?.Name is { } name)
            return BuildTextData(dialogue, name, participant.NameArgs);

        var entity = ResolveParticipantEntity(dialogue, speakerId);
        if (entity == null || Deleted(entity.Value))
            return null;

        return new DialogueTextData(
            "heretek-dialogue-ui-raw-name",
            [new DialogueLocArgumentData("name", MetaData(entity.Value).EntityName)]);
    }

    private DialogueTextData BuildTextData(
        ActiveDialogueSession dialogue,
        LocId text,
        IReadOnlyList<DialogueLocArgumentPrototype> arguments)
    {
        return new DialogueTextData(text, ResolveLocalizationArguments(dialogue, arguments));
    }

    private List<DialogueLocArgumentData> ResolveLocalizationArguments(
        ActiveDialogueSession dialogue,
        IReadOnlyList<DialogueLocArgumentPrototype> arguments)
    {
        var result = new List<DialogueLocArgumentData>(arguments.Count);

        foreach (var argument in arguments)
        {
            switch (argument.Type)
            {
                case DialogueLocArgumentType.Literal:
                    result.Add(new DialogueLocArgumentData(argument.Id, argument.Value ?? string.Empty));
                    break;
                case DialogueLocArgumentType.Number:
                    result.Add(new DialogueLocArgumentData(argument.Id, argument.Amount));
                    break;
                case DialogueLocArgumentType.Counter:
                    TryGetDialogueMemory(dialogue.Target, dialogue.UserId, out var memory);
                    result.Add(new DialogueLocArgumentData(argument.Id, GetCounterValue(memory, argument.Counter ?? string.Empty)));
                    break;
                case DialogueLocArgumentType.BankBalance:
                    var balance = _bank.TryGetBalance(dialogue.Initiator, out var currentBalance)
                        ? currentBalance
                        : 0;
                    result.Add(new DialogueLocArgumentData(argument.Id, balance));
                    break;
                case DialogueLocArgumentType.ParticipantName:
                    result.Add(new DialogueLocArgumentData(
                        argument.Id,
                        ResolveParticipantName(dialogue, argument.Participant)));
                    break;
                case DialogueLocArgumentType.PrototypeName:
                    if (argument.Prototype != null)
                    {
                        result.Add(new DialogueLocArgumentData(
                            argument.Id,
                            argument.Prototype.Value,
                            isPrototype: true));
                    }
                    break;
            }
        }

        return result;
    }

    private string ResolveParticipantName(ActiveDialogueSession dialogue, string? participantId)
    {
        if (string.IsNullOrWhiteSpace(participantId))
            return string.Empty;

        var entity = ResolveParticipantEntity(dialogue, participantId);
        if (entity != null && !Deleted(entity.Value))
            return MetaData(entity.Value).EntityName;

        var participant = FindParticipant(dialogue.Prototype, participantId);
        return participant?.Name is { } name ? Loc.GetString(name) : participantId;
    }

    private DialogueActorStageData BuildActorStageData(
        ActiveDialogueSession dialogue,
        DialogueStep step,
        string speakerId)
    {
        var leftId = step.LeftActor ?? FindDefaultActorId(dialogue.Prototype, DialogueActorSide.Left);
        var rightId = step.RightActor ?? FindDefaultActorId(dialogue.Prototype, DialogueActorSide.Right);
        var left = BuildActorData(dialogue, step, speakerId, leftId);
        var right = BuildActorData(dialogue, step, speakerId, rightId);
        var activeSide = left?.Id == speakerId
            ? DialogueActorSide.Left
            : right?.Id == speakerId
                ? DialogueActorSide.Right
                : DialogueActorSide.Hidden;

        return new DialogueActorStageData(left, right, activeSide);
    }

    private DialogueActorData? BuildActorData(
        ActiveDialogueSession dialogue,
        DialogueStep step,
        string speakerId,
        string? participantId)
    {
        if (string.IsNullOrWhiteSpace(participantId))
            return null;

        var participant = FindParticipant(dialogue.Prototype, participantId);
        if (participantId is not ("initiator" or "npc") && participant == null)
            return null;

        var expression = step.Expressions.GetValueOrDefault(participantId);
        if (participantId == speakerId && !string.IsNullOrWhiteSpace(step.Expression))
            expression = step.Expression;

        string? portrait = null;
        if (participant != null)
        {
            if (!string.IsNullOrWhiteSpace(expression)
                && participant.Expressions.TryGetValue(expression, out var expressionPortrait))
            {
                portrait = expressionPortrait;
            }
            else if (participant.Portrait != null)
            {
                portrait = participant.Portrait.Value;
            }
        }

        var entity = ResolveParticipantEntity(dialogue, participantId);
        return new DialogueActorData(participantId, entity == null ? null : GetNetEntity(entity.Value), portrait);
    }

    private string? FindDefaultActorId(DialoguePrototype prototype, DialogueActorSide side)
    {
        if (prototype.Participants.Count == 0)
        {
            if (prototype.Scene.InitiatorSide == side)
                return "initiator";
            if (prototype.Scene.NpcSide == side)
                return "npc";
            return null;
        }

        return prototype.Participants.FirstOrDefault(participant => participant.Side == side)?.Id;
    }

    private static DialogueParticipantPrototype? FindParticipant(DialoguePrototype prototype, string participantId)
    {
        return prototype.Participants.FirstOrDefault(participant => participant.Id == participantId);
    }

    private static EntityUid? ResolveParticipantEntity(ActiveDialogueSession dialogue, string participantId)
    {
        if (participantId == "initiator")
            return dialogue.Initiator;
        if (participantId == "npc")
            return dialogue.Target;

        return FindParticipant(dialogue.Prototype, participantId)?.Source switch
        {
            DialogueParticipantSource.Initiator => dialogue.Initiator,
            DialogueParticipantSource.Target => dialogue.Target,
            _ => null
        };
    }

    private DialogueSceneStateData BuildSceneStateData(DialogueStep step)
    {
        var showWindow = step.SceneState?.ShowWindow ?? true;
        var showActors = step.SceneState?.ShowActors ?? true;
        var showDim = step.SceneState?.ShowDim ?? true;
        return new DialogueSceneStateData(showWindow, showActors, showDim);
    }

    private DialogueSceneData BuildSceneData(DialogueScenePrototype scene)
    {
        return new DialogueSceneData(
            scene.HideHud,
            scene.AllowCancel,
            scene.DimOpacity,
            scene.WindowWidth,
            scene.WindowMinHeight,
            scene.WindowMaxHeight,
            scene.WindowAnchor,
            scene.WindowMargin,
            scene.ShowActors,
            scene.InitiatorSide,
            scene.NpcSide,
            scene.DimInactiveActors,
            scene.InactiveActorOpacity,
            scene.ActorScale,
            scene.ActorWidth,
            scene.ActorHeight,
            scene.ActorGap,
            scene.ActorOverlap,
            scene.ActorWindowOverlap,
            scene.ActorStageOffsetY,
            scene.LeftActorAlignmentX,
            scene.RightActorAlignmentX,
            scene.LeftActorOffsetX,
            scene.LeftActorOffsetY,
            scene.RightActorOffsetX,
            scene.RightActorOffsetY,
            scene.SpeakerFontSize,
            scene.BodyFontSize,
            scene.ContinueFontSize,
            scene.DuckBackgroundMusic,
            scene.BackgroundMusicDuckGain,
            BuildMusicCueData(scene.Music));
    }

    private DialogueSoundCueData? BuildSoundCueData(SoundSpecifier? sound)
    {
        if (sound == null)
            return null;

        return new DialogueSoundCueData(_audio.ResolveSound(sound), sound.Params);
    }

    private DialogueMusicCueData? BuildMusicCueData(DialogueMusicCuePrototype? cue)
    {
        if (cue == null)
            return null;

        if (cue.Stop)
            return new DialogueMusicCueData(null, default, cue.FadeIn, cue.FadeOut, true);

        if (cue.Sound == null)
            return null;

        var audio = cue.Sound.Params;
        audio.Loop = true;
        return new DialogueMusicCueData(_audio.ResolveSound(cue.Sound), audio, cue.FadeIn, cue.FadeOut, false);
    }

    private bool CanStartInteraction(
        EntityUid initiator,
        EntityUid target,
        DialogueInteractableComponent component,
        bool popup,
        bool autoTrigger)
    {
        if (TerminatingOrDeleted(initiator)
            || TerminatingOrDeleted(target)
            || IsDialogueParticipantIncapacitated(initiator)
            || IsDialogueParticipantIncapacitated(target))
        {
            return false;
        }

        var range = autoTrigger ? component.AutoTriggerRange : component.InteractionRange;
        return IsWithinDialogueRange(initiator, target, range, component.RequireLineOfSight, popup);
    }

    private bool TryResolveInteraction(
        EntityUid initiator,
        EntityUid target,
        DialogueInteractableComponent component,
        out ResolvedDialogueInteraction interaction)
    {
        if (!TryComp(initiator, out ActorComponent? actor))
        {
            interaction = default;
            return false;
        }

        return TryResolveInteraction(initiator, actor.PlayerSession.UserId, target, component, out interaction);
    }

    private bool TryResolveInteraction(
        EntityUid initiator,
        NetUserId userId,
        EntityUid target,
        DialogueInteractableComponent component,
        out ResolvedDialogueInteraction interaction)
    {
        if (!TryEnsurePersistentMemoryLoaded(target, userId, component))
        {
            interaction = default;
            return false;
        }

        TryGetDialogueMemory(target, userId, out var memory);

        for (var i = 0; i < component.Dialogues.Count; i++)
        {
            var entry = component.Dialogues[i];

            if (!AreConditionsMet(initiator, target, userId, entry.Conditions))
                continue;

            if (entry.Dialogue != null
                && _prototypeManager.TryIndex(entry.Dialogue.Value, out var entryPrototype)
                && !entryPrototype.Repeatable
                && HasCompletedDialogue(target, userId, entryPrototype.ID))
            {
                continue;
            }

            if (entry.Dialogue == null && entry.Chat == null && entry.Actions.Count == 0)
                continue;

            if (IsInteractionOnCooldown(memory, entry, i))
            {
                interaction = default;
                return false;
            }

            interaction = new ResolvedDialogueInteraction(
                entry.Dialogue,
                entry.Chat,
                entry.Speaker,
                entry.Actions,
                GetInteractionCooldownKey(entry, i),
                entry.Cooldown);
            return true;
        }

        var baseDialogue = component.BaseDialogue ?? component.Dialogue;

        if (baseDialogue != null)
        {
            if (_prototypeManager.TryIndex(baseDialogue.Value, out var fallbackPrototype)
                && !fallbackPrototype.Repeatable
                && HasCompletedDialogue(target, userId, fallbackPrototype.ID))
            {
                interaction = default;
                return false;
            }

            interaction = new ResolvedDialogueInteraction(baseDialogue, null, DialogueSpeaker.Npc, Array.Empty<DialogueActionPrototype>(), null, 0f);
            return true;
        }

        interaction = default;
        return false;
    }

    private bool CanStartResolvedInteraction(EntityUid target, ResolvedDialogueInteraction interaction)
    {
        if (interaction.DialogueId is not { } dialogueId)
            return CanAcquireTarget(target, DialogueInteractionMode.Personal);

        return !_prototypeManager.TryIndex(dialogueId, out var prototype)
               || CanAcquireTarget(target, prototype.InteractionMode);
    }

    /// <summary>
    /// Personal sessions coexist with other personal sessions. A shared-world session conflicts
    /// with every other active or resumable session, including personal ones.
    /// </summary>
    private bool CanAcquireTarget(
        EntityUid target,
        DialogueInteractionMode requestedMode,
        ActiveDialogueSession? ignored = null)
    {
        if (_sessionsByTarget.TryGetValue(target, out var activeSessions))
        {
            foreach (var session in activeSessions)
            {
                if (!_sessions.TryGetValue(session, out var active) || ReferenceEquals(active, ignored))
                    continue;

                if (requestedMode == DialogueInteractionMode.SharedWorld
                    || active.InteractionMode == DialogueInteractionMode.SharedWorld)
                {
                    return false;
                }
            }
        }

        if (_suspendedSessionsByTarget.TryGetValue(target, out var suspendedSessions))
        {
            foreach (var suspended in suspendedSessions)
            {
                var active = suspended.Dialogue;
                if (ReferenceEquals(active, ignored))
                    continue;

                if (requestedMode == DialogueInteractionMode.SharedWorld
                    || active.InteractionMode == DialogueInteractionMode.SharedWorld)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool ExecuteStandaloneInteraction(
        NetUserId userId,
        EntityUid initiator,
        EntityUid target,
        ResolvedDialogueInteraction interaction)
    {
        if (!ExecuteStandaloneActions(userId, initiator, target, interaction.Actions))
            return false;

        if (interaction.Chat == null)
            return interaction.Actions.Count > 0;

        var speaker = ResolveSpeakerEntity(initiator, target, interaction.Speaker);

        if (speaker == null || Deleted(speaker.Value))
            return interaction.Actions.Count > 0;

        _chat.TrySendInGameICMessage(
            speaker.Value,
            Loc.GetString(interaction.Chat),
            InGameICChatType.Speak,
            false,
            ignoreActionBlocker: true);
        return true;
    }

    private bool AreConditionsMet(
        EntityUid initiator,
        EntityUid target,
        NetUserId userId,
        IReadOnlyList<DialogueConditionPrototype> conditions)
    {
        for (var i = 0; i < conditions.Count; i++)
        {
            if (EvaluateCondition(initiator, target, userId, conditions[i]))
                continue;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Payment and card availability conditions normally hide an unavailable choice. When the choice performs a
    /// transactional action, keep it visible so the server-side pre-check can either play its refusal branch or
    /// safely close the session when no branch was authored. Narrative conditions (flags, counters, completion,
    /// stores) always retain their normal hiding behavior.
    /// </summary>
    private bool AreChoiceConditionsMet(
        EntityUid initiator,
        EntityUid target,
        NetUserId userId,
        DialogueChoiceOptionPrototype choice)
    {
        for (var i = 0; i < choice.Conditions.Count; i++)
        {
            var condition = choice.Conditions[i];
            if (choice.Actions.Any(action => DialogueActionRequirementSystem.IsRequirementAction(action.Type))
                && condition.Type is DialogueConditionType.ItemCountAtLeast or DialogueConditionType.BankBalanceAtLeast)
            {
                continue;
            }

            if (!EvaluateCondition(initiator, target, userId, condition))
                return false;
        }

        return true;
    }

    private bool IsInteractionOnCooldown(
        DialoguePlayerMemory? memory,
        DialogueInteractableEntry entry,
        int index)
    {
        if (entry.Cooldown <= 0f)
            return false;

        var key = GetInteractionCooldownKey(entry, index);

        if (key == null || memory == null || !memory.Cooldowns.TryGetValue(key, out var cooldownEnd))
            return false;

        return cooldownEnd > _timing.CurTime;
    }

    private void ApplyInteractionCooldown(
        EntityUid target,
        NetUserId userId,
        ResolvedDialogueInteraction interaction)
    {
        if (interaction.Cooldown <= 0f || interaction.CooldownKey == null)
            return;

        var memory = GetOrCreateDialogueMemory(target, userId);
        memory.Cooldowns[interaction.CooldownKey] = _timing.CurTime + TimeSpan.FromSeconds(interaction.Cooldown);
    }

    private static string? GetInteractionCooldownKey(DialogueInteractableEntry entry, int index)
    {
        if (entry.Cooldown <= 0f)
            return null;

        if (!string.IsNullOrWhiteSpace(entry.CooldownKey))
            return entry.CooldownKey;

        if (entry.Dialogue != null)
            return $"dialogue:{entry.Dialogue.Value}";

        if (entry.Chat != null)
            return $"chat:{entry.Chat.Value}";

        return $"entry:{index}";
    }

    private bool EvaluateCondition(
        EntityUid initiator,
        EntityUid target,
        NetUserId userId,
        DialogueConditionPrototype condition)
    {
        TryGetDialogueMemory(target, userId, out var memory);

        switch (condition.Type)
        {
            case DialogueConditionType.Flag:
            {
                if (string.IsNullOrWhiteSpace(condition.Flag))
                    return false;

                var hasFlag = memory?.Flags.Contains(condition.Flag) ?? false;
                return hasFlag == condition.Value;
            }
            case DialogueConditionType.CounterAtLeast:
            {
                if (string.IsNullOrWhiteSpace(condition.Counter))
                    return false;

                return GetCounterValue(memory, condition.Counter) >= condition.Amount;
            }
            case DialogueConditionType.CounterAtMost:
            {
                if (string.IsNullOrWhiteSpace(condition.Counter))
                    return false;

                return GetCounterValue(memory, condition.Counter) <= condition.Amount;
            }
            case DialogueConditionType.CounterEquals:
            {
                if (string.IsNullOrWhiteSpace(condition.Counter))
                    return false;

                return GetCounterValue(memory, condition.Counter) == condition.Amount;
            }
            case DialogueConditionType.DialogueCompleted:
            {
                if (condition.Dialogue == null)
                    return false;

                if (!_prototypeManager.TryIndex(condition.Dialogue.Value, out var prototype))
                    return false;

                var completed = memory?.CompletedDialogues.Contains(prototype.ID) ?? false;
                return completed == condition.Value;
            }
            case DialogueConditionType.StoreAvailable:
                return HasComp<StoreComponent>(target) == condition.Value;
            case DialogueConditionType.ItemCountAtLeast:
                return condition.Prototype != null
                       && condition.Amount > 0
                       && _items.CountItems(initiator, condition.Source, condition.Prototype.Value) >= condition.Amount;
            case DialogueConditionType.BankBalanceAtLeast:
                return condition.Amount > 0
                       && TryComp(initiator, out BankAccountComponent? bank)
                       && _bank.TryGetBalance(initiator, out var balance)
                       && balance >= condition.Amount;
            default:
                return false;
        }
    }

    private bool ExecuteActions(ActiveDialogueSession dialogue, IReadOnlyList<DialogueActionPrototype> actions)
    {
        var previousActionSucceeded = true;

        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action.OnlyIfPreviousActionSucceeded && !previousActionSucceeded)
                continue;

            previousActionSucceeded = ExecuteActionCore(dialogue, dialogue.UserId, dialogue.Initiator, dialogue.Target, action);

            if (!previousActionSucceeded && DialogueActionRequirementSystem.IsRequirementAction(action.Type))
                return false;

            if (dialogue.Closing)
                return true;
        }

        return true;
    }

    private bool ExecuteStandaloneActions(
        NetUserId userId,
        EntityUid initiator,
        EntityUid target,
        IReadOnlyList<DialogueActionPrototype> actions)
    {
        var previousActionSucceeded = true;

        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action.OnlyIfPreviousActionSucceeded && !previousActionSucceeded)
                continue;

            previousActionSucceeded = ExecuteActionCore(null, userId, initiator, target, action);

            if (!previousActionSucceeded && DialogueActionRequirementSystem.IsRequirementAction(action.Type))
                return false;
        }

        return true;
    }

    private bool ExecuteActionCore(
        ActiveDialogueSession? dialogue,
        NetUserId userId,
        EntityUid initiator,
        EntityUid target,
        DialogueActionPrototype action)
    {
        switch (action.Type)
        {
            case DialogueActionType.GiveItem:
            {
                if (action.Prototype == null || Deleted(initiator))
                    return false;

                var item = Spawn(action.Prototype.Value, Transform(initiator).Coordinates);
                // The entity is spawned at the initiator's feet. If all hands are occupied,
                // it remains there instead of cancelling the reward.
                _hands.TryPickupAnyHand(initiator, item, checkActionBlocker: false);
                return true;
            }
            case DialogueActionType.TakeItem:
                return action.Prototype != null
                       && action.Amount > 0
                       && _items.TryTakeItems(initiator, action.Source, action.Prototype.Value, action.Amount);
            case DialogueActionType.DebitBankAccount:
                return action.Amount > 0
                       && !Deleted(initiator)
                       && _bank.TryBankWithdraw(initiator, action.Amount);
            case DialogueActionType.CreditBankAccount:
                return action.Amount > 0
                       && !Deleted(initiator)
                       && _bank.TryBankCredit(initiator, action.Amount);
            case DialogueActionType.AddAccess:
                return _access.TryModifyAccess(
                    initiator,
                    action.AccessCardSource,
                    action.Accesses,
                    add: true);
            case DialogueActionType.RemoveAccess:
                return _access.TryModifyAccess(
                    initiator,
                    action.AccessCardSource,
                    action.Accesses,
                    add: false);
            case DialogueActionType.SendChat:
            {
                var speaker = ResolveSpeakerEntity(initiator, target, action.Speaker);

                if (speaker == null || Deleted(speaker.Value) || action.Message is not { } message)
                    return false;

                var messageArguments = dialogue == null
                    ? Array.Empty<(string, object)>()
                    : ResolveLocalizationArguments(dialogue, action.MessageArgs)
                        .Select(argument => (argument.Id, ResolveServerLocalizationArgument(argument)))
                        .ToArray();

                _chat.TrySendInGameICMessage(
                    speaker.Value,
                    Loc.GetString(message, messageArguments),
                    InGameICChatType.Speak,
                    action.HideChat,
                    ignoreActionBlocker: true);
                return true;
            }
            case DialogueActionType.OpenStore:
            {
                if (Deleted(initiator) || !TryComp(target, out StoreComponent? store))
                    return false;

                _store.ToggleUi(initiator, target, store);

                if (dialogue != null)
                {
                    dialogue.Closing = true;
                    dialogue.AbortRequested = true;
                }

                return true;
            }
            case DialogueActionType.SetFlag:
            {
                if (string.IsNullOrWhiteSpace(action.Flag))
                    return false;

                var memory = GetOrCreateDialogueMemory(target, userId);

                if (action.Value)
                    memory.Flags.Add(action.Flag);
                else
                    memory.Flags.Remove(action.Flag);

                SavePersistentMemory(target, userId);

                return true;
            }
            case DialogueActionType.AddCounter:
            {
                if (string.IsNullOrWhiteSpace(action.Counter))
                    return false;

                var memory = GetOrCreateDialogueMemory(target, userId);
                memory.Counters[action.Counter] = GetCounterValue(memory, action.Counter) + action.Amount;
                SavePersistentMemory(target, userId);
                return true;
            }
            case DialogueActionType.SetCounter:
            {
                if (string.IsNullOrWhiteSpace(action.Counter))
                    return false;

                var memory = GetOrCreateDialogueMemory(target, userId);
                memory.Counters[action.Counter] = action.Amount;
                SavePersistentMemory(target, userId);
                return true;
            }
            case DialogueActionType.CloseDialogue:
                if (dialogue == null)
                    return false;

                dialogue.Closing = true;
                dialogue.AbortRequested = true;
                return true;
            case DialogueActionType.MoveSpeakerToSpeaker:
            {
                if (dialogue == null)
                    return false;

                var mover = ResolveSpeakerEntity(initiator, target, action.Speaker);
                var moveTarget = ResolveSpeakerEntity(initiator, target, action.TargetSpeaker);

                if (mover == null || moveTarget == null || mover == moveTarget || Deleted(mover.Value) || Deleted(moveTarget.Value))
                    return false;

                if (HasComp<DialogueInputLockComponent>(mover.Value))
                {
                    Log.Warning($"Dialogue movement skipped for {ToPrettyString(mover.Value)}: scripted movement while input-locked is not supported yet.");
                    return false;
                }

                EnsureDialogueMovement(dialogue, mover.Value);

                var steering = _npcSteering.Register(
                    mover.Value,
                    new EntityCoordinates(moveTarget.Value, new Vector2(action.OffsetX, action.OffsetY)));
                steering.Range = MathF.Max(0.01f, action.Range);
                steering.DirectMove = action.DirectMove;
                steering.InRangeMaxSpeed = action.InRangeMaxSpeed;
                steering.Status = SteeringStatus.Moving;
                steering.FailedPathCount = 0;
                return true;
            }
            case DialogueActionType.StopSpeakerMovement:
            {
                var speaker = ResolveSpeakerEntity(initiator, target, action.Speaker);

                if (speaker == null || Deleted(speaker.Value))
                    return false;

                _npcSteering.Unregister(speaker.Value);
                return true;
            }
            case DialogueActionType.SleepSpeakerAi:
            {
                var speaker = ResolveSpeakerEntity(initiator, target, action.Speaker);

                if (speaker == null || !TryComp<HTNComponent>(speaker.Value, out var htn))
                    return false;

                _npc.SleepNPC(speaker.Value, htn);
                SuppressNpcWakeOnRelease(dialogue, speaker.Value);
                return true;
            }
            case DialogueActionType.WakeSpeakerAi:
            {
                var speaker = ResolveSpeakerEntity(initiator, target, action.Speaker);

                if (speaker == null || !TryComp<HTNComponent>(speaker.Value, out var htn))
                    return false;

                _npc.WakeNPC(speaker.Value, htn);
                return true;
            }
            case DialogueActionType.RotateSpeakerRelative:
            {
                var speaker = ResolveSpeakerEntity(initiator, target, action.Speaker);

                if (speaker == null || Deleted(speaker.Value))
                    return false;

                var xform = Transform(speaker.Value);
                _transform.SetLocalRotation(speaker.Value, xform.LocalRotation + Angle.FromDegrees(action.Degrees), xform);
                return true;
            }
            case DialogueActionType.FaceSpeaker:
            {
                var speaker = ResolveSpeakerEntity(initiator, target, action.Speaker);
                var faceTarget = ResolveSpeakerEntity(initiator, target, action.TargetSpeaker);

                if (speaker == null || faceTarget == null || speaker == faceTarget || Deleted(speaker.Value) || Deleted(faceTarget.Value))
                    return false;

                var speakerPosition = _transform.GetWorldPosition(speaker.Value);
                var targetPosition = _transform.GetWorldPosition(faceTarget.Value);
                var diff = targetPosition - speakerPosition;

                if (diff.LengthSquared() <= 0.0001f)
                    return false;

                _transform.SetWorldRotation(speaker.Value, Angle.FromWorldVec(diff));
                return true;
            }
        }

        return false;
    }

    private void SuppressNpcWakeOnRelease(ActiveDialogueSession? dialogue, EntityUid uid)
    {
        if (dialogue == null || !dialogue.ProtectedEntities.Contains(uid))
            return;

        if (_entityLeases.TryGetValue(uid, out var lease))
            lease.WakeNpcOnRelease = false;
    }

    private void EnsureDialogueMovement(ActiveDialogueSession dialogue, EntityUid uid)
    {
        if (dialogue.ControlledMovers.Any(controlled => controlled.Entity == uid))
            return;

        var hadMarker = HasComp<DialogueMovementActiveComponent>(uid);
        EnsureComp<DialogueMovementActiveComponent>(uid);
        dialogue.ControlledMovers.Add(new ControlledDialogueMovement(uid, hadMarker));
    }

    private EntityUid? ResolveSpeakerEntity(ActiveDialogueSession dialogue, DialogueSpeaker speaker)
    {
        return ResolveSpeakerEntity(dialogue.Initiator, dialogue.Target, speaker);
    }

    private EntityUid? ResolveSpeakerEntity(EntityUid initiator, EntityUid target, DialogueSpeaker speaker)
    {
        return speaker == DialogueSpeaker.Initiator ? initiator : target;
    }

    private bool TryGetDialogueMemory(
        EntityUid target,
        NetUserId userId,
        out DialoguePlayerMemory? memory)
    {
        memory = null;

        if (!TryComp<DialogueMemoryComponent>(target, out var component))
            return false;

        return component.Players.TryGetValue(userId, out memory);
    }

    private DialoguePlayerMemory GetOrCreateDialogueMemory(EntityUid target, NetUserId userId)
    {
        var component = EnsureComp<DialogueMemoryComponent>(target);

        if (component.Players.TryGetValue(userId, out var memory))
            return memory;

        memory = new DialoguePlayerMemory();
        component.Players[userId] = memory;
        return memory;
    }

    private bool HasCompletedDialogue(EntityUid target, NetUserId userId, string dialogueId)
    {
        return TryGetDialogueMemory(target, userId, out var memory)
               && memory!.CompletedDialogues.Contains(dialogueId);
    }

    private static int GetCounterValue(DialoguePlayerMemory? memory, string counter)
    {
        if (memory == null || !memory.Counters.TryGetValue(counter, out var value))
            return 0;

        return value;
    }

    private bool CanContinueDialogue(ActiveDialogueSession dialogue)
    {
        if (dialogue.Session.Status != SessionStatus.InGame
            || dialogue.Session.AttachedEntity != dialogue.Initiator)
        {
            return false;
        }

        return CanResumeDialogue(dialogue);
    }

    private bool CanResumeDialogue(ActiveDialogueSession dialogue)
    {
        if (Deleted(dialogue.Initiator)
            || Deleted(dialogue.Target)
            || IsDialogueParticipantIncapacitated(dialogue.Initiator)
            || IsDialogueParticipantIncapacitated(dialogue.Target)
            || !HasComp<DialogueInteractableComponent>(dialogue.Target))
        {
            return false;
        }

        return IsWithinDialogueRange(
            dialogue.Initiator,
            dialogue.Target,
            dialogue.MaxDialogueRange,
            dialogue.RequireLineOfSight);
    }

    private bool IsDialogueParticipantIncapacitated(EntityUid uid)
    {
        return TryComp(uid, out MobStateComponent? mobState)
               && mobState.CurrentState != MobState.Alive;
    }

    private bool IsWithinDialogueRange(
        EntityUid initiator,
        EntityUid target,
        float range,
        bool requireLineOfSight,
        bool popup = false)
    {
        if (range <= 0f)
            return false;

        if (requireLineOfSight)
            return _interaction.InRangeUnobstructed(initiator, target, range, popup: popup);

        return Transform(initiator).Coordinates.TryDistance(EntityManager, Transform(target).Coordinates, out var distance)
               && distance <= range;
    }

    private void SuspendSession(ActiveDialogueSession dialogue)
    {
        if (!RemoveActiveSession(dialogue))
            return;

        ReleaseDialogueRuntimeState(dialogue);

        if (dialogue.ResumeMode != DialogueResumeMode.Continue
            || dialogue.ResumeGracePeriod <= TimeSpan.Zero
            || !CanResumeDialogue(dialogue))
        {
            return;
        }

        if (_suspendedSessionsByUser.TryGetValue(dialogue.UserId, out var existing))
            DiscardSuspendedSession(existing);

        var suspended = new SuspendedDialogueSession(
            dialogue,
            _timing.CurTime + dialogue.ResumeGracePeriod);
        _suspendedSessionsByUser.Add(dialogue.UserId, suspended);

        if (!_suspendedSessionsByTarget.TryGetValue(dialogue.Target, out var targetSessions))
        {
            targetSessions = new HashSet<SuspendedDialogueSession>();
            _suspendedSessionsByTarget.Add(dialogue.Target, targetSessions);
        }

        targetSessions.Add(suspended);
    }

    private void TryResumeSession(ICommonSession session)
    {
        if (!_suspendedSessionsByUser.TryGetValue(session.UserId, out var suspended))
            return;

        var dialogue = suspended.Dialogue;
        var attached = session.AttachedEntity;

        if (session.Status != SessionStatus.InGame
            || attached == null
            || attached.Value != dialogue.Initiator
            || dialogue.ResumeMode != DialogueResumeMode.Continue
            || _sessions.ContainsKey(session)
            || suspended.ExpiresAt <= _timing.CurTime
            || !CanResumeDialogue(dialogue))
        {
            DiscardSuspendedSession(suspended);
            return;
        }

        if (!CanAcquireTarget(dialogue.Target, dialogue.InteractionMode, dialogue))
            return;

        RemoveSuspendedSession(suspended);
        dialogue.Session = session;
        dialogue.SessionId = _nextSessionId++;
        AddActiveSession(dialogue);
        ProtectEntity(dialogue, dialogue.Initiator);

        if (dialogue.Target != dialogue.Initiator)
            ProtectEntity(dialogue, dialogue.Target);

        if (dialogue.Closing || !TryGetCurrentStep(dialogue, out _))
        {
            CloseSession(dialogue, sendCloseEvent: false);
            return;
        }

        TryRaiseDialogueOpen(dialogue);
    }

    private void AddActiveSession(ActiveDialogueSession dialogue)
    {
        _sessions.Add(dialogue.Session, dialogue);

        if (!_sessionsByTarget.TryGetValue(dialogue.Target, out var targetSessions))
        {
            targetSessions = new HashSet<ICommonSession>();
            _sessionsByTarget.Add(dialogue.Target, targetSessions);
        }

        targetSessions.Add(dialogue.Session);
        UpdateTargetConversationState(dialogue.Target);
    }

    private bool RemoveActiveSession(ActiveDialogueSession dialogue)
    {
        if (!_sessions.Remove(dialogue.Session, out _))
            return false;

        if (_sessionsByTarget.TryGetValue(dialogue.Target, out var targetSessions))
        {
            targetSessions.Remove(dialogue.Session);

            if (targetSessions.Count == 0)
                _sessionsByTarget.Remove(dialogue.Target);
        }

        UpdateTargetConversationState(dialogue.Target);

        return true;
    }

    private void UpdateTargetConversationState(EntityUid target)
    {
        if (Deleted(target))
            return;

        if (!_sessionsByTarget.TryGetValue(target, out var sessions) || sessions.Count == 0)
        {
            RemComp<DialogueConversationComponent>(target);
            return;
        }

        var component = EnsureComp<DialogueConversationComponent>(target);
        component.ActiveSessions = sessions.Count;
        component.HasSharedWorldSession = sessions.Any(session =>
            _sessions.TryGetValue(session, out var active)
            && active.InteractionMode == DialogueInteractionMode.SharedWorld);
        Dirty(target, component);
    }

    private void RemoveSuspendedSession(SuspendedDialogueSession suspended)
    {
        _suspendedSessionsByUser.Remove(suspended.Dialogue.UserId);

        if (!_suspendedSessionsByTarget.TryGetValue(suspended.Dialogue.Target, out var targetSessions))
            return;

        targetSessions.Remove(suspended);
        if (targetSessions.Count == 0)
            _suspendedSessionsByTarget.Remove(suspended.Dialogue.Target);
    }

    private void DiscardSuspendedSession(SuspendedDialogueSession suspended)
    {
        RemoveSuspendedSession(suspended);

        foreach (var contacts in _autoTriggerContactsByUser.Values)
        {
            contacts.Remove(suspended.Dialogue.Target);
        }
    }

    private object ResolveServerLocalizationArgument(DialogueLocArgumentData argument)
    {
        if (argument.Text != null)
            return argument.Text;
        if (argument.Number != null)
            return argument.Number.Value;
        if (argument.Prototype != null
            && _prototypeManager.TryIndex<EntityPrototype>(argument.Prototype, out var prototype))
        {
            return prototype.Name;
        }

        return argument.Prototype ?? string.Empty;
    }

    private void CloseOrDiscardSessionsForUser(NetUserId userId)
    {
        foreach (var dialogue in _sessions.Values.Where(dialogue => dialogue.UserId == userId).ToArray())
        {
            CloseSession(dialogue, sendCloseEvent: true);
        }

        if (_suspendedSessionsByUser.TryGetValue(userId, out var suspended))
            DiscardSuspendedSession(suspended);

        ClearPersistentMemoryCache(userId);
    }

    private void ClearPersistentMemoryCache(NetUserId userId)
    {
        var memoryQuery = EntityQueryEnumerator<DialogueMemoryComponent>();

        while (memoryQuery.MoveNext(out _, out var memory))
        {
            memory.Players.Remove(userId);
            memory.PersistentPlayersLoaded.Remove(userId);
        }

        var keys = _persistentMemoryCache.Keys
            .Concat(_persistentMemoryGenerations.Keys)
            .Concat(_persistentMemoryLoads.Keys)
            .Concat(_persistentMemoryWriteTasks.Keys)
            .Where(key => key.UserId == userId)
            .ToHashSet();

        foreach (var key in keys)
        {
            // Let the last accepted write complete; it already carries the newest cache snapshot.
            // Loads, on the other hand, must be invalidated so they cannot restore stale data after logout.
            if (!_persistentMemoryWriteTasks.ContainsKey(key))
                _persistentMemoryGenerations.AddOrUpdate(key, 1, static (_, generation) => generation + 1);

            _evictedPersistentMemoryKeys[key] = 0;
            TryReleaseEvictedPersistentMemory(key);
        }
    }

    private void TryReleaseEvictedPersistentMemory(PersistentDialogueMemoryKey key)
    {
        if (!_evictedPersistentMemoryKeys.ContainsKey(key)
            || _persistentMemoryLoads.ContainsKey(key)
            || _persistentMemoryWriteTasks.ContainsKey(key))
        {
            return;
        }

        _persistentMemoryCache.TryRemove(key, out _);
        _persistentMemoryGenerations.TryRemove(key, out _);
        _evictedPersistentMemoryKeys.TryRemove(key, out _);
    }

    private void CloseOrDiscardSessionsForEntity(EntityUid entity)
    {
        foreach (var dialogue in _sessions.Values
                     .Where(dialogue => dialogue.Initiator == entity || dialogue.Target == entity)
                     .ToArray())
        {
            CloseSession(dialogue, sendCloseEvent: true);
        }

        foreach (var suspended in _suspendedSessionsByUser.Values
                     .Where(suspended => suspended.Dialogue.Initiator == entity || suspended.Dialogue.Target == entity)
                     .ToArray())
        {
            DiscardSuspendedSession(suspended);
        }
    }

    private void CompleteAndCloseSession(ActiveDialogueSession dialogue, bool sendCloseEvent = true)
    {
        if (dialogue.Completing)
            return;

        dialogue.Completing = true;

        if (!ExecuteActions(dialogue, dialogue.Prototype.CompleteActions) || dialogue.Closing)
        {
            CloseSession(dialogue, sendCloseEvent);
            return;
        }

        var memory = GetOrCreateDialogueMemory(dialogue.Target, dialogue.UserId);
        memory.CompletedDialogues.Add(dialogue.Prototype.ID);
        SavePersistentMemory(dialogue.Target, dialogue.UserId);
        CloseSession(dialogue, sendCloseEvent);
    }

    private void CloseSession(ActiveDialogueSession dialogue, bool sendCloseEvent)
    {
        if (!RemoveActiveSession(dialogue))
            return;

        ReleaseDialogueRuntimeState(dialogue);

        if (sendCloseEvent)
            RaiseNetworkEvent(new DialogueCloseEvent(dialogue.SessionId), dialogue.Session);
    }

    private void ReleaseDialogueRuntimeState(ActiveDialogueSession dialogue)
    {

        foreach (var controlled in dialogue.ControlledMovers)
        {
            if (Deleted(controlled.Entity))
                continue;

            _npcSteering.Unregister(controlled.Entity);

            if (!controlled.HadMarker)
                RemComp<DialogueMovementActiveComponent>(controlled.Entity);
        }

        dialogue.ControlledMovers.Clear();

        foreach (var participant in dialogue.ProtectedEntities)
        {
            ReleaseEntityLease(participant);
        }

        dialogue.ProtectedEntities.Clear();
    }

    private void ReleaseEntityLease(EntityUid uid)
    {
        if (!_entityLeases.TryGetValue(uid, out var lease))
            return;

        lease.Owners--;
        if (lease.Owners > 0)
            return;

        _entityLeases.Remove(uid);
        if (Deleted(uid))
            return;

        if (!lease.HadGodmode)
            _godmode.DisableGodmode(uid);

        if (lease.AppliedInputLock && !lease.HadInputLock)
            RemComp<DialogueInputLockComponent>(uid);

        if (lease.WakeNpcOnRelease && TryComp<HTNComponent>(uid, out var htn))
            _npc.WakeNPC(uid, htn);

        _actionBlocker.UpdateCanMove(uid);
    }

    private void AdvanceSession(ActiveDialogueSession dialogue, string? nextStep)
    {
        if (!TryGetCurrentStep(dialogue, out _))
        {
            CompleteAndCloseSession(dialogue);
            return;
        }

        var sequence = dialogue.StepSequences[^1];
        if (!TryJumpToRootStep(dialogue, nextStep))
            sequence.Index++;

        if (sequence.Index >= sequence.Steps.Count && sequence.NextDialogue != null)
        {
            if (!TrySwitchDialogue(dialogue, sequence.NextDialogue.Value))
                TrySendNextStateOrClose(dialogue);

            return;
        }

        TrySendNextStateOrClose(dialogue);
    }

    private void TrySendNextStateOrClose(ActiveDialogueSession dialogue)
    {
        if (dialogue.AbortRequested)
        {
            CloseSession(dialogue, sendCloseEvent: true);
            return;
        }

        if (dialogue.Closing
            || !TryGetCurrentStep(dialogue, out var step)
            || !HasAvailableChoice(dialogue, step))
        {
            CompleteAndCloseSession(dialogue);
            return;
        }

        SetAutoAdvanceNotBefore(dialogue, step);
        RaiseNetworkEvent(new DialogueLineUpdateEvent(dialogue.SessionId, BuildLineData(dialogue)), dialogue.Session);
    }

    private void SetAutoAdvanceNotBefore(ActiveDialogueSession dialogue, DialogueStep step)
    {
        dialogue.AutoAdvanceNotBefore = step.AutoAdvanceAfter is { } delay
            ? _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(delay, 0f))
            : TimeSpan.Zero;
    }

    private bool HasAvailableChoice(ActiveDialogueSession dialogue, DialogueStep step)
    {
        return step.Type != DialogueStepType.Choice
               || step.Choices.Any(choice => AreChoiceConditionsMet(dialogue.Initiator, dialogue.Target, dialogue.UserId, choice));
    }

    private bool TryGetCurrentStep(ActiveDialogueSession dialogue, out DialogueStep step)
    {
        NormalizeSequenceStack(dialogue);

        if (dialogue.StepSequences.Count == 0)
        {
            step = default!;
            return false;
        }

        var sequence = dialogue.StepSequences[^1];
        step = sequence.Steps[sequence.Index];
        return true;
    }

    private void NormalizeSequenceStack(ActiveDialogueSession dialogue)
    {
        while (dialogue.StepSequences.Count > 0)
        {
            var sequence = dialogue.StepSequences[^1];

            if (sequence.Index < sequence.Steps.Count)
                return;

            dialogue.StepSequences.RemoveAt(dialogue.StepSequences.Count - 1);

            if (!string.IsNullOrWhiteSpace(sequence.ReturnStepId))
                TryJumpToRootStep(dialogue, sequence.ReturnStepId);
        }
    }

    private bool TryJumpToRootStep(ActiveDialogueSession dialogue, string? stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            return false;

        if (!dialogue.RootStepIndices.TryGetValue(stepId, out var index))
        {
            Log.Warning(
                $"Failed to jump dialogue '{dialogue.DialogueId}' to missing root step id '{stepId}'.");
            return false;
        }

        dialogue.RootSequence.Index = index;
        dialogue.StepSequences.Clear();
        dialogue.StepSequences.Add(dialogue.RootSequence);
        return true;
    }

    private void ValidateLoadedDialoguePrototypes()
    {
        foreach (var prototype in _prototypeManager.EnumeratePrototypes<DialoguePrototype>())
        {
            if (prototype.Steps.Count == 0)
            {
                Log.Warning($"Dialogue '{prototype.ID}' has no configured steps.");
                continue;
            }

            TryValidateDialoguePrototype(prototype, out _);
        }
    }

    private bool TryValidateDialoguePrototype(
        DialoguePrototype prototype,
        out Dictionary<string, int> rootStepIndices)
    {
        var result = DialoguePrototypeValidator.Validate(prototype, _prototypeManager);
        rootStepIndices = new Dictionary<string, int>(result.RootStepIndices, StringComparer.Ordinal);

        foreach (var diagnostic in result.Diagnostics)
        {
            Log.Warning(
                $"Failed to start dialogue '{prototype.ID}': [{diagnostic.Code}] {diagnostic.Path}: {diagnostic.Message}");
        }

        return result.IsValid;
    }

    private sealed class ActiveDialogueSession
    {
        public ICommonSession Session { get; set; }
        public NetUserId UserId { get; }
        public int SessionId { get; set; }
        public EntityUid Initiator { get; }
        public EntityUid Target { get; }
        public ProtoId<DialoguePrototype> DialogueId { get; set; }
        public DialoguePrototype Prototype { get; set; }
        public IReadOnlyDictionary<string, int> RootStepIndices { get; set; }
        public float MaxDialogueRange { get; }
        public bool RequireLineOfSight { get; }
        public TimeSpan ResumeGracePeriod { get; }
        public bool AllowCancel { get; set; }
        public DialogueResumeMode ResumeMode { get; set; }
        public DialogueInteractionMode InteractionMode { get; set; }
        public ActiveDialogueSequence RootSequence { get; set; }
        public List<ActiveDialogueSequence> StepSequences { get; } = new();
        public HashSet<EntityUid> ProtectedEntities { get; } = new();
        public List<ControlledDialogueMovement> ControlledMovers { get; } = new();
        public TimeSpan AutoAdvanceNotBefore { get; set; }
        public bool Closing { get; set; }
        public bool AbortRequested { get; set; }
        public bool Completing { get; set; }
        public ActiveDialogueSession(
            ICommonSession session,
            int sessionId,
            EntityUid initiator,
            EntityUid target,
            ProtoId<DialoguePrototype> dialogueId,
            DialoguePrototype prototype,
            IReadOnlyDictionary<string, int> rootStepIndices,
            float maxDialogueRange,
            bool requireLineOfSight,
            TimeSpan resumeGracePeriod,
            bool allowCancel,
            DialogueResumeMode resumeMode,
            DialogueInteractionMode interactionMode)
        {
            Session = session;
            UserId = session.UserId;
            SessionId = sessionId;
            Initiator = initiator;
            Target = target;
            DialogueId = dialogueId;
            Prototype = prototype;
            RootStepIndices = rootStepIndices;
            MaxDialogueRange = maxDialogueRange;
            RequireLineOfSight = requireLineOfSight;
            ResumeGracePeriod = resumeGracePeriod;
            AllowCancel = allowCancel;
            ResumeMode = resumeMode;
            InteractionMode = interactionMode;
            RootSequence = new ActiveDialogueSequence(prototype.Steps);
            StepSequences.Add(RootSequence);
        }
    }

    private sealed record SuspendedDialogueSession(ActiveDialogueSession Dialogue, TimeSpan ExpiresAt);

    private sealed class ActiveDialogueSequence
    {
        public IReadOnlyList<DialogueStep> Steps { get; }
        public string? ReturnStepId { get; }
        public ProtoId<DialoguePrototype>? NextDialogue { get; }
        public int Index { get; set; }

        public ActiveDialogueSequence(
            IReadOnlyList<DialogueStep> steps,
            string? returnStepId = null,
            ProtoId<DialoguePrototype>? nextDialogue = null)
        {
            Steps = steps;
            ReturnStepId = returnStepId;
            NextDialogue = nextDialogue;
        }
    }

    private sealed class DialogueEntityLease
    {
        public int Owners;
        public bool HadGodmode { get; }
        public bool HadInputLock { get; }
        public bool AppliedInputLock;
        public bool WakeNpcOnRelease;

        public DialogueEntityLease(int owners, bool hadGodmode, bool hadInputLock)
        {
            Owners = owners;
            HadGodmode = hadGodmode;
            HadInputLock = hadInputLock;
        }
    }

    private sealed record ControlledDialogueMovement(EntityUid Entity, bool HadMarker);

    private readonly record struct PersistentDialogueMemoryKey(NetUserId UserId, string MemoryKey);

    private readonly record struct ResolvedDialogueInteraction(
        ProtoId<DialoguePrototype>? DialogueId,
        LocId? Chat,
        DialogueSpeaker Speaker,
        IReadOnlyList<DialogueActionPrototype> Actions,
        string? CooldownKey,
        float Cooldown);
}
