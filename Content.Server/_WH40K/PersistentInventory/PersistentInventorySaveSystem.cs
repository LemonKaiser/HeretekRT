using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Server._NF.CryoSleep;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Shared.ActionBlocker;
using Content.Shared.CCVar;
using Content.Shared.Cuffs.Components;
using Content.Shared.Database;
using Content.Shared.Emoting;
using Content.Shared.GameTicking;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Speech;
using Content.Shared.Storage;
using Content.Shared.Throwing;
using Prometheus;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.PersistentInventory;

public enum PersistentInventorySaveSource
{
    PhysicalCryo = 0,
    AdminCommand = 1,
    GracefulShutdown = 2,
    RoundRestart = 3,
}

public enum PersistentInventorySaveStatus
{
    Success = 0,
    Disabled = 1,
    Busy = 2,
    Cooldown = 3,
    InvalidTarget = 4,
    PolicyRejected = 5,
    DatabaseFailure = 6,
    PendingRecovery = 7,
    DryRunSuccess = 8,
    RolloutExcluded = 9,
}

public sealed record PersistentInventorySaveRequest(
    NetUserId UserId,
    EntityUid Body,
    ICommonSession Session,
    PersistentInventorySaveSource Source,
    EntityUid? Cryopod,
    string Actor,
    Guid? ActorUserId,
    string Reason,
    bool Force = false);

public sealed record PersistentInventorySaveResult(
    PersistentInventorySaveStatus Status,
    string Message,
    PersistentInventorySnapshotId? SnapshotId = null,
    PersistentInventoryOperationId? OperationId = null)
{
    public bool IsSuccess => Status == PersistentInventorySaveStatus.Success;
}

/// <summary>
/// Unified server-authoritative save pipeline for physical cryo and admin cryosave.
/// All ECS operations run on the main thread, while database phases are linked by optimistic revisions.
/// </summary>
public sealed partial class PersistentInventorySaveSystem : EntitySystem
{
    private static readonly Counter SaveOutcomes = Metrics.CreateCounter(
        "wh40k_persistent_inventory_save_total",
        "Persistent inventory save results.",
        "result",
        "source");

