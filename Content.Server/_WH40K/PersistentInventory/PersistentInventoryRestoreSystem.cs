using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.PDA;
using Content.Server._WH40K.PersistentInventory.Serialization;
using Content.Shared.Access.Components;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Prometheus;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._WH40K.PersistentInventory;

public enum PersistentInventoryRestoreStatus
{
    Success = 0,
    Disabled = 1,
    IncompatibleBody = 2,
    InvalidSnapshot = 3,
    DatabaseFailure = 4,
    RevisionConflict = 5,
}

public sealed record PersistentInventoryRestoreOutcome(
    PersistentInventoryRestoreStatus Status,
    string Message)
{
    public bool IsSuccess => Status == PersistentInventoryRestoreStatus.Success;

    public bool MayFallbackToDefault =>
        Status is PersistentInventoryRestoreStatus.IncompatibleBody
            or PersistentInventoryRestoreStatus.InvalidSnapshot;
}

/// <summary>
/// Restores the graph in isolation, atomically places it on a body that has not been issued yet,
/// and only then transitions the durable snapshot from Active to Bound.
/// </summary>
public sealed class PersistentInventoryRestoreSystem : EntitySystem
{
    private static readonly Counter RestoreOutcomes = Metrics.CreateCounter(
        "wh40k_persistent_inventory_restore_total",
        "Persistent inventory restore results.",
        "result");

