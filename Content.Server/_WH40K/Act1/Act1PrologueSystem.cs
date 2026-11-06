using System.Numerics;
using System.Linq;
using Content.Server._WH40K.CharacterCreation;
using Content.Server._WH40K.Dialogue;
using Content.Server._NF.CryoSleep;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server.Mind;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared._WH40K.Act1;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.Dialogue;
using Content.Shared.GameTicking;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Light.Components;
using Content.Shared.Parallax;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.SSDIndicator;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Act1;

/// <summary>
/// Owns the shared Act I map, remote player corridors and narrative state. The client only receives presentation
/// cues; every progression gate remains server authoritative.
/// </summary>
public sealed class Act1PrologueSystem : EntitySystem
{
    public const string CorridorPath = "/Maps/_WH40K/Act1/act1_prologue.yml";

    private const string EchoPrototype = "WH40KAct1Echo";
    private const string VisionPrototype = "WH40KAct1Vision";
    private const string VisionManifestationPrototype = "WH40KAct1VisionManifestation";
    private const string EchoDialogue = "WH40KAct1EchoDialogue";
    private const string VisionDialogue = "WH40KAct1VisionDialogue";
    private const string FractureDialogue = "WH40KAct1FractureDialogue";
    private const string ActParallax = "WH40KAct1White";
    private const string WandererGear = "WandererGear";
    private const string RogueTraderJob = "RogueTrader";

    [Dependency] private SharedAct1PrologueControlLockSystem _controlLock = default!;
    [Dependency] private DialogueSystem _dialogue = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PlayTimeTrackingSystem _playTime = default!;
    [Dependency] private SharedPointLightSystem _pointLights = default!;
    [Dependency] private Wh40kPlayerProgressManager _progress = default!;
    [Dependency] private SharedRoleSystem _roles = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<NetUserId, TimeSpan> _pendingIntroPresentations = new();
    private EntityUid _actMapUid = EntityUid.Invalid;
    private MapId _actMapId = MapId.Nullspace;
    private int _nextCorridorSlot;