    private static readonly Histogram SaveDuration = Metrics.CreateHistogram(
        "wh40k_persistent_inventory_save_duration_seconds",
        "Persistent inventory save saga duration in seconds.",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.05, 2, 12),
        });

    private static readonly Histogram PayloadSize = Metrics.CreateHistogram(
        "wh40k_persistent_inventory_payload_bytes",
        "Compressed persistent inventory payload size.",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(256, 2, 14),
        });

    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ITaskManager _taskManager = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private PersistentInventoryManager _manager = default!;
    [Dependency] private PersistentInventoryLifecycleSystem _lifecycle = default!;
    [Dependency] private PersistentInventorySnapshotSerializer _serializer = default!;
    [Dependency] private CryoSleepSystem _cryo = default!;

    private readonly ConcurrentDictionary<NetUserId, byte> _accountLocks = new();
    private readonly ConcurrentDictionary<NetUserId, DateTime> _cooldowns = new();
    private int _activeOperations;
    private Task _reconciliation = Task.CompletedTask;
    private bool _reconciliationStarted;
    private DateTime _nextCooldownPrune = DateTime.MinValue;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, PickupAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, DropAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, ThrowAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, UseAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, AttackAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, ChangeDirectionAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, InteractionAttemptEvent>(CancelInteraction);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, PullAttemptEvent>(CancelPull);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, SpeakAttemptEvent>(CancelSpeak);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, EmoteAttemptEvent>(CancelEmote);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, BeingEquippedAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, IsEquippingAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, BeingUnequippedAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, IsUnequippingAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, ContainerIsInsertingAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, ContainerGettingInsertedAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, ContainerIsRemovingAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, ContainerGettingRemovedAttemptEvent>(Cancel);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, StorageInteractAttemptEvent>(CancelStorage);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, StorageInteractUsingAttemptEvent>(CancelStorageUsing);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, ComponentStartup>(OnOperationLockStartup);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, ComponentShutdown>(UpdateCanMove);
        SubscribeLocalEvent<PersistentInventoryOperationComponent, UpdateCanMoveEvent>(CancelMovement);
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = DateTime.UtcNow;
        if (now < _nextCooldownPrune)
            return;

        _nextCooldownPrune = now.AddMinutes(1);
        foreach (var (userId, expiresAt) in _cooldowns)
        {
            if (expiresAt <= now)
            {
                _cooldowns.TryRemove(
                    new KeyValuePair<NetUserId, DateTime>(userId, expiresAt));
            }
        }
    }

    private void OnRoundStarting(RoundStartingEvent args)
    {
        _ = StartReconciliation();
    }

    public Task StartReconciliation()
    {
        if (_reconciliationStarted)
            return _reconciliation;

        _reconciliationStarted = true;
        _reconciliation = ReconcileStagingAsync();
        return _reconciliation;
    }

    public async Task<PersistentInventorySaveResult> TrySaveAsync(
        PersistentInventorySaveRequest request,
        CancellationToken cancel = default)
    {
        var rollout = PersistentInventoryRollout.GetDecision(_configuration, request.UserId);
        if (rollout == PersistentInventoryRolloutDecision.Disabled)
            return Result(PersistentInventorySaveStatus.Disabled, "Persistent inventory выключен.", request);
        if (rollout == PersistentInventoryRolloutDecision.Excluded)
        {
            return Result(
                PersistentInventorySaveStatus.RolloutExcluded,
                "Аккаунт не входит в текущую production-выборку persistent inventory.",
                request);
        }

        if (rollout == PersistentInventoryRolloutDecision.Full)
        {
            try
            {
                await _lifecycle.EnsureReadyAsync().WaitAsync(cancel);
                await StartReconciliation().WaitAsync(cancel);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Error($"Persistent inventory reconciliation failed: {exception}");
                return Result(
                    PersistentInventorySaveStatus.DatabaseFailure,
                    "Стартовая сверка persistent inventory не завершена.",
                    request);
            }
        }

        if (_manager.IsLifeLossPending(request.UserId))
            return Result(PersistentInventorySaveStatus.Busy, "Для аккаунта завершается утрата прошлой жизни.", request);

        if (!_accountLocks.TryAdd(request.UserId, 0))
            return Result(PersistentInventorySaveStatus.Busy, "Для аккаунта уже выполняется сохранение.", request);

        if (!TryEnterGlobalSlot())
        {
            _accountLocks.TryRemove(request.UserId, out _);
            return Result(PersistentInventorySaveStatus.Busy, "Достигнут лимит одновременных сохранений.", request);
        }

        PersistentInventoryMetrics.ActiveSaves.Inc();
        var stopwatch = Stopwatch.StartNew();
        var operationId = PersistentInventoryOperationId.New();
        var snapshotId = PersistentInventorySnapshotId.New();
        var staged = false;
        var cleanupAuthorized = false;
        var worldRetired = false;
        PersistentInventoryCaptureResult? capture = null;
        PersistentInventoryRevision stagedRevision = default;

        try
        {
            if (_cooldowns.TryGetValue(request.UserId, out var cooldownUntil) &&
                cooldownUntil <= DateTime.UtcNow)
            {
                _cooldowns.TryRemove(
                    new KeyValuePair<NetUserId, DateTime>(request.UserId, cooldownUntil));
            }
            else if (!request.Force && cooldownUntil > DateTime.UtcNow)
            {
                return Result(
                    PersistentInventorySaveStatus.Cooldown,
                    $"Повторное сохранение будет доступно через {Math.Ceiling((cooldownUntil - DateTime.UtcNow).TotalSeconds)} с.",
                    request,
                    snapshotId,
                    operationId);
            }

            var phaseStopwatch = Stopwatch.StartNew();
            var preparation = await RunOnMainThread(() => PrepareOnMainThread(request, operationId));
            if (!preparation.Success)
            {
                return Result(
                    preparation.Status,
                    preparation.Error!,
                    request,
                    snapshotId,
                    operationId);
            }

            capture = preparation.Capture!;
            var packed = PersistentInventoryPayloadCodec.Pack(capture.Payload!, _serializer.GetConfiguredLimits());
            PersistentInventoryMetrics.PhaseDuration
                .WithLabels("save", "serialize_validate")
                .Observe(phaseStopwatch.Elapsed.TotalSeconds);
            PayloadSize.Observe(packed.CompressedBytes);

            if (rollout == PersistentInventoryRolloutDecision.DryRun)
            {
                var dryRunIsolated = await RunOnMainThread(() => _serializer.RestoreIsolated(capture.Payload!));
                if (!dryRunIsolated.Success)
                {
                    return Result(
                        PersistentInventorySaveStatus.PolicyRejected,
                        $"Dry-run isolated-проверка не пройдена: {dryRunIsolated.Error}",
                        request,
                        snapshotId,
                        operationId);
                }

                await RunOnMainThread(() => _serializer.DeleteIsolated(dryRunIsolated));
                var dryRunRevalidation = await RunOnMainThread(
                    () => RevalidateOnMainThread(request, operationId, capture, packed));
                if (!dryRunRevalidation.Success)
                {
                    return Result(
                        PersistentInventorySaveStatus.PolicyRejected,
                        $"Dry-run повторная проверка не пройдена: {dryRunRevalidation.Error}",
                        request,
                        snapshotId,
                        operationId);
                }

                _adminLog.Add(
                    LogType.Mind,
                    LogImpact.Low,
                    $"Persistent inventory dry-run succeeded for {request.UserId}: " +
                    $"source {request.Source}, roots {packed.RootCount}, entities {packed.EntityCount}, " +
                    $"compressed {packed.CompressedBytes} bytes, uncompressed {packed.UncompressedBytes} bytes, " +
                    $"duration {stopwatch.Elapsed.TotalMilliseconds:F0} ms, actor {request.Actor}.");
                return Result(
                    PersistentInventorySaveStatus.DryRunSuccess,
                    $"Dry-run пройден: {packed.RootCount} корней, {packed.EntityCount} сущностей, " +
                    $"{packed.CompressedBytes} байт; БД и мир не изменены.",
                    request,
                    snapshotId,
                    operationId);
            }

            var current = await _db.GetPersistentInventoryHeaderAsync(request.UserId, cancel);
            if (current?.State == PersistentInventorySnapshotState.Staging)
            {
                return Result(
                    PersistentInventorySaveStatus.Busy,
                    "В БД уже есть незавершённый staging-кандидат.",
                    request,
                    snapshotId,
                    operationId);
            }

            var expectedRevision = current?.Revision ?? PersistentInventoryRevision.None;
            phaseStopwatch.Restart();
            var stage = await _db.StagePersistentInventoryAsync(
                request.UserId,
                new PersistentInventoryStageRequest(
                    snapshotId,
                    operationId,
                    expectedRevision,
                    capture.Payload!.SchemaVersion,
                    capture.Payload.PolicyId,
                    preparation.RoleId,
                    preparation.ProfileName,
                    packed.Data,
                    packed.Sha256,
                    capture.Payload.Roots.Count,
                    capture.Payload.Entities.Count,
                    packed.UncompressedBytes,
                    request.Actor,
                    request.ActorUserId,
                    request.Reason,
                    _manager.ServerEpoch),
                cancel);
            PersistentInventoryMetrics.PhaseDuration
                .WithLabels("save", "database_stage")
                .Observe(phaseStopwatch.Elapsed.TotalSeconds);
            PersistentInventoryMetrics.DatabaseOperations
                .WithLabels("stage", stage.IsSuccess ? "success" : stage.Status.ToString())
                .Inc();
            if (!stage.IsSuccess || stage.Header == null)
            {
                return Result(
                    PersistentInventorySaveStatus.DatabaseFailure,
                    $"БД отклонила staging: {stage.Status}.",
                    request,
                    snapshotId,
                    operationId);
            }

            staged = true;
            stagedRevision = stage.AppliedRevision;
            _manager.UpdateFromMutation(request.UserId, stage.Header);

            var stored = await _db.GetPersistentInventoryRevisionAsync(request.UserId, snapshotId, cancel);
            if (stored == null)
            {
                return Result(
                    PersistentInventorySaveStatus.DatabaseFailure,
                    "Записанный staging-кандидат не найден.",
                    request,
                    snapshotId,
                    operationId);
            }

            var storedPayload = PersistentInventoryPayloadCodec.Unpack(
                stored.Payload,
                stored.Metadata.PayloadSha256,
                _serializer.GetConfiguredLimits());
            var isolated = await RunOnMainThread(() => _serializer.RestoreIsolated(storedPayload));
            if (!isolated.Success)
            {
                return Result(
                    PersistentInventorySaveStatus.PolicyRejected,
                    $"Изолированная проверка кандидата не пройдена: {isolated.Error}",
                    request,
                    snapshotId,
                    operationId);
            }

            await RunOnMainThread(() => _serializer.DeleteIsolated(isolated));

            var revalidation = await RunOnMainThread(
                () => RevalidateOnMainThread(request, operationId, capture, packed));
            if (!revalidation.Success)
            {
                return Result(
                    PersistentInventorySaveStatus.PolicyRejected,
                    revalidation.Error!,
                    request,
                    snapshotId,
                    operationId);
            }

            if (PersistentInventoryRollout.GetDecision(_configuration, request.UserId) !=
                PersistentInventoryRolloutDecision.Full)
            {
                return Result(
                    PersistentInventorySaveStatus.Disabled,
                    "Rollout остановлен до разрешения необратимой очистки мира.",
                    request,
                    snapshotId,
                    operationId);
            }

            phaseStopwatch.Restart();
            var authorization = await AuthorizeWorldCleanupWithReconciliationAsync(
                request,
                snapshotId,
                operationId,
                stagedRevision);
            PersistentInventoryMetrics.PhaseDuration
                .WithLabels("save", "database_authorize")
                .Observe(phaseStopwatch.Elapsed.TotalSeconds);
            PersistentInventoryMetrics.DatabaseOperations
                .WithLabels("authorize", authorization.Status.ToString())
                .Inc();
            if (authorization.Status == CleanupAuthorizationStatus.Unknown)
            {
                // A lost response is not evidence that the transaction did not commit. Keep every
                // world entity locked and leave the staging operation for durable reconciliation.
                cleanupAuthorized = true;
                return Result(
                    PersistentInventorySaveStatus.PendingRecovery,
                    "Ответ БД на разрешение очистки потерян. Мир оставлен замороженным до сверки или перезапуска.",
                    request,
                    snapshotId,
                    operationId);
            }

            if (authorization.Status != CleanupAuthorizationStatus.Confirmed)
            {
                return Result(
                    PersistentInventorySaveStatus.DatabaseFailure,
                    $"БД не разрешила очистку мира: {authorization.Error}.",
                    request,
                    snapshotId,
                    operationId);
            }

            cleanupAuthorized = true;
            stagedRevision = authorization.AppliedRevision;

            await RunOnMainThread(() =>
            {
                RetireWorldOnMainThread(request, capture);
                worldRetired = true;
            });

            var promoted = await PromoteWithRetryAsync(
                request,
                snapshotId,
                operationId,
                stagedRevision);
            if (!promoted.IsSuccess)
            {
                _manager.Remove(request.UserId);
                _adminLog.Add(
                    LogType.Mind,
                    LogImpact.Extreme,
                    $"Persistent inventory world retirement for {request.UserId} operation {operationId} " +
                    $"is awaiting startup recovery; promotion status {promoted.Status}.");
                return Result(
                    PersistentInventorySaveStatus.PendingRecovery,
                    "Тело удалено после durable commit, но promotion ожидает startup recovery.",
                    request,
                    snapshotId,
                    operationId);
            }

            await _manager.LoadAsync(request.UserId, CancellationToken.None);
            await RunOnMainThread(() => _gameTicker.ReturnPlayerToLobby(request.Session));

            _cooldowns[request.UserId] = DateTime.UtcNow.AddSeconds(
                Math.Max(0, _configuration.GetCVar(CCVars.Wh40kPersistentInventorySaveCooldownSeconds)));
            _adminLog.Add(
                LogType.Mind,
                LogImpact.High,
                $"Persistent inventory save succeeded for {request.UserId}: snapshot {snapshotId}, " +
                $"operation {operationId}, source {request.Source}, items {packed.RootCount}, " +
                $"entities {packed.EntityCount}, compressed {packed.CompressedBytes} bytes, " +
                $"duration {stopwatch.Elapsed.TotalMilliseconds:F0} ms, actor {request.Actor}.");

            return Result(
                PersistentInventorySaveStatus.Success,
                "Инвентарь сохранён, тело удалено, игрок возвращён в лобби.",
                request,
                snapshotId,
                operationId);
        }
        catch (OperationCanceledException) when (!cleanupAuthorized)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(
                $"Persistent inventory save {operationId} for {request.UserId} failed " +
                $"(authorized={cleanupAuthorized}, retired={worldRetired}): {exception}");
            return Result(
                cleanupAuthorized
                    ? PersistentInventorySaveStatus.PendingRecovery
                    : PersistentInventorySaveStatus.DatabaseFailure,
                cleanupAuthorized
                    ? "Операция прошла durable commit и будет завершена startup recovery."
                    : $"Сохранение отменено до очистки мира: {exception.GetType().Name}.",
                request,
                snapshotId,
                operationId);
        }
        finally
        {
            if (staged && !cleanupAuthorized)
                await AbortCandidateBestEffortAsync(request, snapshotId, operationId);

            if (!cleanupAuthorized && !worldRetired && capture != null)
                await RunOnMainThread(() => RemoveOperationLocks(capture, request.Body, operationId));

            stopwatch.Stop();
            SaveDuration.Observe(stopwatch.Elapsed.TotalSeconds);
            PersistentInventoryMetrics.LockDuration
                .WithLabels("save")
                .Observe(stopwatch.Elapsed.TotalSeconds);
            PersistentInventoryMetrics.ActiveSaves.Dec();
            Interlocked.Decrement(ref _activeOperations);
            _accountLocks.TryRemove(request.UserId, out _);
        }
    }

    private SavePreparation PrepareOnMainThread(
        PersistentInventorySaveRequest request,
        PersistentInventoryOperationId operationId)
    {
        if (!Exists(request.Body) ||
            !TryComp(request.Body, out MobStateComponent? mob) ||
            !_mobState.IsAlive(request.Body, mob))
        {
            return SavePreparation.Fail(PersistentInventorySaveStatus.InvalidTarget, "Цель не является живым мобом.");
        }

        if (HasComp<PersistentInventoryOperationComponent>(request.Body))
        {
            return SavePreparation.Fail(
                PersistentInventorySaveStatus.Busy,
                "Тело уже заблокировано незавершённой persistent inventory операцией.");
        }

        if (!_mind.TryGetMind(request.Body, out _, out var mind) ||
            mind.UserId != request.UserId ||
            mind.CurrentEntity != request.Body ||
            request.Session.UserId != request.UserId ||
            request.Session.AttachedEntity != request.Body ||
            request.Session.Status == SessionStatus.Disconnected)
        {
            return SavePreparation.Fail(
                PersistentInventorySaveStatus.InvalidTarget,
                "Attached mob, mind и владелец аккаунта не совпадают.");
        }

        if (request.Source == PersistentInventorySaveSource.PhysicalCryo)
        {
            if (request.Cryopod is not { Valid: true } pod ||
                !Exists(pod) ||
                !_cryo.ContainsBody(pod, request.Body) ||
                request.Session.Status == SessionStatus.Disconnected)
            {
                return SavePreparation.Fail(
                    PersistentInventorySaveStatus.InvalidTarget,
                    "Физический cryo требует подключённого владельца в этой капсуле.");
            }

        }

        if (!request.Force &&
            TryComp(request.Body, out CuffableComponent? cuffable) &&
            cuffable.CuffedHandCount > 0)
        {
            return SavePreparation.Fail(
                PersistentInventorySaveStatus.InvalidTarget,
                "Нельзя сохранить персонажа в наручниках без --force.");
        }

        var capturedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var capture = _serializer.CaptureOwner(
            request.Body,
            capturedAtUnixMilliseconds: capturedAt);
        if (!capture.IsSuccess)
        {
            return SavePreparation.Fail(
                PersistentInventorySaveStatus.PolicyRejected,
                capture.Error ?? "Policy отклонила инвентарь.");
        }

        WarnAboutOmittedComponents(request.UserId, capture.OmittedComponents);
        AddOperationLocks(capture, request.Body, request.UserId, operationId);
        var roleId = TryComp(request.Body, out PlayerJobComponent? playerJob)
            ? playerJob.JobPrototype?.Id
            : null;
        return SavePreparation.Ok(capture, roleId, MetaData(request.Body).EntityName);
    }

    private async Task<CleanupAuthorizationResult> AuthorizeWorldCleanupWithReconciliationAsync(
        PersistentInventorySaveRequest request,
        PersistentInventorySnapshotId snapshotId,
        PersistentInventoryOperationId operationId,
        PersistentInventoryRevision stagedRevision)
    {
        var authorizeRequest = new PersistentInventoryAuthorizeWorldCleanupRequest(
            snapshotId,
            operationId,
            stagedRevision,
            _manager.ServerEpoch,
            request.Actor,
            request.ActorUserId,
            request.Reason);

        try
        {
            var direct = await _db.AuthorizePersistentInventoryWorldCleanupAsync(
                request.UserId,
                authorizeRequest,
                CancellationToken.None);
            if (direct.IsSuccess)
            {
                return new CleanupAuthorizationResult(
                    CleanupAuthorizationStatus.Confirmed,
                    direct.AppliedRevision);
            }

            return new CleanupAuthorizationResult(
                CleanupAuthorizationStatus.Rejected,
                direct.AppliedRevision,
                direct.Status.ToString());
        }
        catch (Exception exception)
        {
            Log.Warning(
                $"Persistent inventory authorize response was ambiguous for {request.UserId}, " +
                $"operation {operationId}: {exception.GetType().Name}. Reconciling.");
        }

        const int reconciliationAttempts = 3;
        for (var attempt = 0; attempt < reconciliationAttempts; attempt++)
        {
            try
            {
                var header = await _db.GetPersistentInventoryHeaderAsync(
                    request.UserId,
                    CancellationToken.None);
                if (header is
                    {
                        State: PersistentInventorySnapshotState.Staging,
                        SavePhase: PersistentInventorySavePhase.WorldCleanupAuthorized,
                        Staging: { } staging,
                    } &&
                    header.OperationId == operationId &&
                    staging.SnapshotId == snapshotId)
                {
                    return new CleanupAuthorizationResult(
                        CleanupAuthorizationStatus.Confirmed,
                        header.Revision);
                }

                if (header is
                    {
                        State: PersistentInventorySnapshotState.Active,
                        CurrentVerified: { } current,
                    } &&
                    header.OperationId == operationId &&
                    current.SnapshotId == snapshotId)
                {
                    // Startup reconciliation may already have promoted the authorized candidate.
                    // The world still has to be retired before the operation is considered complete.
                    return new CleanupAuthorizationResult(
                        CleanupAuthorizationStatus.Confirmed,
                        header.Revision);
                }

                if (header is not
                    {
                        State: PersistentInventorySnapshotState.Staging,
                        SavePhase: PersistentInventorySavePhase.CandidateStaged,
                        Staging: { } candidate,
                    } ||
                    header.OperationId != operationId ||
                    candidate.SnapshotId != snapshotId)
                {
                    return new CleanupAuthorizationResult(
                        CleanupAuthorizationStatus.Rejected,
                        header?.Revision ?? stagedRevision,
                        "durable header no longer matches the staged operation");
                }

                authorizeRequest = authorizeRequest with { ExpectedRevision = header.Revision };
                var retry = await _db.AuthorizePersistentInventoryWorldCleanupAsync(
                    request.UserId,
                    authorizeRequest,
                    CancellationToken.None);
                if (retry.IsSuccess)
                {
                    return new CleanupAuthorizationResult(
                        CleanupAuthorizationStatus.Confirmed,
                        retry.AppliedRevision);
                }

                if (retry.Status != PersistentInventoryMutationStatus.RevisionMismatch)
                {
                    return new CleanupAuthorizationResult(
                        CleanupAuthorizationStatus.Rejected,
                        retry.AppliedRevision,
                        retry.Status.ToString());
                }
            }
            catch (Exception retryException)
            {
                Log.Error(
                    $"Persistent inventory authorize reconciliation {attempt + 1}/{reconciliationAttempts} " +
                    $"failed for {request.UserId}, operation {operationId}: {retryException.GetType().Name}.");
            }

            if (attempt + 1 < reconciliationAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), CancellationToken.None);
        }

        return new CleanupAuthorizationResult(
            CleanupAuthorizationStatus.Unknown,
            stagedRevision,
            "database outcome remains ambiguous");
    }

    private void WarnAboutOmittedComponents(
        NetUserId userId,
        IReadOnlyList<PersistentInventoryOmittedComponent> omitted)
    {
        if (!_configuration.GetCVar(CCVars.Wh40kPersistentInventoryWarnOmittedComponents) ||
            omitted.Count == 0)
            return;

        const int exampleLimit = 12;
        var occurrences = omitted.Sum(entry => entry.Occurrences);
        var examples = string.Join(
            ", ",
            omitted.Take(exampleLimit)
                .Select(entry => $"{entry.PrototypeId}:{entry.ComponentId}"));
        var remainder = omitted.Count > exampleLimit
            ? $", +{omitted.Count - exampleLimit} more"
            : string.Empty;

        Log.Warning(
            $"Профиль persistent inventory {userId}: пропущено {occurrences} неподдерживаемых компонентов " +
            $"({omitted.Count} сочетаний прототип/компонент). Предметы будут восстановлены без этих состояний. / " +
            $"Persistent inventory profile omitted unsupported state; items will restore without it. " +
            $"Examples: {examples}{remainder}.");
    }

    private RevalidationResult RevalidateOnMainThread(
        PersistentInventorySaveRequest request,
        PersistentInventoryOperationId operationId,
        PersistentInventoryCaptureResult expectedCapture,
        PackedPersistentInventoryPayload expectedPacked)
    {
        if (!Exists(request.Body) ||
            !TryComp(request.Body, out PersistentInventoryOperationComponent? operation) ||
            operation.OperationId != operationId ||
            operation.UserId != request.UserId ||
            !_mind.TryGetMind(request.Body, out _, out var mind) ||
            mind.UserId != request.UserId ||
            mind.CurrentEntity != request.Body ||
            request.Session.UserId != request.UserId ||
            request.Session.AttachedEntity != request.Body ||
            request.Session.Status == SessionStatus.Disconnected ||
            !TryComp(request.Body, out MobStateComponent? mob) ||
            !_mobState.IsAlive(request.Body, mob))
        {
            return RevalidationResult.Fail("Тело или владелец изменились во время сохранения.");
        }

        if (request.Source == PersistentInventorySaveSource.PhysicalCryo)
        {
            if (request.Cryopod is not { Valid: true } pod ||
                !Exists(pod) ||
                !_cryo.ContainsBody(pod, request.Body))
            {
                return RevalidationResult.Fail("Состояние физического cryo изменилось во время сохранения.");
            }
        }

        if (!request.Force &&
            TryComp(request.Body, out CuffableComponent? cuffable) &&
            cuffable.CuffedHandCount > 0)
        {
            return RevalidationResult.Fail("На персонаже появились наручники во время сохранения.");
        }

        var capture = _serializer.CaptureOwner(
            request.Body,
            expectedCapture.Payload!.PolicyId,
            expectedCapture.Payload.CapturedAtUnixMilliseconds);
        if (!capture.IsSuccess)
            return RevalidationResult.Fail(capture.Error ?? "Повторный capture отклонён.");

        if (!capture.CapturedEntities.ToHashSet().SetEquals(expectedCapture.CapturedEntities) ||
            !capture.DeniedEntities.ToHashSet().SetEquals(expectedCapture.DeniedEntities))
        {
            return RevalidationResult.Fail("Состав ECS-графа изменился во время сохранения.");
        }

        foreach (var entity in capture.CapturedEntities
                     .Concat(capture.DeniedEntities)
                     .Append(request.Body)
                     .Distinct())
        {
            if (!TryComp(entity, out PersistentInventoryOperationComponent? entityOperation) ||
                entityOperation.OperationId != operationId ||
                entityOperation.UserId != request.UserId)
            {
                return RevalidationResult.Fail("Блокировка ECS-графа была нарушена во время сохранения.");
            }
        }

        var packed = PersistentInventoryPayloadCodec.Pack(capture.Payload!, _serializer.GetConfiguredLimits());
        if (!packed.Sha256.AsSpan().SequenceEqual(expectedPacked.Sha256) ||
            !packed.Data.AsSpan().SequenceEqual(expectedPacked.Data))
        {
            return RevalidationResult.Fail("Инвентарь изменился во время сохранения.");
        }

        return RevalidationResult.Ok();
    }

    private void RetireWorldOnMainThread(
        PersistentInventorySaveRequest request,
        PersistentInventoryCaptureResult capture)
    {
        if (request.Cryopod is { Valid: true } pod && Exists(pod))
            _cryo.CompletePersistentCryoWorldRetirement(request.Body, pod, request.UserId);
        else
            _cryo.CompletePersistentCryoWorldRetirement(request.Body, null, request.UserId);

        foreach (var entity in capture.CapturedEntities
                     .Concat(capture.DeniedEntities)
                     .Distinct()
                     .Where(entity => entity != request.Body))
        {
            if (Exists(entity) && !TerminatingOrDeleted(entity))
                Del(entity);
        }

        if (Exists(request.Body) && !TerminatingOrDeleted(request.Body))
            Del(request.Body);
    }

    private async Task<PersistentInventoryMutationResult> PromoteWithRetryAsync(
        PersistentInventorySaveRequest request,
        PersistentInventorySnapshotId snapshotId,
        PersistentInventoryOperationId operationId,
        PersistentInventoryRevision expectedRevision)
    {
        PersistentInventoryMutationResult? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                last = await _db.PromotePersistentInventoryAsync(
                    request.UserId,
                    new PersistentInventoryPromoteRequest(
                        snapshotId,
                        operationId,
                        expectedRevision,
                        request.Actor,
                        request.ActorUserId,
                        request.Reason),
                    CancellationToken.None);
                if (last.IsSuccess)
                    return last;
            }
            catch (Exception exception)
            {
                PersistentInventoryMetrics.DatabaseOperations
                    .WithLabels("promote", "retry_exception")
                    .Inc();
                Log.Error(
                    $"Persistent inventory promotion retry {attempt + 1} failed for {request.UserId}: " +
                    $"{exception.GetType().Name}.");
            }

            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
        }

        return last ?? new PersistentInventoryMutationResult(
            PersistentInventoryMutationStatus.CandidateNotFound,
            null,
            expectedRevision,
            PersistentInventorySnapshotState.Staging,
            snapshotId);
    }

    private async Task AbortCandidateBestEffortAsync(
        PersistentInventorySaveRequest request,
        PersistentInventorySnapshotId snapshotId,
        PersistentInventoryOperationId operationId)
    {
        try
        {
            var header = await _db.GetPersistentInventoryHeaderAsync(request.UserId);
            if (header?.State != PersistentInventorySnapshotState.Staging ||
                header.Staging?.SnapshotId != snapshotId ||
                header.OperationId != operationId ||
                header.SavePhase != PersistentInventorySavePhase.CandidateStaged)
            {
                return;
            }

            await _db.TransitionPersistentInventoryAsync(
                request.UserId,
                new PersistentInventoryTransitionRequest(
                    PersistentInventorySnapshotState.Aborted,
                    PersistentInventoryOperationId.New(),
                    header.Revision,
                    request.Actor,
                    request.ActorUserId,
                    $"Save aborted before world cleanup: {request.Reason}"));
            await _manager.LoadAsync(request.UserId);
        }
        catch (Exception exception)
        {
            Log.Error(
                $"Failed to abort staging candidate {snapshotId} for {request.UserId}: " +
                $"{exception.GetType().Name}.");
        }
    }

    public async Task ReconcileStagingAsync(CancellationToken cancel = default)
    {
        IReadOnlyList<PersistentInventorySnapshotHeader> staging;
        try
        {
            staging = await _db.GetPersistentInventoryStagingAsync(cancel);
        }
        catch (Exception exception)
        {
            Log.Error($"Cannot enumerate persistent inventory staging records: {exception}");
            throw;
        }

        foreach (var header in staging)
        {
            cancel.ThrowIfCancellationRequested();
            var userId = new NetUserId(header.AccountId.Value);
            if (!_accountLocks.TryAdd(userId, 0))
                continue;

            try
            {
                if (header.Staging == null)
                {
                    await QuarantineRecoveryAsync(
                        userId,
                        header,
                        "Staging header has no candidate.",
                        cancel);
                    continue;
                }

                if (header.SavePhase == PersistentInventorySavePhase.CandidateStaged)
                {
                    await _db.TransitionPersistentInventoryAsync(
                        userId,
                        new PersistentInventoryTransitionRequest(
                            PersistentInventorySnapshotState.Aborted,
                            PersistentInventoryOperationId.New(),
                            header.Revision,
                            "startup-reconciliation",
                            Reason: "Candidate was never authorized for world cleanup."),
                        cancel);
                    continue;
                }

                if (header.SavePhase != PersistentInventorySavePhase.WorldCleanupAuthorized ||
                    header.StagingServerEpoch == null ||
                    header.OperationId != header.Staging.OperationId)
                {
                    await QuarantineRecoveryAsync(
                        userId,
                        header,
                        "Ambiguous staging phase, epoch, or operation identity.",
                        cancel);
                    continue;
                }

                var stored = await _db.GetPersistentInventoryRevisionAsync(
                    userId,
                    header.Staging.SnapshotId,
                    cancel);
                if (stored == null)
                {
                    await QuarantineRecoveryAsync(userId, header, "Staging payload is missing.", cancel);
                    continue;
                }

                PersistentInventoryPayload payload;
                try
                {
                    payload = PersistentInventoryPayloadCodec.Unpack(
                        stored.Payload,
                        stored.Metadata.PayloadSha256,
                        _serializer.GetConfiguredLimits());
                }
                catch (InvalidDataException exception)
                {
                    await QuarantineRecoveryAsync(userId, header, exception.Message, cancel);
                    continue;
                }

                var isolated = await RunOnMainThread(() => _serializer.RestoreIsolated(payload));
                if (!isolated.Success)
                {
                    await QuarantineRecoveryAsync(
                        userId,
                        header,
                        isolated.Error ?? "Isolated recovery validation failed.",
                        cancel);
                    continue;
                }

                await RunOnMainThread(() => _serializer.DeleteIsolated(isolated));
                var promote = await _db.PromotePersistentInventoryAsync(
                    userId,
                    new PersistentInventoryPromoteRequest(
                        header.Staging.SnapshotId,
                        header.OperationId,
                        header.Revision,
                        "startup-reconciliation",
                        Reason: "World cleanup was durably authorized before restart."),
                    cancel);
                if (!promote.IsSuccess)
                {
                    Log.Error(
                        $"Startup reconciliation could not promote {userId}/{header.OperationId}: {promote.Status}.");
                }
                else
                {
                    _adminLog.Add(
                        LogType.Mind,
                        LogImpact.High,
                        $"Persistent inventory startup reconciliation promoted {userId}, " +
                        $"snapshot {header.Staging.SnapshotId}, operation {header.OperationId}.");
                }
            }
            finally
            {
                _accountLocks.TryRemove(userId, out _);
            }
        }
    }

    private async Task QuarantineRecoveryAsync(
        NetUserId userId,
        PersistentInventorySnapshotHeader header,
        string reason,
        CancellationToken cancel)
    {
        var result = await _db.TransitionPersistentInventoryAsync(
            userId,
            new PersistentInventoryTransitionRequest(
                PersistentInventorySnapshotState.Quarantined,
                PersistentInventoryOperationId.New(),
                header.Revision,
                "startup-reconciliation",
                Reason: reason,
                QuarantineReason: PersistentInventoryQuarantineReason.AmbiguousRecovery),
            cancel);
        if (!result.IsSuccess)
            Log.Error($"Failed to quarantine ambiguous persistent inventory staging for {userId}: {result.Status}.");
    }

    private bool TryEnterGlobalSlot()
    {
        var maximum = Math.Max(1, _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMaxConcurrentSaves));
        while (true)
        {
            var current = Volatile.Read(ref _activeOperations);
            if (current >= maximum)
                return false;
            if (Interlocked.CompareExchange(ref _activeOperations, current + 1, current) == current)
                return true;
        }
    }

    private void AddOperationLocks(
        PersistentInventoryCaptureResult capture,
        EntityUid body,
        NetUserId userId,
        PersistentInventoryOperationId operationId)
    {
        foreach (var entity in capture.CapturedEntities
                     .Concat(capture.DeniedEntities)
                     .Append(body)
                     .Distinct())
        {
            var component = EnsureComp<PersistentInventoryOperationComponent>(entity);
            component.UserId = userId;
            component.OperationId = operationId;
        }
    }

    private void RemoveOperationLocks(
        PersistentInventoryCaptureResult capture,
        EntityUid body,
        PersistentInventoryOperationId operationId)
    {
        foreach (var entity in capture.CapturedEntities
                     .Concat(capture.DeniedEntities)
                     .Append(body)
                     .Distinct())
        {
            if (TryComp(entity, out PersistentInventoryOperationComponent? component) &&
                component!.OperationId == operationId)
            {
                RemComp<PersistentInventoryOperationComponent>(entity);
            }
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
        await RunOnMainThread(() =>
        {
            action();
            return true;
        });
    }

    private PersistentInventorySaveResult Result(
        PersistentInventorySaveStatus status,
        string message,
        PersistentInventorySaveRequest request,
        PersistentInventorySnapshotId? snapshotId = null,
        PersistentInventoryOperationId? operationId = null)
    {
        SaveOutcomes.WithLabels(status.ToString(), request.Source.ToString()).Inc();
        if (status == PersistentInventorySaveStatus.PolicyRejected)
            PersistentInventoryMetrics.ValidationFailures.WithLabels("save", "policy_or_roundtrip").Inc();
        if (status == PersistentInventorySaveStatus.DatabaseFailure)
            PersistentInventoryMetrics.DatabaseOperations.WithLabels("save", "failure").Inc();
        return new PersistentInventorySaveResult(status, message, snapshotId, operationId);
    }

    private void Cancel(
        EntityUid uid,
        PersistentInventoryOperationComponent component,
        CancellableEntityEventArgs args)
    {
        args.Cancel();
    }

    private void CancelInteraction(
        Entity<PersistentInventoryOperationComponent> entity,
        ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void CancelStorage(
        Entity<PersistentInventoryOperationComponent> entity,
        ref StorageInteractAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void CancelStorageUsing(
        Entity<PersistentInventoryOperationComponent> entity,
        ref StorageInteractUsingAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void CancelPull(
        EntityUid uid,
        PersistentInventoryOperationComponent component,
        PullAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void CancelSpeak(
        EntityUid uid,
        PersistentInventoryOperationComponent component,
        SpeakAttemptEvent args)
    {
        args.Cancel();
    }

    private void CancelEmote(
        EntityUid uid,
        PersistentInventoryOperationComponent component,
        EmoteAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnOperationLockStartup(
        EntityUid uid,
        PersistentInventoryOperationComponent component,
        ComponentStartup args)
    {
        if (TryComp<PullableComponent>(uid, out var pullable))
            _pulling.TryStopPull(uid, pullable);

        UpdateCanMove(uid, component, args);
    }

    private void CancelMovement(
        EntityUid uid,
        PersistentInventoryOperationComponent component,
        UpdateCanMoveEvent args)
    {
        if (component.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    private void UpdateCanMove(
        EntityUid uid,
        PersistentInventoryOperationComponent component,
        EntityEventArgs args)
    {
        _actionBlocker.UpdateCanMove(uid);
    }

    private void OnGhostAttempt(GhostAttemptHandleEvent args)
    {
        if (args.Mind.CurrentEntity is { Valid: true } body &&
            HasComp<PersistentInventoryOperationComponent>(body))
        {
            args.Result = false;
            args.Handled = true;
        }
    }

    private sealed record SavePreparation(
        bool Success,
        PersistentInventorySaveStatus Status,
        PersistentInventoryCaptureResult? Capture,
        string? RoleId,
        string? ProfileName,
        string? Error)
    {
        public static SavePreparation Ok(
            PersistentInventoryCaptureResult capture,
            string? roleId,
            string? profileName)
        {
            return new SavePreparation(
                true,
                PersistentInventorySaveStatus.Success,
                capture,
                roleId,
                profileName,
                null);
        }

        public static SavePreparation Fail(PersistentInventorySaveStatus status, string error)
        {
            return new SavePreparation(false, status, null, null, null, error);
        }
    }

    private enum CleanupAuthorizationStatus
    {
        Confirmed,
        Rejected,
        Unknown,
    }

    private sealed record CleanupAuthorizationResult(
        CleanupAuthorizationStatus Status,
        PersistentInventoryRevision AppliedRevision,
        string? Error = null);

    private sealed record RevalidationResult(bool Success, string? Error)
    {
        public static RevalidationResult Ok()
        {
            return new RevalidationResult(true, null);
        }

        public static RevalidationResult Fail(string error)
        {
            return new RevalidationResult(false, error);
        }
    }
}
