using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Body.Components;
using Content.Server.Database;
using Content.Server.GameTicking.Events;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Interaction.Events;
using Prometheus;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.PersistentInventory;

public sealed record PersistentInventoryAdminMutationResult(bool Success, string Message);

/// <summary>
/// Completes the lifecycle of a snapshot that has already been materialized.
/// Ordinary physical death is intentionally not considered a loss event here.
/// </summary>
public sealed class PersistentInventoryLifecycleSystem : EntitySystem
{
    private static readonly Counter LifeLossOutcomes = Metrics.CreateCounter(
        "wh40k_persistent_inventory_life_loss_total",
        "Persistent inventory bound-life terminal transitions.",
        "result",
        "reason");

    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly PersistentInventoryManager _manager = default!;
    [Dependency] private readonly PersistentInventorySnapshotSerializer _serializer = default!;

    private readonly Dictionary<NetUserId, EntityUid> _boundBodies = new();
    private readonly Dictionary<NetUserId, PendingDisconnect> _disconnects = new();
    private readonly Dictionary<NetUserId, Task<bool>> _lifeLossTasks = new();
    private readonly CancellationTokenSource _shutdown = new();

    private Task _bootstrap = Task.CompletedTask;
    private Task _metricsRefresh = Task.CompletedTask;
    private TimeSpan _nextMetricsRefresh;
    private bool _bootstrapStarted;
    private bool _epochStarted;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<PersistentInventoryBoundLifeComponent, BeforeGibbedEvent>(OnBeforeGibbed);
        SubscribeLocalEvent<PersistentInventoryBoundLifeComponent, SuicideEvent>(OnSuicide);
        SubscribeLocalEvent<PersistentInventoryBoundLifeComponent, PlayerAttachedEvent>(OnBoundPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnBoundPlayerDetached);
        SubscribeLocalEvent<PersistentInventoryBoundLifeComponent, EntityTerminatingEvent>(OnBodyTerminating);
        SubscribeLocalEvent<PersistentInventoryBoundLifeComponent, ComponentShutdown>(OnBoundLifeShutdown);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _shutdown.Cancel();