    private static readonly Histogram RestoreDuration = Metrics.CreateHistogram(
        "wh40k_persistent_inventory_restore_duration_seconds",
        "Persistent inventory restore duration in seconds.",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 14),
        });

    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly PersistentInventoryManager _manager = default!;
    [Dependency] private readonly PersistentInventoryLifecycleSystem _lifecycle = default!;
    [Dependency] private readonly PersistentInventorySnapshotSerializer _serializer = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly PdaSystem _pda = default!;

    public bool TryReserveRestore(
        NetUserId userId,
        out PersistentInventoryRestoreReservation reservation)
    {
        reservation = default!;
        return PersistentInventoryRollout.GetDecision(_configuration, userId) ==
                   PersistentInventoryRolloutDecision.Full &&
               _manager.TryReserveRestore(userId, out reservation);
    }

    public bool IsSpawnBlockedByDurableState(NetUserId userId)
    {
        var status = _manager.Get(userId).Status;
        if (status is PersistentInventoryCacheStatus.Bound
            or PersistentInventoryCacheStatus.Staging
            or PersistentInventoryCacheStatus.Unavailable)
        {
            return true;
        }

        return status == PersistentInventoryCacheStatus.Available &&
               PersistentInventoryRollout.GetDecision(_configuration, userId) !=
               PersistentInventoryRolloutDecision.Full;
    }

    public void CancelRestore(PersistentInventoryRestoreReservation reservation)
    {
        _manager.ReleaseRestore(reservation);
    }

    public async Task<PersistentInventoryRestoreOutcome> RestoreAndBindAsync(
        EntityUid body,
        PersistentInventoryRestoreReservation reservation,
        CancellationToken cancel = default)
    {
        var stopwatch = Stopwatch.StartNew();
        PersistentInventoryRestoreResult? isolated = null;
        PersistentInventorySnapshotHeader? durableHeader = null;
        var locksApplied = false;
        try
        {
            cancel.ThrowIfCancellationRequested();
            if (PersistentInventoryRollout.GetDecision(_configuration, reservation.UserId) !=
                PersistentInventoryRolloutDecision.Full)
            {
                _manager.ReleaseRestore(reservation);
                return Outcome(
                    PersistentInventoryRestoreStatus.Disabled,
                    "Persistent inventory или rollout был отключён до восстановления.");
            }

            PersistentInventoryPayload payload = default!;
            var appliedMigrationActions = new List<string>();
            const int maximumRepairPasses = 3;
            for (var repairPass = 0; repairPass < maximumRepairPasses; repairPass++)
            {
                PersistentInventoryRestorePreparation preparation;
                try
                {
                    payload = PersistentInventoryPayloadCodec.Unpack(
                        reservation.StoredRevision.Payload,
                        reservation.StoredRevision.Metadata.PayloadSha256,
                        _serializer.GetConfiguredLimits());
                    preparation = _serializer.PrepareForRestore(payload);
                }
                catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
                {
                    return await QuarantineInvalidAsync(
                        reservation,
                        ClassifyQuarantine(exception.Message),
                        exception.Message,
                        cancel);
                }

                payload = preparation.Payload;
                appliedMigrationActions.AddRange(preparation.MigrationActions);
                if (!preparation.RequiresDatabaseRewrite)
                    break;

                var repairedSnapshotId = PersistentInventorySnapshotId.New();
                var packed = PersistentInventoryPayloadCodec.Pack(
                    preparation.Payload,
                    _serializer.GetConfiguredLimits());
                PersistentInventoryMutationResult repair;
                try
                {
                    repair = await _db.RepairPersistentInventoryAsync(
                        reservation.UserId,
                        new PersistentInventoryRepairRequest(
                            reservation.SnapshotId,
                            repairedSnapshotId,
                            reservation.OperationId,
                            reservation.ExpectedRevision,
                            preparation.Payload.SchemaVersion,
                            preparation.Payload.PolicyId,
                            packed.Data,
                            packed.Sha256,
                            packed.RootCount,
                            packed.EntityCount,
                            packed.UncompressedBytes,
                            "persistent-restore-repair",
                            Reason: BuildRepairReason(
                                preparation.RemovedPrototypeIds,
                                preparation.MigrationActions)),
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _manager.ReleaseRestore(reservation);
                    Log.Error(
                        $"Persistent inventory repair failed for {reservation.UserId}: " +
                        $"{exception.GetType().Name}.");
                    return Outcome(
                        PersistentInventoryRestoreStatus.DatabaseFailure,
                        "База данных временно не подтвердила очистку устаревших предметов.");
                }

                if (!repair.IsSuccess ||
                    repair.Header is not
                    {
                        State: PersistentInventorySnapshotState.Active,
                        CurrentVerified: not null,
                    } repairedHeader ||
                    repairedHeader.CurrentVerified.SnapshotId != repairedSnapshotId)
                {
                    _manager.ReleaseRestore(reservation);
                    if (repair.Header != null)
                        _manager.UpdateFromMutation(reservation.UserId, repair.Header);
                    else
                        _manager.Remove(reservation.UserId);

                    Log.Warning(
                        $"Persistent inventory repair rejected for {reservation.UserId}: {repair.Status}.");
                    return Outcome(
                        PersistentInventoryRestoreStatus.RevisionConflict,
                        "Снимок уже изменён другим процессом во время очистки.");
                }

                var oldSnapshotId = reservation.SnapshotId;
                _manager.ReleaseRestore(reservation);
                _manager.UpdateFromMutation(reservation.UserId, repairedHeader);
                var reloaded = await _manager.LoadAsync(reservation.UserId, CancellationToken.None);
                if (reloaded.Status != PersistentInventoryCacheStatus.Available ||
                    reloaded.StoredRevision?.Metadata.SnapshotId != repairedSnapshotId ||
                    !_manager.TryReserveRestore(reservation.UserId, out var repairedReservation))
                {
                    Log.Error(
                        $"Persistent inventory repair reload failed for {reservation.UserId}: " +
                        $"snapshot {repairedSnapshotId}, cache {reloaded.Status}.");
                    return Outcome(
                        PersistentInventoryRestoreStatus.DatabaseFailure,
                        "Очищенный снимок не удалось повторно загрузить из базы данных.");
                }

                reservation = repairedReservation;
                _adminLog.Add(
                    LogType.Mind,
                    LogImpact.High,
                    $"Persistent inventory restore repaired {reservation.UserId}: " +
                    $"old snapshot {oldSnapshotId}, new snapshot {repairedSnapshotId}, " +
                    $"removed prototypes [{string.Join(", ", preparation.RemovedPrototypeIds)}].");
            }

            var finalPreparation = _serializer.PrepareForRestore(payload);
            if (finalPreparation.RequiresDatabaseRewrite)
            {
                return await QuarantineInvalidAsync(
                    reservation,
                    PersistentInventoryQuarantineReason.InvalidSchema,
                    "Persistent inventory migrations did not converge after database repair.",
                    cancel);
            }

            isolated = _serializer.RestoreIsolated(finalPreparation.Payload);
            if (!isolated.Success)
            {
                return await QuarantineInvalidAsync(
                    reservation,
                    ClassifyQuarantine(isolated.Error),
                    isolated.Error ?? "Isolated restore validation failed.",
                    cancel);
            }

            if (appliedMigrationActions.Count > 0)
            {
                isolated = isolated with
                {
                    MigrationActions = appliedMigrationActions
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                };
            }

            PersistentInventoryMetrics.ObserveMigrationActions(isolated.MigrationActions);
            if (!TryPlaceExact(body, isolated, out var placementError))
            {
                _serializer.DeleteIsolated(isolated);
                isolated = null;
                _manager.ReleaseRestore(reservation);
                return Outcome(
                    PersistentInventoryRestoreStatus.IncompatibleBody,
                    placementError ?? "Снимок несовместим с выбранным телом.");
            }

            RebindPdas(body, isolated);
            AddOperationLocks(body, isolated, reservation);
            locksApplied = true;

            if (!_manager.IsRestoreReservationCurrent(reservation) ||
                !_players.TryGetSessionById(reservation.UserId, out var session) ||
                session.Status == SessionStatus.Disconnected)
            {
                RemoveOperationLocks(body, isolated, reservation);
                locksApplied = false;
                _serializer.DeleteIsolated(isolated);
                isolated = null;
                _manager.ReleaseRestore(reservation);
                return Outcome(
                    PersistentInventoryRestoreStatus.DatabaseFailure,
                    "Игрок отключился или запуск был отменён до durable bind.");
            }

            PersistentInventoryMutationResult binding;
            var bindStopwatch = Stopwatch.StartNew();
            try
            {
                binding = await _db.TransitionPersistentInventoryAsync(
                    reservation.UserId,
                    new PersistentInventoryTransitionRequest(
                        PersistentInventorySnapshotState.Bound,
                        reservation.OperationId,
                        reservation.ExpectedRevision,
                        "persistent-restore",
                        Reason: BuildBindReason(isolated.MigrationActions),
                        ServerEpoch: _manager.ServerEpoch,
                        LifeId: reservation.LifeId),
                    CancellationToken.None);
                PersistentInventoryMetrics.PhaseDuration
                    .WithLabels("restore", "database_bind")
                    .Observe(bindStopwatch.Elapsed.TotalSeconds);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                PersistentInventoryMetrics.PhaseDuration
                    .WithLabels("restore", "database_bind")
                    .Observe(bindStopwatch.Elapsed.TotalSeconds);
                RemoveOperationLocks(body, isolated, reservation);
                locksApplied = false;
                _serializer.DeleteIsolated(isolated);
                isolated = null;
                _manager.ReleaseRestore(reservation);
                Log.Error(
                    $"Persistent inventory bind failed for {reservation.UserId}: {exception.GetType().Name}.");
                return Outcome(
                    PersistentInventoryRestoreStatus.DatabaseFailure,
                    "База данных временно не подтвердила восстановление.");
            }

            if (!binding.IsSuccess ||
                binding.Header is not
                {
                    State: PersistentInventorySnapshotState.Bound,
                    ServerEpoch: not null,
                    LifeId: not null,
                } boundHeader ||
                boundHeader.CurrentVerified?.SnapshotId != reservation.SnapshotId ||
                boundHeader.ServerEpoch != _manager.ServerEpoch ||
                boundHeader.LifeId != reservation.LifeId)
            {
                RemoveOperationLocks(body, isolated, reservation);
                locksApplied = false;
                _serializer.DeleteIsolated(isolated);
                isolated = null;
                _manager.ReleaseRestore(reservation);
                if (binding.Header != null)
                    _manager.UpdateFromMutation(reservation.UserId, binding.Header);
                else
                    _manager.Remove(reservation.UserId);

                Log.Warning(
                    $"Persistent inventory bind rejected for {reservation.UserId}: {binding.Status}.");
                return Outcome(
                    PersistentInventoryRestoreStatus.RevisionConflict,
                    "Снимок уже изменён или восстановлен другим процессом.");
            }

            durableHeader = boundHeader;
            _lifecycle.BindBody(
                body,
                reservation.UserId,
                reservation.SnapshotId,
                reservation.LifeId);
            RemoveOperationLocks(body, isolated, reservation);
            locksApplied = false;
            _manager.CompleteRestore(reservation, boundHeader);
            _adminLog.Add(
                LogType.Mind,
                LogImpact.High,
                $"Persistent inventory restore succeeded for {reservation.UserId}: " +
                $"snapshot {reservation.SnapshotId}, operation {reservation.OperationId}, " +
                $"life {reservation.LifeId}, entities {isolated.Entities.Count}, " +
                $"duration {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
            return Outcome(
                PersistentInventoryRestoreStatus.Success,
                "Persistent inventory восстановлен.");
        }
        catch (OperationCanceledException)
        {
            if (isolated != null)
            {
                if (locksApplied)
                    RemoveOperationLocks(body, isolated, reservation);
                _serializer.DeleteIsolated(isolated);
            }

            _manager.ReleaseRestore(reservation);
            throw;
        }
        catch (Exception exception)
        {
            if (durableHeader != null)
            {
                if (isolated != null && locksApplied)
                    RemoveOperationLocks(body, isolated, reservation);
                _manager.CompleteRestore(reservation, durableHeader);
                Log.Error(
                    $"Persistent inventory restore completed durably for {reservation.UserId}, " +
                    $"but post-bind handling failed: {exception}.");
                return Outcome(
                    PersistentInventoryRestoreStatus.Success,
                    "Persistent inventory восстановлен с ошибкой постобработки.");
            }

            if (isolated != null)
            {
                if (locksApplied)
                    RemoveOperationLocks(body, isolated, reservation);
                _serializer.DeleteIsolated(isolated);
            }

            _manager.ReleaseRestore(reservation);
            Log.Error(
                $"Persistent inventory restore failed unexpectedly for {reservation.UserId}: {exception}.");
            return Outcome(
                PersistentInventoryRestoreStatus.DatabaseFailure,
                "Восстановление прервано до подтверждения выдачи.");
        }
        finally
        {
            RestoreDuration.Observe(stopwatch.Elapsed.TotalSeconds);
        }
    }

    internal bool TryPlaceExact(
        EntityUid body,
        PersistentInventoryRestoreResult restored,
        out string? error)
    {
        error = null;
        if (TerminatingOrDeleted(body))
        {
            error = "Тело было удалено до размещения снимка.";
            return false;
        }

        var roots = restored.Roots.ToArray();
        if (roots.Select(root => (root.Root.Kind, root.Root.Name)).Distinct().Count() != roots.Length)
        {
            error = "Снимок содержит повторяющиеся точки размещения.";
            return false;
        }

        foreach (var (root, _) in roots)
        {
            switch (root.Kind)
            {
                case PersistentInventoryRootKind.InventorySlot:
                    if (!_inventory.TryGetSlot(body, root.Name, out _) ||
                        _inventory.TryGetSlotEntity(body, root.Name, out _))
                    {
                        error = $"Слот {root.Name} отсутствует или занят.";
                        return false;
                    }

                    break;
                case PersistentInventoryRootKind.Hand:
                    if (!_hands.TryGetHand(body, root.Name, out var hand) || !hand.IsEmpty)
                    {
                        error = $"Рука {root.Name} отсутствует или занята.";
                        return false;
                    }

                    break;
                default:
                    error = "Снимок содержит неизвестный тип точки размещения.";
                    return false;
            }
        }

        var pendingSlots = roots
            .Where(root => root.Root.Kind == PersistentInventoryRootKind.InventorySlot)
            .OrderBy(root => root.Root.Name, StringComparer.Ordinal)
            .ToList();
        while (pendingSlots.Count > 0)
        {
            var placed = false;
            for (var index = pendingSlots.Count - 1; index >= 0; index--)
            {
                var entry = pendingSlots[index];
                _transform.SetCoordinates(entry.Entity, Transform(body).Coordinates);
                if (!_inventory.TryEquip(
                        body,
                        entry.Entity,
                        entry.Root.Name,
                        silent: true,
                        force: false))
                {
                    continue;
                }

                pendingSlots.RemoveAt(index);
                placed = true;
            }

            if (!placed)
            {
                error = "Сохранённая экипировка несовместима со слотами выбранного вида.";
                return false;
            }
        }

        foreach (var entry in roots
                     .Where(root => root.Root.Kind == PersistentInventoryRootKind.Hand)
                     .OrderBy(root => root.Root.Name, StringComparer.Ordinal))
        {
            _transform.SetCoordinates(entry.Entity, Transform(body).Coordinates);
            if (!_hands.TryPickup(
                    body,
                    entry.Entity,
                    entry.Root.Name,
                    checkActionBlocker: false,
                    animate: false))
            {
                error = "Сохранённый предмет несовместим с указанной рукой.";
                return false;
            }
        }

        return true;
    }

    private void RebindPdas(EntityUid body, PersistentInventoryRestoreResult restored)
    {
        foreach (var uid in restored.Entities.Values)
        {
            if (!TryComp(uid, out PdaComponent? pda))
                continue;

            var ownerName = pda.OwnerName;
            if (TryComp(pda.ContainedId, out IdCardComponent? idCard) &&
                !string.IsNullOrWhiteSpace(idCard.FullName))
            {
                ownerName = idCard.FullName;
            }

            _pda.SetOwner(uid, pda, body, ownerName ?? Name(body));
        }
    }

    private async Task<PersistentInventoryRestoreOutcome> QuarantineInvalidAsync(
        PersistentInventoryRestoreReservation reservation,
        PersistentInventoryQuarantineReason reason,
        string details,
        CancellationToken cancel)
    {
        PersistentInventoryMutationResult result;
        try
        {
            result = await _db.TransitionPersistentInventoryAsync(
                reservation.UserId,
                new PersistentInventoryTransitionRequest(
                    PersistentInventorySnapshotState.Quarantined,
                    reservation.OperationId,
                    reservation.ExpectedRevision,
                    "persistent-restore",
                    Reason: Limit(details),
                    QuarantineReason: reason),
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _manager.ReleaseRestore(reservation);
            Log.Error(
                $"Persistent inventory quarantine failed for {reservation.UserId}: " +
                $"{exception.GetType().Name}.");
            return Outcome(
                PersistentInventoryRestoreStatus.DatabaseFailure,
                "Невалидный снимок не удалось безопасно поместить в карантин.");
        }

        _manager.ReleaseRestore(reservation);
        if (!result.IsSuccess || result.Header == null)
        {
            _manager.Remove(reservation.UserId);
            return Outcome(
                PersistentInventoryRestoreStatus.DatabaseFailure,
                "Невалидный снимок не удалось безопасно поместить в карантин.");
        }

        _manager.UpdateFromMutation(reservation.UserId, result.Header);
        _adminLog.Add(
            LogType.Mind,
            LogImpact.High,
            $"Persistent inventory restore quarantined {reservation.UserId}: " +
            $"snapshot {reservation.SnapshotId}, operation {reservation.OperationId}, reason {reason}.");
        return Outcome(
            PersistentInventoryRestoreStatus.InvalidSnapshot,
            "Снимок инвентаря признан непригодным и помещён в карантин.");
    }

    private void AddOperationLocks(
        EntityUid body,
        PersistentInventoryRestoreResult restored,
        PersistentInventoryRestoreReservation reservation)
    {
        foreach (var uid in restored.Entities.Values.Append(body).Distinct())
        {
            var component = EnsureComp<PersistentInventoryOperationComponent>(uid);
            component.UserId = reservation.UserId;
            component.OperationId = reservation.OperationId;
        }
    }

    private void RemoveOperationLocks(
        EntityUid body,
        PersistentInventoryRestoreResult restored,
        PersistentInventoryRestoreReservation reservation)
    {
        foreach (var uid in restored.Entities.Values.Append(body).Distinct())
        {
            if (TerminatingOrDeleted(uid) ||
                !TryComp(uid, out PersistentInventoryOperationComponent? component) ||
                component.UserId != reservation.UserId ||
                component.OperationId != reservation.OperationId)
            {
                continue;
            }

            RemComp<PersistentInventoryOperationComponent>(uid);
        }
    }

    private static PersistentInventoryQuarantineReason ClassifyQuarantine(string? error)
    {
        if (error == null)
            return PersistentInventoryQuarantineReason.InvalidSchema;
        if (error.Contains("SHA-256", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("hash", StringComparison.OrdinalIgnoreCase))
        {
            return PersistentInventoryQuarantineReason.HashMismatch;
        }

        if (error.Contains("size", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return PersistentInventoryQuarantineReason.SizeLimit;
        }

        if (error.Contains("prototype", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("policy", StringComparison.OrdinalIgnoreCase))
        {
            return PersistentInventoryQuarantineReason.MissingPrototype;
        }

        return PersistentInventoryQuarantineReason.InvalidSchema;
    }

    private static string Limit(string value)
    {
        return value.Length <= 512 ? value : value[..512];
    }

    private static string BuildBindReason(IReadOnlyList<string>? migrationActions)
    {
        if (migrationActions == null || migrationActions.Count == 0)
            return "Snapshot graph placed on an unreleased spawn body.";

        return Limit(
            "Snapshot graph placed after migrations: " +
            string.Join(", ", migrationActions));
    }

    private static string BuildRepairReason(
        IReadOnlyList<string> removedPrototypeIds,
        IReadOnlyList<string> migrationActions)
    {
        var removed = removedPrototypeIds.Count == 0
            ? "none"
            : string.Join(", ", removedPrototypeIds);
        return Limit(
            $"Rewrote snapshot before restore; obsolete prototypes: {removed}; " +
            $"actions: {string.Join(", ", migrationActions)}.");
    }

    private static PersistentInventoryRestoreOutcome Outcome(
        PersistentInventoryRestoreStatus status,
        string message)
    {
        RestoreOutcomes.WithLabels(status.ToString()).Inc();
        if (status == PersistentInventoryRestoreStatus.InvalidSnapshot)
            PersistentInventoryMetrics.ValidationFailures.WithLabels("restore", "snapshot").Inc();
        if (status == PersistentInventoryRestoreStatus.DatabaseFailure)
            PersistentInventoryMetrics.DatabaseOperations.WithLabels("restore", "failure").Inc();
        return new PersistentInventoryRestoreOutcome(status, message);
    }
}