    [Dependency] private MapSystem _maps = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnPlayerBeforeSpawn);
        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnRulePlayerSpawning);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<Act1PrologueInstanceComponent, ComponentShutdown>(OnInstanceShutdown);
        SubscribeLocalEvent<Act1EchoComponent, DialogueLineStartedEvent>(OnEchoDialogueLineStarted);
        SubscribeLocalEvent<Act1EchoComponent, DialogueCompletedEvent>(OnEchoDialogueCompleted);
    }

    public override void Shutdown()
    {
        DeleteSharedActMap();
        _pendingIntroPresentations.Clear();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        SendPendingIntroPresentations();

        var query = EntityQueryEnumerator<Act1PrologueComponent, Act1PrologueInstanceComponent>();
        while (query.MoveNext(out var mob, out var prologue, out var runtime))
        {
            if (HasCompletedAct1(runtime.UserId) && prologue.Phase != Act1ProloguePhase.Transition)
            {
                if (_players.TryGetSessionById(runtime.UserId, out var completedSession))
                {
                    if (completedSession.AttachedEntity == mob)
                    {
                        FinishInstance(completedSession, mob, runtime, sendFadeFromBlack: true);
                        continue;
                    }

                    RaiseNetworkEvent(new Act1ProloguePresentationEvent(Act1ProloguePresentationCue.FadeFromBlack), completedSession.Channel);
                }

                RemoveCompletedInstance(mob, runtime);
                continue;
            }

            if (!_players.TryGetSessionById(runtime.UserId, out var session) ||
                session.AttachedEntity != mob)
            {
                continue;
            }

            switch (prologue.Phase)
            {
                case Act1ProloguePhase.Intro when runtime.IntroPresentationStarted && _timing.CurTime >= runtime.PhaseEndsAt:
                    SetPhase(mob, prologue, Act1ProloguePhase.Exploration);
                    break;

                case Act1ProloguePhase.Exploration:
                    TryStartEchoDialogue(session, mob, prologue, runtime);
                    break;

                case Act1ProloguePhase.Dialogue when !_dialogue.IsInDialogue(session):
                    SetPhase(mob, prologue, Act1ProloguePhase.Exploration);
                    TryStartEchoDialogue(session, mob, prologue, runtime);
                    break;

                case Act1ProloguePhase.Vision:
                    UpdateVisionSequence(session, mob, prologue, runtime);
                    break;

                case Act1ProloguePhase.Fracture:
                    UpdateFractureSequence(session, mob, prologue, runtime);
                    break;

                case Act1ProloguePhase.Transition:
                    TryFinishTransition(session, mob, runtime);
                    break;
            }
        }
    }

    /// <summary>
    /// Starts the ordinary lobby-to-Act-I route. It is deliberately separate from the normal job picker because
    /// the prologue supplies its own role and private map instance.
    /// </summary>
    public bool TryJoinFromLobby(ICommonSession session, out string message)
    {
        if (HasCompletedAct1(session.UserId))
        {
            message = "Акт I уже пройден. Повторный запуск запрещён.";
            CancelClientTransition(session);
            return false;
        }

        if (_ticker.UserHasJoinedGame(session))
        {
            message = "Нельзя начать Акт I для уже активного персонажа.";
            CancelClientTransition(session);
            return false;
        }

        if (_ticker.RunLevel != GameRunLevel.InRound)
        {
            message = "Акт I можно начать только в идущем раунде.";
            CancelClientTransition(session);
            return false;
        }

        if (!ShouldStartNormally(session.UserId))
        {
            message = "Для этого аккаунта Акт I сейчас недоступен.";
            CancelClientTransition(session);
            return false;
        }

        _ticker.MakeJoinGame(session, EntityUid.Invalid, silent: true);
        message = "Запуск Акта I подтверждён.";
        return true;
    }

    private void OnPlayerBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (HasCompletedAct1(ev.Player.UserId))
        {
            CancelClientTransition(ev.Player);
            return;
        }

        var shouldStartNormally = ShouldStartNormally(ev.Player.UserId);
        if (!shouldStartNormally)
            return;

        if (!TryStartPrologue(ev.Player, ev.Profile, joinGame: false, out var reason))
        {
            Log.Error($"Could not start Act I for {ev.Player.UserId}: {reason}");
            return;
        }

        ev.Handled = true;
    }

    private void OnRulePlayerSpawning(RulePlayerSpawningEvent ev)
    {
        foreach (var session in ev.PlayerPool.ToArray())
        {
            if (HasCompletedAct1(session.UserId))
            {
                CancelClientTransition(session);
                continue;
            }

            var shouldStartNormally = ShouldStartNormally(session.UserId);
            if (!shouldStartNormally)
                continue;

            if (!ev.Profiles.TryGetValue(session.UserId, out var profile))
            {
                Log.Error($"Could not start Act I for {session.UserId}: the ready-player profile is missing.");
                continue;
            }

            if (!TryStartPrologue(session, profile, joinGame: true, out var reason))
            {
                Log.Error($"Could not start Act I for {session.UserId}: {reason}");
                continue;
            }

            ev.PlayerPool.Remove(session);
        }
    }

    private bool TryStartPrologue(
        ICommonSession session,
        HumanoidCharacterProfile profile,
        bool joinGame,
        out string reason)
    {
        if (HasCompletedAct1(session.UserId) || !ShouldStartNormally(session.UserId))
        {
            reason = "Акт I уже завершён или недоступен этому аккаунту.";
            CancelClientTransition(session);
            return false;
        }

        if (!TryFindFootfallArrival(out _))
        {
            reason = "На Footfall не найдена точка возвращения из Акта I.";
            CancelClientTransition(session);
            return false;
        }

        if (!TryCreateInstance(session, out var instance, out reason))
        {
            CancelClientTransition(session);
            return false;
        }

        // This is raised before the player body is created, while the client is still in the lobby.
        // The client keeps the completed black fade until the white-room entity is replicated.
        RaiseNetworkEvent(new Act1ProloguePresentationEvent(Act1ProloguePresentationCue.FadeToBlack), session.Channel);

        var mob = _stationSpawning.SpawnPlayerMob(
            instance.PlayerSpawn,
            RogueTraderJob,
            profile,
            station: null,
            session: session,
            loadoutMode: PlayerSpawnLoadoutMode.PersistentRestore);

        if (joinGame)
            _ticker.PlayerJoinGame(session, silent: true);

        var mind = _mind.GetMind(session.UserId);
        if (mind == null)
        {
            mind = _mind.CreateMind(session.UserId, profile.Name);
            _mind.SetUserId(mind.Value, session.UserId);
            _playTime.PlayerRolesChanged(session);
        }

        var postPrologueJob = ResolvePostPrologueJob(profile);
        _mind.TransferTo(mind.Value, mob);
        _roles.MindAddJobRole(mind.Value, silent: true, jobPrototype: RogueTraderJob);
        StartInstance(session, mob, instance, profile, postPrologueJob);
        return true;
    }

    private bool ShouldStartNormally(NetUserId userId)
    {
        var progress = _progress.Get(userId);
        return progress.ActStage == Wh40kActStage.Act1InProgress &&
               progress.OnboardingStatus == Wh40kOnboardingStatus.CharacterCreated;
    }

    private bool HasCompletedAct1(NetUserId userId) =>
        _progress.Get(userId).ActStage == Wh40kActStage.Act1Completed;

    private string ResolvePostPrologueJob(HumanoidCharacterProfile profile)
    {
        foreach (var (job, priority) in profile.JobPriorities.OrderByDescending(pair => pair.Value))
        {
            if (priority == JobPriority.Never ||
                job.Id == RogueTraderJob ||
                !_prototypes.TryIndex(job, out JobPrototype? prototype) ||
                !prototype.SetPreference ||
                prototype.JobEntity != null)
            {
                continue;
            }

            return job.Id;
        }

        return SharedGameTicker.FallbackOverflowJob;
    }

    private void StartInstance(
        ICommonSession session,
        EntityUid mob,
        PrologueCorridor instance,
        HumanoidCharacterProfile profile,
        string postPrologueJob)
    {
        var prologue = EnsureComp<Act1PrologueComponent>(mob);
        prologue.Phase = Act1ProloguePhase.Intro;
        Dirty(mob, prologue);
        EnsureComp<Act1PrologueControlLockComponent>(mob);

        var runtime = EnsureComp<Act1PrologueInstanceComponent>(mob);
        runtime.CorridorGrid = instance.GridUid;
        runtime.CorridorSlot = instance.Slot;
        runtime.EchoAnchor = instance.EchoAnchor;
        runtime.UserId = session.UserId;
        runtime.VisionAnchors.Clear();
        runtime.VisionAnchors.AddRange(instance.VisionAnchors);
        runtime.CurrentVision = EntityUid.Invalid;
        runtime.NextVisionIndex = 0;
        runtime.VisionDialoguePending = false;
        runtime.VisionDialogueCompleted = false;
        runtime.FractureDialogueStarted = false;
        runtime.FractureDialogueCompleted = false;
        runtime.FractureStartedAt = TimeSpan.Zero;
        runtime.FractureMaxReachedAt = TimeSpan.Zero;
        runtime.LastFractureProgress = -1f;
        runtime.PhaseEndsAt = TimeSpan.Zero;
        runtime.IntroPresentationStarted = false;
        runtime.PostPrologueProfile = profile;
        runtime.PostPrologueJob = postPrologueJob;
        runtime.CompletionTask = null;
        runtime.Echo = Spawn(EchoPrototype, instance.EchoAnchor);
        _meta.SetEntityName(runtime.Echo, "Эхо");

        EquipWandererGear(mob);
        ConfigurePrologueLight(mob);
        _pendingIntroPresentations[session.UserId] = _timing.CurTime +
                                                   Act1ProloguePresentationTiming.FadeToBlackDuration +
                                                   IntroPresentationDelay;
    }

    private void EquipWandererGear(EntityUid mob)
    {
        // The body is born for Act I and has not received station or persistent equipment.
        // Do not unequip an existing uniform here: its pockets would be physically dropped into the white room.
        _stationSpawning.EquipStartingGear(mob, WandererGear);
    }

    private void TryStartEchoDialogue(
        ICommonSession session,
        EntityUid mob,
        Act1PrologueComponent prologue,
        Act1PrologueInstanceComponent runtime)
    {
        if (runtime.Echo == EntityUid.Invalid || Deleted(runtime.Echo))
            return;

        if (!_dialogue.TryStartScriptedDialogue(session, runtime.Echo, EchoDialogue))
            return;

        SetPhase(mob, prologue, Act1ProloguePhase.Dialogue);
    }

    private void OnEchoDialogueCompleted(Entity<Act1EchoComponent> echo, ref DialogueCompletedEvent args)
    {
        if (!TryComp<Act1PrologueComponent>(args.Initiator, out var prologue) ||
            !TryComp<Act1PrologueInstanceComponent>(args.Initiator, out var runtime) ||
            runtime.Echo != echo.Owner)
        {
            return;
        }

        if (string.Equals(args.DialogueId, EchoDialogue, StringComparison.Ordinal) &&
            prologue.Phase == Act1ProloguePhase.Dialogue)
        {
            StartVisionSequence(args.Initiator, prologue, runtime);
            return;
        }

        if (string.Equals(args.DialogueId, VisionDialogue, StringComparison.Ordinal) &&
            prologue.Phase == Act1ProloguePhase.Vision)
        {
            runtime.VisionDialogueCompleted = true;
            return;
        }

        if (string.Equals(args.DialogueId, FractureDialogue, StringComparison.Ordinal) &&
            prologue.Phase == Act1ProloguePhase.Fracture)
        {
            runtime.FractureDialogueCompleted = true;
        }
    }

    private void OnEchoDialogueLineStarted(Entity<Act1EchoComponent> echo, ref DialogueLineStartedEvent args)
    {
        if (!TryComp<Act1PrologueComponent>(args.Initiator, out var prologue) ||
            !TryComp<Act1PrologueInstanceComponent>(args.Initiator, out var runtime) ||
            runtime.Echo != echo.Owner ||
            !string.Equals(args.DialogueId, VisionDialogue, StringComparison.Ordinal) ||
            prologue.Phase != Act1ProloguePhase.Vision)
        {
            return;
        }

        // The first image is created before the dialogue opens. Every following line
        // prepares its image here, before DialogueLineUpdateEvent reaches the client.
        while (runtime.NextVisionIndex <= args.StepIndex &&
               runtime.NextVisionIndex < runtime.VisionAnchors.Count)
        {
            SpawnNextVision(args.Initiator, runtime);
        }

        // The last line is narration and has no matching image.
        if (args.StepIndex >= runtime.VisionAnchors.Count)
            ClearCurrentVision(runtime);
    }

    private void StartVisionSequence(
        EntityUid mob,
        Act1PrologueComponent prologue,
        Act1PrologueInstanceComponent runtime)
    {
        _controlLock.SetBlockMovement(mob, true);
        runtime.NextVisionIndex = 0;
        runtime.CurrentVision = EntityUid.Invalid;
        runtime.VisionSequenceDeadline = _timing.CurTime + VisionSequenceFallbackDuration;
        runtime.VisionDialoguePending = true;
        runtime.VisionDialogueCompleted = false;
        SpawnNextVision(mob, runtime);
        SetPhase(mob, prologue, Act1ProloguePhase.Vision);
    }

    private void UpdateVisionSequence(
        ICommonSession session,
        EntityUid mob,
        Act1PrologueComponent prologue,
        Act1PrologueInstanceComponent runtime)
    {
        // A component created before this field existed can still safely resume.
        if (runtime.VisionSequenceDeadline == TimeSpan.Zero)
            runtime.VisionSequenceDeadline = _timing.CurTime + VisionSequenceFallbackDuration;

        if (!runtime.VisionDialogueCompleted && !_dialogue.IsInDialogue(session))
            runtime.VisionDialoguePending = true;

        if (runtime.VisionDialoguePending && TryStartVisionDialogue(session, runtime))
            runtime.VisionDialoguePending = false;

        if (runtime.NextVisionIndex >= runtime.VisionAnchors.Count &&
            !runtime.VisionDialoguePending &&
            runtime.VisionDialogueCompleted)
        {
            BeginFractureSequence(session, mob, prologue, runtime);
            return;
        }

        if (runtime.NextVisionIndex >= runtime.VisionAnchors.Count &&
            _timing.CurTime >= runtime.VisionSequenceDeadline)
        {
            Log.Warning($"Act I vision dialogue did not complete for {runtime.UserId}; continuing the cinematic from its server fallback.");
            runtime.VisionDialoguePending = false;
            runtime.VisionDialogueCompleted = true;
            BeginFractureSequence(session, mob, prologue, runtime);
        }
    }

    private void BeginFractureSequence(
        ICommonSession session,
        EntityUid mob,
        Act1PrologueComponent prologue,
        Act1PrologueInstanceComponent runtime)
    {
        ClearCurrentVision(runtime);
        runtime.FractureDialogueStarted = false;
        runtime.FractureDialogueCompleted = false;
        runtime.FractureStartedAt = TimeSpan.Zero;
        runtime.FractureMaxReachedAt = TimeSpan.Zero;
        runtime.LastFractureProgress = -1f;
        SetPhase(mob, prologue, Act1ProloguePhase.Fracture);
        SendFractureProgress(session, runtime, 0f, blackout: false, force: true);
    }

    private void UpdateFractureSequence(
        ICommonSession session,
        EntityUid mob,
        Act1PrologueComponent prologue,
        Act1PrologueInstanceComponent runtime)
    {
        if (!runtime.FractureDialogueCompleted && !_dialogue.IsInDialogue(session))
            runtime.FractureDialogueStarted = false;

        if (!runtime.FractureDialogueStarted)
        {
            if (runtime.Echo == EntityUid.Invalid || Deleted(runtime.Echo))
            {
                runtime.FractureDialogueStarted = true;
                runtime.FractureDialogueCompleted = true;
                runtime.FractureStartedAt = _timing.CurTime;
                return;
            }

            if (!_dialogue.TryStartScriptedDialogue(session, runtime.Echo, FractureDialogue))
                return;

            runtime.FractureDialogueStarted = true;
            runtime.FractureStartedAt = _timing.CurTime;
            return;
        }

        var elapsed = _timing.CurTime - runtime.FractureStartedAt;
        var degree = Act1ProloguePresentationTiming.GetFractureProgress(elapsed);
        SendFractureProgress(session, runtime, degree, blackout: false);

        if (elapsed < Act1ProloguePresentationTiming.FractureBuildDuration ||
            !runtime.FractureDialogueCompleted)
        {
            return;
        }

        if (runtime.FractureMaxReachedAt == TimeSpan.Zero)
        {
            runtime.FractureMaxReachedAt = _timing.CurTime;
            SendFractureProgress(session, runtime, 1f, blackout: false, force: true);
            return;
        }

        if (_timing.CurTime >= runtime.FractureMaxReachedAt + Act1ProloguePresentationTiming.FractureMaxHoldDuration)
            BeginTransition(session, mob, prologue, runtime);
    }

    private void SendFractureProgress(
        ICommonSession session,
        Act1PrologueInstanceComponent runtime,
        float degree,
        bool blackout,
        bool force = false)
    {
        degree = Math.Clamp(degree, 0f, 1f);
        if (!force && MathF.Abs(runtime.LastFractureProgress - degree) < 0.015f)
            return;

        runtime.LastFractureProgress = degree;
        RaiseNetworkEvent(new Act1PrologueFractureEvent(degree, blackout), session.Channel);
    }

    private bool TryStartVisionDialogue(ICommonSession session, Act1PrologueInstanceComponent runtime)
    {
        if (runtime.Echo == EntityUid.Invalid || Deleted(runtime.Echo))
        {
            runtime.VisionDialogueCompleted = true;
            return true;
        }

        return _dialogue.TryStartScriptedDialogue(session, runtime.Echo, VisionDialogue);
    }

    private void SpawnNextVision(EntityUid mob, Act1PrologueInstanceComponent runtime)
    {
        if (runtime.CurrentVision != EntityUid.Invalid && Exists(runtime.CurrentVision))
            QueueDel(runtime.CurrentVision);

        if (runtime.NextVisionIndex >= runtime.VisionAnchors.Count)
            return;

        var index = runtime.NextVisionIndex++;
        var coordinates = runtime.VisionAnchors[index];
        var vision = Spawn(VisionPrototype, coordinates);
        // A vision has no attached player mind. Do not show the inherited SSD "Zzz" icon above it.
        RemComp<SSDIndicatorComponent>(vision);
        _humanoid.CloneAppearance(mob, vision);
        EquipVision(vision, coordinates, VisionOutfits[index % VisionOutfits.Length]);
        _meta.SetEntityName(vision, VisionNames[index % VisionNames.Length]);
        Spawn(VisionManifestationPrototype, coordinates);
        runtime.CurrentVision = vision;
    }

    private void TryFinishTransition(
        ICommonSession session,
        EntityUid mob,
        Act1PrologueInstanceComponent runtime)
    {
        if (_timing.CurTime < runtime.PhaseEndsAt || runtime.CompletionTask is { IsCompleted: false })
            return;

        if (runtime.CompletionTask is { IsFaulted: true } or { IsCanceled: true })
        {
            Log.Error($"Act I completion database operation failed for {runtime.UserId}; returning the player without advancing progress.");
        }
        FinishInstance(session, mob, runtime, sendFadeFromBlack: true);
    }

    private void BeginTransition(
        ICommonSession session,
        EntityUid mob,
        Act1PrologueComponent prologue,
        Act1PrologueInstanceComponent runtime)
    {
        SetPhase(mob, prologue, Act1ProloguePhase.Transition);
        runtime.PhaseEndsAt = _timing.CurTime + Act1ProloguePresentationTiming.ShatterToBlackDuration;
        runtime.CompletionTask = _progress.CompleteAct1Async(runtime.UserId);
        RaiseNetworkEvent(new Act1ProloguePresentationEvent(Act1ProloguePresentationCue.ShatterToBlack), session.Channel);
    }

    private bool FinishInstance(
        ICommonSession session,
        EntityUid mob,
        Act1PrologueInstanceComponent instance,
        bool sendFadeFromBlack)
    {
        if (!TryFindFootfallArrival(out var destination))
            return false;

        ClearCurrentVision(instance);
        if (HasComp<Act1ProloguePersonalLightComponent>(mob))
        {
            _pointLights.RemoveLightDeferred(mob);
            RemComp<Act1ProloguePersonalLightComponent>(mob);
        }

        ClearActInventory(mob);
        _transform.SetCoordinates(mob, Transform(mob), destination);
        if (instance.PostPrologueProfile is { } profile)
        {
            _stationSpawning.SpawnPlayerMob(
                destination,
                instance.PostPrologueJob,
                profile,
                station: null,
                entity: mob,
                session: session);

            EnsureComp<PlayerJobComponent>(mob).JobPrototype = instance.PostPrologueJob;
            if (_mind.GetMind(session.UserId) is { } mind)
                _roles.MindAddJobRole(mind, silent: true, jobPrototype: instance.PostPrologueJob);
        }
        else
        {
            Log.Error($"Act I profile is missing for {session.UserId}; equipping fallback gear.");
            EquipWandererGear(mob);
        }

        // Reveal the destination only after the existing body has been moved and
        // its final role equipment/profile have been applied successfully. If a
        // spawn step fails, the client must remain covered while the transition
        // can be retried instead of exposing a half-finished character.
        if (sendFadeFromBlack)
            RaiseNetworkEvent(new Act1ProloguePresentationEvent(Act1ProloguePresentationCue.FadeFromBlack), session.Channel);

        RemComp<Act1PrologueControlLockComponent>(mob);
        RemComp<Act1PrologueComponent>(mob);
        RemComp<Act1PrologueInstanceComponent>(mob);
        return true;
    }

    private bool TryCreateInstance(ICommonSession session, out PrologueCorridor instance, out string reason)
    {
        instance = default;
        reason = string.Empty;

        if (!EnsureSharedActMap(out var mapId))
        {
            reason = "Не удалось создать общую карту Акта I.";
            return false;
        }

        var slot = _nextCorridorSlot++;
        var options = DeserializationOptions.Default with { InitializeMaps = true };
        if (!_mapLoader.TryLoadGrid(mapId, new ResPath(CorridorPath), out var grid, options, GetCorridorOffset(slot)))
        {
            reason = $"Не удалось загрузить коридор Акта I по пути {CorridorPath}.";
            return false;
        }

        RemComp<ImplicitRoofComponent>(grid.Value.Owner);
        _meta.SetEntityName(grid.Value.Owner, $"Act I corridor {slot}");
        if (!TryResolveInstanceMarkers(grid.Value.Owner, out var playerSpawn, out var echoAnchor, out var visionAnchors, out reason))
        {
            QueueDel(grid.Value.Owner);
            return false;
        }

        instance = new PrologueCorridor(grid.Value.Owner, slot, playerSpawn, echoAnchor, visionAnchors);
        return true;
    }

    private bool TryResolveInstanceMarkers(
        EntityUid gridUid,
        out EntityCoordinates playerSpawn,
        out EntityCoordinates echoAnchor,
        out IReadOnlyList<EntityCoordinates> visionAnchors,
        out string reason)
    {
        playerSpawn = EntityCoordinates.Invalid;
        echoAnchor = EntityCoordinates.Invalid;
        visionAnchors = Array.Empty<EntityCoordinates>();
        reason = string.Empty;
        var markers = new Dictionary<Act1PrologueMarkerRole, EntityCoordinates>();
        var markerEntities = new List<EntityUid>();

        var query = EntityQueryEnumerator<Act1PrologueMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var markerUid, out var marker, out var transform))
        {
            if (transform.GridUid != gridUid)
                continue;

            if (!markers.TryAdd(marker.Role, transform.Coordinates))
            {
                reason = $"На карте Акта I повторяется метка {marker.Role}.";
                return false;
            }

            markerEntities.Add(markerUid);
        }

        foreach (var role in RequiredInstanceMarkers)
        {
            if (!markers.ContainsKey(role))
            {
                reason = $"На карте Акта I нет обязательной метки {role}.";
                return false;
            }
        }

        playerSpawn = markers[Act1PrologueMarkerRole.PlayerSpawn];
        echoAnchor = markers[Act1PrologueMarkerRole.EchoAnchor];
        visionAnchors = VisionMarkerRoles.Select(role => markers[role]).ToArray();
        foreach (var markerUid in markerEntities)
            QueueDel(markerUid);

        return true;
    }

    private void ConfigurePrologueLight(EntityUid mob)
    {
        var light = _pointLights.EnsureLight(mob);
        _pointLights.SetEnabled(mob, true, light);
        _pointLights.SetColor(mob, Color.FromHex("#FFF4E8"), light);
        _pointLights.SetRadius(mob, 3.6f, light);
        _pointLights.SetEnergy(mob, 0.9f, light);
        _pointLights.SetSoftness(mob, 0.9f, light);
        _pointLights.SetCastShadows(mob, false, light);
        EnsureComp<Act1ProloguePersonalLightComponent>(mob);
    }

    private void ClearCurrentVision(Act1PrologueInstanceComponent runtime)
    {
        if (runtime.CurrentVision != EntityUid.Invalid && Exists(runtime.CurrentVision))
            QueueDel(runtime.CurrentVision);

        runtime.CurrentVision = EntityUid.Invalid;
    }

    private void RemoveCompletedInstance(EntityUid mob, Act1PrologueInstanceComponent runtime)
    {
        ClearCurrentVision(runtime);

        if (HasComp<Act1ProloguePersonalLightComponent>(mob))
        {
            _pointLights.RemoveLightDeferred(mob);
            RemComp<Act1ProloguePersonalLightComponent>(mob);
        }

        RemComp<Act1PrologueControlLockComponent>(mob);
        RemComp<Act1PrologueComponent>(mob);
        RemComp<Act1PrologueInstanceComponent>(mob);
    }

    private void EquipVision(EntityUid vision, EntityCoordinates coordinates, VisionOutfit outfit)
    {
        EquipVisionItem(vision, coordinates, outfit.Jumpsuit, "jumpsuit");
        EquipVisionItem(vision, coordinates, outfit.Shoes, "shoes");
        EquipVisionItem(vision, coordinates, outfit.Head, "head");
        EquipVisionItem(vision, coordinates, outfit.Neck, "neck");
        EquipVisionItem(vision, coordinates, outfit.OuterClothing, "outerClothing");
        EquipVisionItem(vision, coordinates, outfit.Gloves, "gloves");
        EquipVisionItem(vision, coordinates, outfit.Belt, "belt");
        EquipVisionItem(vision, coordinates, outfit.Back, "back");
    }

    private void EquipVisionItem(EntityUid vision, EntityCoordinates coordinates, string? prototype, string slot)
    {
        if (prototype == null)
            return;

        var item = Spawn(prototype, coordinates);
        if (!_inventory.TryEquip(vision, item, slot, silent: true, force: true))
            QueueDel(item);
    }

    private bool TryFindFootfallArrival(out EntityCoordinates arrival)
    {
        var query = EntityQueryEnumerator<Act1PrologueMarkerComponent, TransformComponent>();
        while (query.MoveNext(out _, out var marker, out var transform))
        {
            if (marker.Role != Act1PrologueMarkerRole.FootfallArrival)
                continue;

            arrival = transform.Coordinates;
            return true;
        }

        var fallbackQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (fallbackQuery.MoveNext(out _, out var spawnPoint, out var transform))
        {
            if (spawnPoint.SpawnType != SpawnPointType.LateJoin || transform.MapID == _actMapId)
                continue;

            Log.Warning("На Footfall нет MarkerWH40KAct1FootfallArrival. Используется точка late join как место возврата из Акта I.");
            arrival = transform.Coordinates;
            return true;
        }

        arrival = EntityCoordinates.Invalid;
        return false;
    }

    private void CancelClientTransition(ICommonSession session)
    {
        RaiseNetworkEvent(new Act1ProloguePresentationEvent(Act1ProloguePresentationCue.CancelTransition), session.Channel);
    }

    private void OnInstanceShutdown(Entity<Act1PrologueInstanceComponent> ent, ref ComponentShutdown args)
    {
        _pendingIntroPresentations.Remove(ent.Comp.UserId);
        if (ent.Comp.CorridorGrid != EntityUid.Invalid && Exists(ent.Comp.CorridorGrid))
            QueueDel(ent.Comp.CorridorGrid);
    }

    private void SendPendingIntroPresentations()
    {
        foreach (var (userId, readyAt) in _pendingIntroPresentations.ToArray())
        {
            if (_timing.CurTime < readyAt ||
                !_players.TryGetSessionById(userId, out var session) ||
                !_ticker.UserHasJoinedGame(session) ||
                session.AttachedEntity is not { Valid: true } mob ||
                !TryComp<Act1PrologueComponent>(mob, out var prologue) ||
                !TryComp<Act1PrologueInstanceComponent>(mob, out var runtime) ||
                prologue.Phase != Act1ProloguePhase.Intro)
            {
                continue;
            }

            runtime.IntroPresentationStarted = true;
            runtime.PhaseEndsAt = _timing.CurTime + Act1ProloguePresentationTiming.IntroTotalDuration;
            RaiseNetworkEvent(new Act1ProloguePresentationEvent(Act1ProloguePresentationCue.Intro), session.Channel);
            _pendingIntroPresentations.Remove(userId);
        }
    }

    private void ClearActInventory(EntityUid mob)
    {
        var items = new HashSet<EntityUid>();
        if (_inventory.TryGetSlots(mob, out var slots))
        {
            foreach (var slot in slots)
            {
                if (_inventory.TryGetSlotEntity(mob, slot.Name, out var item))
                    items.Add(item.Value);
            }
        }

        foreach (var item in _hands.EnumerateHeld(mob))
            items.Add(item);

        foreach (var item in items)
            Del(item);
    }

    private bool EnsureSharedActMap(out MapId mapId)
    {
        if (_actMapUid != EntityUid.Invalid && Exists(_actMapUid) &&
            TryComp<MapComponent>(_actMapUid, out var existingMap))
        {
            mapId = existingMap.MapId;
            return true;
        }

        _actMapUid = _maps.CreateMap(out _actMapId, runMapInit: true);
        _meta.SetEntityName(_actMapUid, "Act I shared space");

        var parallax = EnsureComp<ParallaxComponent>(_actMapUid);
        parallax.Parallax = ActParallax;
        Dirty(_actMapUid, parallax);

        // Keep the scene itself uniformly pale; actors receive a private no-shadow light in StartInstance.
        _maps.SetAmbientLight(_actMapId, Color.FromSrgb(Color.White));

        var gravity = EnsureComp<GravityComponent>(_actMapUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(_actMapUid, gravity);
        mapId = _actMapId;
        return true;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        DeleteSharedActMap();
        _pendingIntroPresentations.Clear();
        _nextCorridorSlot = 0;
    }

    private void DeleteSharedActMap()
    {
        if (_actMapUid != EntityUid.Invalid && Exists(_actMapUid))
            QueueDel(_actMapUid);

        _actMapUid = EntityUid.Invalid;
        _actMapId = MapId.Nullspace;
    }

    private static Vector2 GetCorridorOffset(int slot)
    {
        const float spacing = 1024f;
        const int columns = 64;
        return new Vector2(slot % columns * spacing, slot / columns * spacing);
    }

    private void SetPhase(EntityUid mob, Act1PrologueComponent prologue, Act1ProloguePhase phase)
    {
        prologue.Phase = phase;
        Dirty(mob, prologue);
    }

    private readonly record struct PrologueCorridor(
        EntityUid GridUid,
        int Slot,
        EntityCoordinates PlayerSpawn,
        EntityCoordinates EchoAnchor,
        IReadOnlyList<EntityCoordinates> VisionAnchors);

    private readonly record struct VisionOutfit(
        string? Jumpsuit,
        string? Shoes = null,
        string? Head = null,
        string? Neck = null,
        string? OuterClothing = null,
        string? Gloves = null,
        string? Belt = null,
        string? Back = null);

    private static readonly TimeSpan VisionSequenceFallbackDuration = TimeSpan.FromSeconds(36);
    private static readonly TimeSpan IntroPresentationDelay = TimeSpan.FromMilliseconds(150);

    private static readonly string[] VisionNames =
    {
        "Наследник варранта",
        "Безымянный выживший",
        "Торговец-должник",
        "Подданный десятины",
        "Пустотник",
        "Гвардеец",
        "Адепт Кузни",
        "Орудие Инквизиции",
    };

    private static readonly VisionOutfit[] VisionOutfits =
    {
        new("ClothingUniformJumpsuitCommandUniform",
            Shoes: "ClothingShoesCommissarBoots",
            Head: "ClothingHeadOfficer",
            Neck: "ClothingNeckImperialAquilaMedal",
            OuterClothing: "ClothingOuterArmorOfficer",
            Gloves: "ClothingHandsGlovesCombatColonel",
            Belt: "offbelt",
            Back: "ClothingBackpackGuard"),
        new("ClothingUniformJumpsuitWanderer",
            Shoes: "ClothingShoesBootsWanderer",
            Head: "ClothingHeadHatImperialCitizen",
            OuterClothing: "ClothingOuterCloakWanderer",
            Gloves: "ClothingHandsGlovesCombatVoidsman",
            Back: "ClothingBackpackWanderer"),
        new("ClothingUniformJumpsuitSmug",
            Shoes: "ClothingShoesAnkleBoots",
            Head: "ClothingHeadHatImperialCitizen",
            OuterClothing: "ClothingOuterArmorsmugCoat",
            Gloves: "ClothingHandsGlovesCombatVoidsman",
            Belt: "offbelt",
            Back: "ClothingBackpackSatchelWanderer"),
        new("ClothingUniformJumpsuitImperialCitizen",
            Shoes: "ClothingShoesImperialPilgrim",
            Neck: "ClothingNeckImperialAquilaMedal",
            OuterClothing: "ClothingOuterRobeImperialPilgrim",
            Gloves: "ClothingHandsGlovesCombatVoidsman",
            Belt: "guardbelt"),
        new("ClothingUniformJumpsuitVoidsman",
            Shoes: "ClothingShoesAnkleBoots",
            Head: "ClothingHeadHelmetVoidsmanEnlistedA",
            OuterClothing: "ClothingOuterHardsuitVoidsmanEnlistedA",
            Gloves: "ClothingHandsGlovesCombatVoidsman",
            Belt: "ClothingBeltVoidsman",
            Back: "ClothingBackpackGuard"),
        new("ClothingUniformJumpsuitGuardsman",
            Shoes: "ClothingShoesGenBoots",
            Head: "ClothingHeadSgt",
            OuterClothing: "ClothingOuterArmorFlakVest",
            Gloves: "ClothingHandsGlovesCombatColonel",
            Belt: "guardbelt",
            Back: "ClothingBackpackGuard"),
        new("ClothingUniformJumpsuitTechpriest",
            Shoes: "ClothingShoesArmoredWorkbootsTechpriest",
            Head: "ClothingHeadHelmetHardsuitTechpriest",
            Neck: "ClothingNeckRobeTechpriestMars",
            OuterClothing: "ClothingOuterHardsuitTechpriest",
            Gloves: "ClothingHandsGlovesTechpriest",
            Back: "ClothingBackpackTechpriest"),
        new("ClothingUniformJumpsuitInquisition",
            Shoes: "ClothingShoesAnkleBootsInquisition",
            Head: "ClothingHeadVetInquisition",
            Neck: "ClothingNeckInqusition",
            OuterClothing: "ClothingOuterArmorFlakInquisition",
            Gloves: "ClothingHandsGlovesInquisition",
            Belt: "guardbelt",
            Back: "ClothingBackpackGuard"),
    };

    private static readonly Act1PrologueMarkerRole[] RequiredInstanceMarkers =
    {
        Act1PrologueMarkerRole.PlayerSpawn,
        Act1PrologueMarkerRole.EchoAnchor,
        Act1PrologueMarkerRole.VisionOne,
        Act1PrologueMarkerRole.VisionTwo,
        Act1PrologueMarkerRole.VisionThree,
        Act1PrologueMarkerRole.VisionFour,
        Act1PrologueMarkerRole.VisionFive,
        Act1PrologueMarkerRole.VisionSix,
        Act1PrologueMarkerRole.VisionSeven,
        Act1PrologueMarkerRole.VisionEight,
    };

    private static readonly Act1PrologueMarkerRole[] VisionMarkerRoles =
    {
        Act1PrologueMarkerRole.VisionOne,
        Act1PrologueMarkerRole.VisionTwo,
        Act1PrologueMarkerRole.VisionThree,
        Act1PrologueMarkerRole.VisionFour,
        Act1PrologueMarkerRole.VisionFive,
        Act1PrologueMarkerRole.VisionSix,
        Act1PrologueMarkerRole.VisionSeven,
        Act1PrologueMarkerRole.VisionEight,
    };
}