        _shutdown.Dispose();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (_metricsRefresh.IsCompleted && now >= _nextMetricsRefresh)
        {
            _nextMetricsRefresh = now + TimeSpan.FromSeconds(
                Math.Max(5, _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMetricsRefreshSeconds)));
            _metricsRefresh = RefreshMetricsAsync();
        }

        foreach (var (userId, pending) in _disconnects.ToArray())
        {
            if (pending.Deadline > now)
                continue;

            _disconnects.Remove(userId);
            StartLifeLoss(
                pending.Body,
                PersistentInventorySnapshotState.LostByDisconnect,
                PersistentInventoryInvalidationReason.None,
                PersistentInventoryLossReason.DisconnectTimeout,
                "disconnect-timeout",
                null,
                "Disconnect timer expired; bound body will be deleted after durable loss.",
                deleteBodyAfterCommit: true);
        }
    }

    public Task EnsureReadyAsync()
    {
        if (!_configuration.GetCVar(CCVars.Wh40kPersistentInventoryEnabled))
            return Task.CompletedTask;
        if (_bootstrapStarted)
            return _bootstrap;

        _bootstrapStarted = true;
        _bootstrap = BootstrapAsync();
        return _bootstrap;
    }

    public void BindBody(
        EntityUid body,
        NetUserId userId,
        PersistentInventorySnapshotId snapshotId,
        PersistentInventoryLifeId lifeId)
    {
        var component = EnsureComp<PersistentInventoryBoundLifeComponent>(body);
        component.UserId = userId;
        component.SnapshotId = snapshotId;
        component.LifeId = lifeId;
        component.LifeLossStarted = false;
        component.SuppressBodyDeletion = false;
        _boundBodies[userId] = body;
        if (!_players.TryGetSessionById(userId, out var session) ||
            session.Status == SessionStatus.Disconnected)
        {
            ScheduleDisconnect(userId, body);
        }
    }

    public bool HasActiveBoundBody(NetUserId userId)
    {
        return _boundBodies.TryGetValue(userId, out var body) &&
               Exists(body) &&
               TryComp(body, out PersistentInventoryBoundLifeComponent? component) &&
               component.UserId == userId &&
               !component.SuppressBodyDeletion;
    }

    /// <summary>
    /// Repairs a durable bind that belongs to this server epoch but has no corresponding
    /// world body. This can only happen when a spawn was interrupted after the database
    /// bind was committed. A real bound body is never released here.
    /// </summary>
    public async Task<bool> TryRecoverOrphanedBoundLifeAsync(NetUserId userId)
    {
        if (PersistentInventoryRollout.GetDecision(_configuration, userId) !=
                PersistentInventoryRolloutDecision.Full ||
            _manager.IsLifeLossPending(userId) ||
            HasActiveBoundBody(userId))
        {
            return false;
        }

        var entry = _manager.Get(userId);
        if (entry is not
            {
                Status: PersistentInventoryCacheStatus.Bound,
                Header:
                {
                    State: PersistentInventorySnapshotState.Bound,
                    ServerEpoch: { } serverEpoch,
                } header,
            } ||
            serverEpoch != _manager.ServerEpoch)
        {
            return false;
        }

        try
        {
            var result = await _db.TransitionPersistentInventoryAsync(
                userId,
                new PersistentInventoryTransitionRequest(
                    PersistentInventorySnapshotState.Active,
                    PersistentInventoryOperationId.New(),
                    header.Revision,
                    "spawn-orphan-reconciliation",
                    Reason: "Current server epoch has a bound snapshot without a world body.",
                    LossReason: PersistentInventoryLossReason.ServerRecovery,
                    AuditAction: PersistentInventoryAuditAction.Recovered),
                CancellationToken.None);

            if (result.Header is { } refreshed)
                _manager.UpdateFromMutation(userId, refreshed);
            else
                _manager.Remove(userId);

            if (!result.IsSuccess || result.Header?.State != PersistentInventorySnapshotState.Active)
            {
                Log.Warning(
                    $"Persistent inventory orphaned bind recovery was rejected for {userId}: {result.Status}.");
                return false;
            }

            _adminLog.Add(
                LogType.Mind,
                LogImpact.High,
                $"Persistent inventory orphaned bound life recovered for {userId}: " +
                $"snapshot {header.CurrentVerified?.SnapshotId}, epoch {_manager.ServerEpoch}.");
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(
                $"Persistent inventory orphaned bind recovery failed for {userId}: " +
                $"{exception.GetType().Name}.");
            return false;
        }
    }

    public async Task<bool> InvalidateBoundLifeAsync(
        EntityUid body,
        PersistentInventoryInvalidationReason reason,
        string actor,
        Guid? actorUserId,
        string details,
        CancellationToken cancel = default)
    {
        if (!_configuration.GetCVar(CCVars.Wh40kPersistentInventoryEnabled) ||
            !TryComp(body, out PersistentInventoryBoundLifeComponent? component))
        {
            return true;
        }

        var task = StartLifeLoss(
            body,
            PersistentInventorySnapshotState.Invalid,
            reason,
            PersistentInventoryLossReason.None,
            actor,
            actorUserId,
            details,
            deleteBodyAfterCommit: false);
        return task == null || await task.WaitAsync(cancel);
    }

    public void QueueInvalidation(
        EntityUid body,
        PersistentInventoryInvalidationReason reason,
        string actor,
        Guid? actorUserId,
        string details)
    {
        if (!_configuration.GetCVar(CCVars.Wh40kPersistentInventoryEnabled))
            return;

        _ = StartLifeLoss(
            body,
            PersistentInventorySnapshotState.Invalid,
            reason,
            PersistentInventoryLossReason.None,
            actor,
            actorUserId,
            details,
            deleteBodyAfterCommit: false);
    }

    /// <summary>
    /// Waits for any in-flight life loss and guarantees that a durable bound life is retired
    /// before the respawn flow removes the current mind.
    /// </summary>
    public async Task<PersistentInventoryAdminMutationResult> PrepareRespawnAsync(
        NetUserId userId,
        PersistentInventoryInvalidationReason reason,
        string actor,
        Guid? actorUserId,
        string details,
        CancellationToken cancel = default)
    {
        if (!_configuration.GetCVar(CCVars.Wh40kPersistentInventoryEnabled))
            return new PersistentInventoryAdminMutationResult(true, "Persistent inventory is disabled.");

        await EnsureReadyAsync().WaitAsync(cancel);

        if (_lifeLossTasks.TryGetValue(userId, out var pending) &&
            !await pending.WaitAsync(cancel))
        {
            return new PersistentInventoryAdminMutationResult(
                false,
                "The already running bound-life invalidation was not confirmed.");
        }

        if (_boundBodies.TryGetValue(userId, out var body) &&
            TryComp(body, out PersistentInventoryBoundLifeComponent? component) &&
            component.UserId == userId &&
            !component.SuppressBodyDeletion)
        {
            var committed = await InvalidateBoundLifeAsync(
                body,
                reason,
                actor,
                actorUserId,
                details,
                cancel);
            if (!committed)
            {
                return new PersistentInventoryAdminMutationResult(
                    false,
                    "The bound-life invalidation was not confirmed.");
            }
        }

        if (_lifeLossTasks.TryGetValue(userId, out pending) &&
            !await pending.WaitAsync(cancel))
        {
            return new PersistentInventoryAdminMutationResult(
                false,
                "The bound-life invalidation did not finish successfully.");
        }

        var entry = _manager.Get(userId);
        if (entry.Status is PersistentInventoryCacheStatus.Staging
            or PersistentInventoryCacheStatus.Unavailable)
        {
            return new PersistentInventoryAdminMutationResult(
                false,
                "A persistent inventory save or database reconciliation is still pending.");
        }

        if (entry.Status != PersistentInventoryCacheStatus.Bound)
        {
            if (HasActiveBoundBody(userId))
            {
                return new PersistentInventoryAdminMutationResult(
                    false,
                    "A bound body remained after the durable life was retired.");
            }

            return new PersistentInventoryAdminMutationResult(true, "No bound life remains.");
        }

        var transition = await AdminTransitionAsync(
            userId,
            PersistentInventorySnapshotState.Invalid,
            actor,
            actorUserId,
            details,
            reason,
            PersistentInventoryQuarantineReason.None,
            PersistentInventoryAuditAction.Invalidated,
            cancel);
        if (!transition.Success)
            return transition;

        if (_boundBodies.TryGetValue(userId, out body))
            await CompleteWorldLifeLossAsync(body, userId, deleteBody: false);

        return new PersistentInventoryAdminMutationResult(true, "The bound life was retired for respawn.");
    }

    public async Task<PersistentInventoryAdminMutationResult> AdminInvalidateAsync(
        NetUserId userId,
        string actor,
        Guid? actorUserId,
        string reason,
        CancellationToken cancel = default)
    {
        await EnsureReadyAsync().WaitAsync(cancel);
        if (HasActiveBoundBody(userId) &&
            _boundBodies.TryGetValue(userId, out var body))
        {
            var success = await InvalidateBoundLifeAsync(
                body,
                PersistentInventoryInvalidationReason.StaffAction,
                actor,
                actorUserId,
                reason,
                cancel);
            return new PersistentInventoryAdminMutationResult(
                success,
                success ? "Привязанная жизнь инвалидирована." : "Инвалидация не подтверждена БД.");
        }

        return await AdminTransitionAsync(
            userId,
            PersistentInventorySnapshotState.Invalid,
            actor,
            actorUserId,
            reason,
            PersistentInventoryInvalidationReason.StaffAction,
            PersistentInventoryQuarantineReason.None,
            PersistentInventoryAuditAction.Invalidated,
            cancel);
    }

    public async Task<PersistentInventoryAdminMutationResult> AdminQuarantineAsync(
        NetUserId userId,
        string actor,
        Guid? actorUserId,
        string reason,
        CancellationToken cancel = default)
    {
        await EnsureReadyAsync().WaitAsync(cancel);
        if (HasActiveBoundBody(userId) || _manager.IsLifeLossPending(userId))
        {
            return new PersistentInventoryAdminMutationResult(
                false,
                "Нельзя поместить снимок в карантин, пока существует активное привязанное тело.");
        }

        return await AdminTransitionAsync(
            userId,
            PersistentInventorySnapshotState.Quarantined,
            actor,
            actorUserId,
            reason,
            PersistentInventoryInvalidationReason.None,
            PersistentInventoryQuarantineReason.StaffAction,
            PersistentInventoryAuditAction.Quarantined,
            cancel);
    }

    public async Task<PersistentInventoryAdminMutationResult> AdminRollbackAsync(
        NetUserId userId,
        PersistentInventorySnapshotId snapshotId,
        string actor,
        Guid? actorUserId,
        string reason,
        CancellationToken cancel = default)
    {
        await EnsureReadyAsync().WaitAsync(cancel);
        if (HasActiveBoundBody(userId) || _manager.IsLifeLossPending(userId))
        {
            return new PersistentInventoryAdminMutationResult(
                false,
                "Rollback запрещён, пока существует активная или завершаемая привязанная жизнь.");
        }

        var header = await _db.GetPersistentInventoryHeaderAsync(userId, cancel);
        if (header == null)
            return new PersistentInventoryAdminMutationResult(false, "Снимок аккаунта не найден.");

        var result = await _db.SelectPersistentInventoryRevisionAsync(
            userId,
            new PersistentInventorySelectRevisionRequest(
                snapshotId,
                PersistentInventoryOperationId.New(),
                header.Revision,
                PersistentInventoryRevisionSelectionMode.Rollback,
                actor,
                actorUserId,
                Limit(reason)),
            cancel);
        return await CompleteAdminMutationAsync(
            userId,
            result,
            $"Rollback на snapshot {snapshotId} выполнен.",
            cancel);
    }

    public async Task<PersistentInventoryAdminMutationResult> AdminRecoverLostAsync(
        NetUserId userId,
        string actor,
        Guid? actorUserId,
        string reason,
        CancellationToken cancel = default)
    {
        await EnsureReadyAsync().WaitAsync(cancel);
        if (HasActiveBoundBody(userId) || _manager.IsLifeLossPending(userId))
        {
            return new PersistentInventoryAdminMutationResult(
                false,
                "Recover-lost запрещён, пока существует активная или завершаемая привязанная жизнь.");
        }

        var header = await _db.GetPersistentInventoryHeaderAsync(userId, cancel);
        if (header == null)
            return new PersistentInventoryAdminMutationResult(false, "Снимок аккаунта не найден.");

        var latestLostSnapshot = await _db.GetLatestPersistentInventoryLostSnapshotAsync(userId, cancel);
        if (latestLostSnapshot == null)
            return new PersistentInventoryAdminMutationResult(false, "Доступный LostByDisconnect snapshot не найден.");

        var result = await _db.SelectPersistentInventoryRevisionAsync(
            userId,
            new PersistentInventorySelectRevisionRequest(
                latestLostSnapshot.Value,
                PersistentInventoryOperationId.New(),
                header.Revision,
                PersistentInventoryRevisionSelectionMode.RecoverLost,
                actor,
                actorUserId,
                Limit(reason)),
            cancel);
        return await CompleteAdminMutationAsync(
            userId,
            result,
            $"Последний LostByDisconnect snapshot {latestLostSnapshot.Value} восстановлен в Active.",
            cancel);
    }

    private void OnRoundStarting(RoundStartingEvent args)
    {
        _ = EnsureReadyAsync();
    }

    private void OnBeforeGibbed(
        Entity<PersistentInventoryBoundLifeComponent> entity,
        ref BeforeGibbedEvent args)
    {
        QueueInvalidation(
            entity,
            PersistentInventoryInvalidationReason.Gib,
            "body-gib",
            null,
            "Bound body was gibbed.");
    }

    private void OnSuicide(
        Entity<PersistentInventoryBoundLifeComponent> entity,
        ref SuicideEvent args)
    {
        QueueInvalidation(
            entity,
            PersistentInventoryInvalidationReason.Suicide,
            "suicide",
            entity.Comp.UserId.UserId,
            "Bound life ended by suicide.");
    }

    private void OnBodyTerminating(
        Entity<PersistentInventoryBoundLifeComponent> entity,
        ref EntityTerminatingEvent args)
    {
        if (entity.Comp.SuppressBodyDeletion || entity.Comp.LifeLossStarted)
            return;

        QueueInvalidation(
            entity,
            PersistentInventoryInvalidationReason.BodyDeleted,
            "body-delete",
            null,
            "Bound body was deleted outside controlled cleanup.");
    }

    private void OnBoundPlayerAttached(
        Entity<PersistentInventoryBoundLifeComponent> entity,
        ref PlayerAttachedEvent args)
    {
        if (args.Player.UserId != entity.Comp.UserId ||
            entity.Comp.LifeLossStarted ||
            _boundBodies.GetValueOrDefault(entity.Comp.UserId) != entity.Owner)
        {
            return;
        }

        _disconnects.Remove(entity.Comp.UserId);
    }

    private void OnBoundPlayerDetached(PlayerDetachedEvent args)
    {
        var userId = args.Player.UserId;
        if (args.Player.Status != SessionStatus.Disconnected ||
            !_boundBodies.TryGetValue(userId, out var body) ||
            body != args.Entity ||
            !TryComp(body, out PersistentInventoryBoundLifeComponent? component) ||
            component.UserId != userId ||
            component.LifeLossStarted)
        {
            return;
        }

        ScheduleDisconnect(userId, body);
    }

    private void OnBoundLifeShutdown(
        Entity<PersistentInventoryBoundLifeComponent> entity,
        ref ComponentShutdown args)
    {
        if (_boundBodies.GetValueOrDefault(entity.Comp.UserId) == entity.Owner)
            _boundBodies.Remove(entity.Comp.UserId);
        _disconnects.Remove(entity.Comp.UserId);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        var userId = args.Session.UserId;
        if (!_boundBodies.TryGetValue(userId, out var body) ||
            !TryComp(body, out PersistentInventoryBoundLifeComponent? component) ||
            component.UserId != userId ||
            component.LifeLossStarted)
        {
            return;
        }

        if (args.NewStatus == SessionStatus.Disconnected)
        {
            ScheduleDisconnect(userId, body);
            return;
        }

        if (args.NewStatus is not (SessionStatus.Connected or SessionStatus.InGame))
            return;
        if (args.Session.AttachedEntity == body)
            _disconnects.Remove(userId);
    }

    private void ScheduleDisconnect(NetUserId userId, EntityUid body)
    {
        var seconds = Math.Max(
            0,
            _configuration.GetCVar(CCVars.Wh40kPersistentInventoryDisconnectDeleteDelaySeconds));
        _disconnects[userId] = new PendingDisconnect(
            body,
            _timing.CurTime + TimeSpan.FromSeconds(seconds));
    }

    private Task<bool>? StartLifeLoss(
        EntityUid body,
        PersistentInventorySnapshotState targetState,
        PersistentInventoryInvalidationReason invalidationReason,
        PersistentInventoryLossReason lossReason,
        string actor,
        Guid? actorUserId,
        string details,
        bool deleteBodyAfterCommit)
    {
        if (!TryComp(body, out PersistentInventoryBoundLifeComponent? component))
            return null;
        if (_lifeLossTasks.TryGetValue(component.UserId, out var existing))
            return existing;

        if (!_manager.TryBeginLifeLoss(component.UserId, component.LifeId, out var reservation))
            return _manager.IsLifeLossPending(component.UserId)
                ? _lifeLossTasks.GetValueOrDefault(component.UserId)
                : null;

        component.LifeLossStarted = true;
        _disconnects.Remove(component.UserId);
        var task = RunLifeLossWithRetryAsync(
            body,
            reservation,
            targetState,
            invalidationReason,
            lossReason,
            actor,
            actorUserId,
            details,
            deleteBodyAfterCommit);
        _lifeLossTasks.Add(component.UserId, task);
        _ = ObserveLifeLossAsync(component.UserId, task);
        return task;
    }

    private async Task<bool> RunLifeLossWithRetryAsync(
        EntityUid body,
        PersistentInventoryLifeLossReservation initialReservation,
        PersistentInventorySnapshotState targetState,
        PersistentInventoryInvalidationReason invalidationReason,
        PersistentInventoryLossReason lossReason,
        string actor,
        Guid? actorUserId,
        string details,
        bool deleteBodyAfterCommit)
    {
        var reservation = initialReservation;
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                var auditAction = targetState switch
                {
                    PersistentInventorySnapshotState.Invalid =>
                        PersistentInventoryAuditAction.Invalidated,
                    PersistentInventorySnapshotState.LostByDisconnect =>
                        PersistentInventoryAuditAction.Lost,
                    PersistentInventorySnapshotState.Active =>
                        PersistentInventoryAuditAction.Recovered,
                    _ => PersistentInventoryAuditAction.StateChanged,
                };
                var result = await _db.TransitionPersistentInventoryAsync(
                    reservation.UserId,
                    new PersistentInventoryTransitionRequest(
                        targetState,
                        reservation.OperationId,
                        reservation.ExpectedRevision,
                        actor,
                        actorUserId,
                        Limit(details),
                        invalidationReason,
                        lossReason,
                        AuditAction: auditAction),
                    CancellationToken.None);

                if (result.IsSuccess && result.Header is { } committed)
                {
                    _manager.CompleteLifeLoss(reservation, committed);
                    await CompleteWorldLifeLossAsync(body, reservation.UserId, deleteBodyAfterCommit);
                    _adminLog.Add(
                        LogType.Mind,
                        LogImpact.High,
                        $"Persistent inventory life loss committed for {reservation.UserId}: " +
                        $"snapshot {reservation.SnapshotId}, life {reservation.LifeId}, " +
                        $"state {targetState}, operation {reservation.OperationId}.");
                    LifeLossOutcomes.WithLabels("success", targetState.ToString()).Inc();
                    return true;
                }

                if (result.Header is { } current)
                {
                    if (current.State != PersistentInventorySnapshotState.Bound ||
                        current.LifeId != reservation.LifeId)
                    {
                        _manager.CompleteLifeLoss(reservation, current);
                        await CompleteWorldLifeLossAsync(body, reservation.UserId, deleteBodyAfterCommit);
                        LifeLossOutcomes.WithLabels("superseded", targetState.ToString()).Inc();
                        return true;
                    }

                    if (_manager.TryRefreshLifeLoss(reservation, current, out var refreshed))
                    {
                        reservation = refreshed;
                        continue;
                    }
                }

                Log.Error(
                    $"Persistent inventory life loss rejected for {reservation.UserId}: {result.Status}.");
            }
            catch (Exception exception)
            {
                PersistentInventoryMetrics.DatabaseOperations
                    .WithLabels("life_loss", "retry_exception")
                    .Inc();
                Log.Error(
                    $"Persistent inventory life loss retry for {reservation.UserId} failed: " +
                    $"{exception.GetType().Name}.");
            }

            var retrySeconds = Math.Max(
                1,
                _configuration.GetCVar(CCVars.Wh40kPersistentInventoryDatabaseRetrySeconds));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(retrySeconds), _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        LifeLossOutcomes.WithLabels("shutdown", targetState.ToString()).Inc();
        return false;
    }

    private async Task RefreshMetricsAsync()
    {
        PersistentInventoryMetrics.SetRolloutMode(PersistentInventoryRollout.GetMode(_configuration));
        try
        {
            var counts = await _db.GetPersistentInventoryStateCountsAsync(_shutdown.Token);
            PersistentInventoryMetrics.SetStateCounts(counts);
            PersistentInventoryMetrics.DatabaseOperations.WithLabels("state_counts", "success").Inc();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PersistentInventoryMetrics.DatabaseOperations.WithLabels("state_counts", "failure").Inc();
            Log.Warning($"Persistent inventory metrics refresh failed: {exception.GetType().Name}.");
        }
    }

    private async Task ObserveLifeLossAsync(NetUserId userId, Task<bool> task)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            Log.Error($"Persistent inventory life loss task for {userId} escaped: {exception}");
        }
        finally
        {
            await RunOnMainThread(() => _lifeLossTasks.Remove(userId));
        }
    }

    private async Task CompleteWorldLifeLossAsync(
        EntityUid body,
        NetUserId userId,
        bool deleteBody)
    {
        await RunOnMainThread(() =>
        {
            _disconnects.Remove(userId);
            if (_boundBodies.GetValueOrDefault(userId) == body)
                _boundBodies.Remove(userId);
            if (!TryComp(body, out PersistentInventoryBoundLifeComponent? component))
                return;

            component.SuppressBodyDeletion = true;
            if (deleteBody && !TerminatingOrDeleted(body))
                QueueDel(body);
            else
                RemCompDeferred<PersistentInventoryBoundLifeComponent>(body);
        });
    }

    private async Task BootstrapAsync()
    {
        await _db.BeginPersistentInventoryServerEpochAsync(
            _manager.ServerEpoch,
            _shutdown.Token);
        _epochStarted = true;

        var bound = await _db.GetPersistentInventoryBoundAsync(_shutdown.Token);
        foreach (var header in bound)
        {
            _shutdown.Token.ThrowIfCancellationRequested();
            if (header.ServerEpoch == _manager.ServerEpoch)
                continue;

            var userId = new NetUserId(header.AccountId.Value);
            if (header.ServerEpoch is not { } oldEpoch)
            {
                await QuarantineStartupAsync(
                    userId,
                    header,
                    PersistentInventoryQuarantineReason.DatabaseInvariant,
                    "Bound snapshot has no server epoch.");
                continue;
            }

            var epoch = await _db.GetPersistentInventoryServerEpochAsync(oldEpoch, _shutdown.Token);
            if (epoch?.CleanShutdownAt == null)
            {
                await QuarantineStartupAsync(
                    userId,
                    header,
                    PersistentInventoryQuarantineReason.AmbiguousRecovery,
                    "Previous server epoch did not record a clean shutdown.");
                continue;
            }

            var currentValidation = await ValidateRevisionAsync(userId, header.CurrentVerified);
            if (currentValidation.Success)
            {
                var recovery = await _db.TransitionPersistentInventoryAsync(
                    userId,
                    new PersistentInventoryTransitionRequest(
                        PersistentInventorySnapshotState.Active,
                        PersistentInventoryOperationId.New(),
                        header.Revision,
                        "startup-reconciliation",
                        Reason: "Clean previous epoch retired the bound world.",
                        LossReason: PersistentInventoryLossReason.ServerRecovery,
                        AuditAction: PersistentInventoryAuditAction.Recovered),
                    _shutdown.Token);
                if (recovery.IsSuccess && recovery.Header != null)
                    _manager.UpdateFromMutation(userId, recovery.Header);
                else
                    throw new InvalidOperationException($"Bound recovery failed: {recovery.Status}.");
                continue;
            }

            var fallbackValidation = await ValidateRevisionAsync(userId, header.LastKnownGood);
            if (fallbackValidation.Success && header.LastKnownGood is { } fallback)
            {
                var rollback = await _db.SelectPersistentInventoryRevisionAsync(
                    userId,
                    new PersistentInventorySelectRevisionRequest(
                        fallback.SnapshotId,
                        PersistentInventoryOperationId.New(),
                        header.Revision,
                        PersistentInventoryRevisionSelectionMode.StartupFallback,
                        "startup-reconciliation",
                        null,
                        $"Current revision failed validation: {Limit(currentValidation.Error)}"),
                    _shutdown.Token);
                if (rollback.IsSuccess && rollback.Header != null)
                    _manager.UpdateFromMutation(userId, rollback.Header);
                else
                    throw new InvalidOperationException($"LastKnownGood recovery failed: {rollback.Status}.");
                continue;
            }

            await QuarantineStartupAsync(
                userId,
                header,
                ClassifyQuarantine(currentValidation.Error),
                $"Current and LastKnownGood revisions are unusable: {Limit(currentValidation.Error)}");
        }
    }

    private async Task<RevisionValidation> ValidateRevisionAsync(
        NetUserId userId,
        PersistentInventoryRevisionMetadata? metadata)
    {
        if (metadata == null)
            return RevisionValidation.Fail("Revision metadata is missing.");

        var stored = await _db.GetPersistentInventoryRevisionAsync(
            userId,
            metadata.SnapshotId,
            _shutdown.Token);
        if (stored == null)
            return RevisionValidation.Fail("Revision payload is missing.");

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
            return RevisionValidation.Fail(exception.Message);
        }

        var restored = await RunOnMainThread(() => _serializer.RestoreIsolated(payload));
        if (!restored.Success)
            return RevisionValidation.Fail(restored.Error ?? "Isolated validation failed.");

        await RunOnMainThread(() => _serializer.DeleteIsolated(restored));
        return RevisionValidation.Ok();
    }

    private async Task QuarantineStartupAsync(
        NetUserId userId,
        PersistentInventorySnapshotHeader header,
        PersistentInventoryQuarantineReason reason,
        string details)
    {
        var result = await _db.TransitionPersistentInventoryAsync(
            userId,
            new PersistentInventoryTransitionRequest(
                PersistentInventorySnapshotState.Quarantined,
                PersistentInventoryOperationId.New(),
                header.Revision,
                "startup-reconciliation",
                Reason: Limit(details),
                QuarantineReason: reason,
                AuditAction: PersistentInventoryAuditAction.Quarantined),
            _shutdown.Token);
        if (!result.IsSuccess || result.Header == null)
            throw new InvalidOperationException($"Bound quarantine failed: {result.Status}.");

        _manager.UpdateFromMutation(userId, result.Header);
    }

    private async Task<PersistentInventoryAdminMutationResult> AdminTransitionAsync(
        NetUserId userId,
        PersistentInventorySnapshotState state,
        string actor,
        Guid? actorUserId,
        string reason,
        PersistentInventoryInvalidationReason invalidationReason,
        PersistentInventoryQuarantineReason quarantineReason,
        PersistentInventoryAuditAction auditAction,
        CancellationToken cancel)
    {
        var header = await _db.GetPersistentInventoryHeaderAsync(userId, cancel);
        if (header == null)
            return new PersistentInventoryAdminMutationResult(false, "Снимок аккаунта не найден.");

        var result = await _db.TransitionPersistentInventoryAsync(
            userId,
            new PersistentInventoryTransitionRequest(
                state,
                PersistentInventoryOperationId.New(),
                header.Revision,
                actor,
                actorUserId,
                Limit(reason),
                invalidationReason,
                QuarantineReason: quarantineReason,
                AuditAction: auditAction),
            cancel);
        return await CompleteAdminMutationAsync(userId, result, $"Снимок переведён в {state}.", cancel);
    }

    private async Task<PersistentInventoryAdminMutationResult> CompleteAdminMutationAsync(
        NetUserId userId,
        PersistentInventoryMutationResult result,
        string successMessage,
        CancellationToken cancel)
    {
        if (!result.IsSuccess || result.Header == null)
        {
            return new PersistentInventoryAdminMutationResult(
                false,
                $"Операция отклонена: {result.Status}.");
        }

        _manager.UpdateFromMutation(userId, result.Header);
        if (result.Header.State == PersistentInventorySnapshotState.Active)
        {
            var loaded = await _manager.LoadAsync(userId, cancel);
            if (loaded.Status != PersistentInventoryCacheStatus.Available ||
                loaded.StoredRevision?.Metadata.SnapshotId != result.Header.CurrentVerified?.SnapshotId)
            {
                Log.Error(
                    $"Persistent inventory admin mutation for {userId} selected snapshot durably, " +
                    "but the active payload could not be loaded into cache.");
                return new PersistentInventoryAdminMutationResult(
                    false,
                    "Snapshot выбран в БД, но payload не загрузился в серверный кэш; spawn заблокирован.");
            }
        }

        _adminLog.Add(
            LogType.Mind,
            LogImpact.High,
            $"Persistent inventory admin mutation for {userId}: state {result.Header.State}, " +
            $"revision {result.Header.Revision}, operation {result.Header.OperationId}.");
        return new PersistentInventoryAdminMutationResult(true, successMessage);
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

    private static PersistentInventoryQuarantineReason ClassifyQuarantine(string? error)
    {
        if (error?.Contains("SHA-256", StringComparison.OrdinalIgnoreCase) == true ||
            error?.Contains("hash", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PersistentInventoryQuarantineReason.HashMismatch;
        }

        if (error?.Contains("prototype", StringComparison.OrdinalIgnoreCase) == true)
            return PersistentInventoryQuarantineReason.MissingPrototype;
        return PersistentInventoryQuarantineReason.InvalidSchema;
    }

    private static string Limit(string? value)
    {
        value ??= "Unknown persistent inventory lifecycle error.";
        return value.Length <= 512 ? value : value[..512];
    }

    private readonly record struct PendingDisconnect(EntityUid Body, TimeSpan Deadline);

    private readonly record struct RevisionValidation(bool Success, string? Error)
    {
        public static RevisionValidation Ok()
        {
            return new RevisionValidation(true, null);
        }

        public static RevisionValidation Fail(string error)
        {
            return new RevisionValidation(false, error);
        }
    }
}
