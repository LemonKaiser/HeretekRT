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
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared._WH40K.Augments;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Prometheus;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
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
public sealed partial class PersistentInventoryRestoreSystem : EntitySystem
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

    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PersistentInventoryManager _manager = default!;
    [Dependency] private PersistentInventoryLifecycleSystem _lifecycle = default!;
    [Dependency] private PersistentInventorySnapshotSerializer _serializer = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedSubdermalImplantSystem _subdermal = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private PdaSystem _pda = default!;

    public bool TryReserveRestore(
        NetUserId userId,
        out PersistentInventoryRestoreReservation reservation)
    {
        reservation = default!;
        return !EntityManager.System<Content.Server._WH40K.Administration.Wh40kPlayerResetSystem>().IsResetting(userId) &&
               PersistentInventoryRollout.GetDecision(_configuration, userId) ==
                   PersistentInventoryRolloutDecision.Full &&
               _manager.TryReserveRestore(userId, out reservation);
    }

    public bool IsSpawnBlockedByDurableState(NetUserId userId)
    {
        // A snapshot from a previously enabled rollout must not prevent a normal
        // spawn when persistent inventory is currently disabled or the account is
        // outside the active rollout bucket.
        if (PersistentInventoryRollout.GetDecision(_configuration, userId) !=
            PersistentInventoryRolloutDecision.Full)
        {
            return false;
        }

        var status = _manager.Get(userId).Status;
        if (status is PersistentInventoryCacheStatus.Bound
            or PersistentInventoryCacheStatus.Staging
            or PersistentInventoryCacheStatus.Unavailable)
        {
            return true;
        }

        return false;
    }

    public void CancelRestore(PersistentInventoryRestoreReservation reservation)
    {
        _manager.ReleaseRestore(reservation);
    }

    public Task<PersistentInventoryRestoreOutcome> RestoreAndBindAsync(
        EntityUid body,
        PersistentInventoryRestoreReservation reservation,
        CancellationToken cancel = default)
    {
        var reset = EntityManager.System<Content.Server._WH40K.Administration.Wh40kPlayerResetSystem>();
        if (reset.IsResetting(reservation.UserId))
        {
            CancelRestore(reservation);
            return Task.FromResult(Outcome(PersistentInventoryRestoreStatus.RevisionConflict, "Player reset is in progress."));
        }
        return reset.RunPlayerOperationAsync(reservation.UserId, () => RestoreAndBindCoreAsync(body, reservation, cancel));
    }

    private async Task<PersistentInventoryRestoreOutcome> RestoreAndBindCoreAsync(
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
                var packed = PersistentInventoryPayloadCodec.PackAccountSnapshot(
                    preparation.Payload,
                    _serializer.GetConfiguredLimits(),
                    Math.Max(PersistentInventoryStoragePolicy.SnapshotBytes, reservation.StoredRevision.Metadata.CompressedBytes));
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

            // Pre-commerce snapshots have no item title. Derive one from durable snapshot identity rather than
            // minting a new title on every restore, so replaying the same legacy snapshot cannot resell its goods.
            foreach (var item in finalPreparation.Payload.Entities)
            {
                if (item.Components.Any(component => component.ComponentId == "MerchantItemToken") ||
                    !isolated.Entities.TryGetValue(item.EntityId, out var uid)) continue;
                EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>()
                    .AssignLegacyTitle(uid, $"{reservation.SnapshotId}:{item.EntityId}");
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
            string? placementError = null;
            if (!EntityManager.System<Content.Server._WH40K.Quests.Wh40kQuestSubmissionSystem>()
                    .WithReservationPlacement(isolated.Entities.Values, () => TryPlaceExact(body, isolated, out placementError)))
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
            var quests = EntityManager.System<Content.Server._WH40K.Quests.Wh40kQuestSessionSystem>();
            _ = quests.TrackWork(EntityManager.System<Content.Server._WH40K.Quests.Wh40kQuestSubmissionSystem>()
                .ReconcileAsync(reservation.UserId));
            _ = quests.TrackWork(EntityManager.System<Content.Server._WH40K.Merchant.MerchantSystem>()
                .ReconcileSalesAsync(reservation.UserId));
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
        if (restored.Chassis != null && !HasComp<Wh40kChassisComponent>(body))
        {
            error = "Сохранённые компоненты шасси требуют кибернетического тела.";
            return false;
        }

        if (roots.Select(root => (root.Root.Kind, root.Root.Name)).Distinct().Count() != roots.Length)
        {
            error = "Снимок содержит повторяющиеся точки размещения.";
            return false;
        }

        foreach (var (root, entity) in roots)
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
                    break;
                case PersistentInventoryRootKind.ChassisCell:
                    if (restored.Chassis?.CaptureCellSlot != true || !HasComp<PowerCellComponent>(entity))
                    {
                        error = "Сохранённая батарея несовместима с шасси.";
                        return false;
                    }

                    break;
                case PersistentInventoryRootKind.BodyPart:
                    if (!HasComp<BodyComponent>(body) || !HasComp<BodyPartComponent>(entity))
                    {
                        error = $"Тело не поддерживает установку кибернетической части {root.Name}.";
                        return false;
                    }

                    break;
                case PersistentInventoryRootKind.BodyOrgan:
                    if (!HasComp<BodyComponent>(body) ||
                        !HasComp<OrganComponent>(entity) ||
                        root.Name.LastIndexOf(':') <= 0)
                    {
                        error = $"Тело не поддерживает установку кибернетического органа {root.Name}.";
                        return false;
                    }

                    break;
                case PersistentInventoryRootKind.SubdermalImplant:
                    if (!HasComp<SubdermalImplantComponent>(entity))
                    {
                        error = $"Объект {root.Name} не является подкожным имплантом.";
                        return false;
                    }

                    break;
                default:
                    error = "Снимок содержит неизвестный тип точки размещения.";
                    return false;
            }
        }

        if (!TryPlaceBodyParts(body, roots, out error) ||
            !TryPlaceBodyOrgans(body, roots, out error) ||
            !TryPlaceChassisState(body, restored.Chassis, roots, out error) ||
            !TryPlaceSubdermalImplants(body, roots, out error))
        {
            return false;
        }

        foreach (var (root, _) in roots.Where(r => r.Root.Kind == PersistentInventoryRootKind.Hand))
        {
            if (!_hands.TryGetHand(body, root.Name, out var hand) || !hand.IsEmpty)
            {
                error = $"Рука {root.Name} отсутствует или занята.";
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

    private bool TryPlaceChassisState(
        EntityUid body,
        PersistentInventoryChassisState? chassis,
        (PersistentInventoryRoot Root, EntityUid Entity)[] roots,
        out string? error)
    {
        error = null;
        if (chassis == null)
            return true;

        foreach (var path in chassis.EmptyLimbSlots)
        {
            var separator = path.LastIndexOf('/');
            var parentPath = separator < 0 ? string.Empty : path[..separator];
            var slotName = path[(separator + 1)..];
            if (!TryResolveBodyPartByPath(body, parentPath, out var parent, out _) ||
                !_containers.TryGetContainer(parent, SharedBodySystem.GetPartSlotContainerId(slotName), out var container) ||
                container is not ContainerSlot { ContainedEntity: { } part })
                continue;

            if (!TryComp<BodyPartComponent>(part, out var limb) ||
                limb.PartType is not (BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot) ||
                !DetachAndDiscardPartSubtree(parent, slotName, part))
            {
                error = $"Не удалось восстановить пустое место для конечности {path}.";
                return false;
            }
        }

        if (chassis.CollectorRemoved)
        {
            foreach (var (uid, organ) in _body.GetBodyOrgans(body).ToArray())
            {
                if (HasComp<Wh40kRadCollectorComponent>(uid) && _body.RemoveOrgan(uid, organ))
                    Del(uid);
            }
        }

        if (!chassis.CaptureCellSlot)
            return true;

        var saved = roots.FirstOrDefault(root => root.Root.Kind == PersistentInventoryRootKind.ChassisCell);
        if (!TryComp<PowerCellSlotComponent>(body, out var slot) ||
            saved.Root != null && saved.Root.Name != slot.CellSlotId ||
            !_containers.TryGetContainer(body, slot.CellSlotId, out var batteryContainer) ||
            batteryContainer is not ContainerSlot batterySlot)
        {
            error = "У тела отсутствует подходящий слот батареи шасси.";
            return false;
        }

        if (batterySlot.ContainedEntity is { } starter)
        {
            if (!_containers.Remove(starter, batterySlot, force: true))
            {
                error = "Не удалось извлечь стартовую батарею шасси.";
                return false;
            }

            Del(starter);
        }

        if (saved.Root == null || _containers.Insert(saved.Entity, batterySlot, force: true))
            return true;

        error = "Не удалось установить сохранённую батарею шасси.";
        return false;
    }

    private bool TryPlaceBodyParts(
        EntityUid body,
        (PersistentInventoryRoot Root, EntityUid Entity)[] roots,
        out string? error)
    {
        error = null;
        var partRoots = roots
            .Where(r => r.Root.Kind == PersistentInventoryRootKind.BodyPart)
            .OrderBy(r => r.Root.Name.Count(c => c == '/'))
            .ThenBy(r => r.Root.Name, StringComparer.Ordinal)
            .ToArray();

        if (partRoots.Length == 0)
            return true;

        var attachedAny = false;
        foreach (var (root, partUid) in partRoots)
        {
            var segments = root.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                error = $"Некорректный путь кибернетической конечности: {root.Name}.";
                return false;
            }

            var parentPath = segments.Length > 1
                ? string.Join('/', segments[..^1])
                : "torso";
            var slotName = segments[^1];

            if (!TryResolveBodyPartByPath(body, parentPath, out var parentUid, out var parentComp) ||
                !TryComp<BodyPartComponent>(partUid, out var newPartComp))
            {
                error = $"Не удалось найти родительскую часть {parentPath} для {root.Name}.";
                return false;
            }

            if (!_body.TryCreatePartSlot(parentUid, slotName, newPartComp.PartType, out _, parentComp))
            {
                error = $"Не удалось подготовить слот {slotName} для {root.Name}.";
                return false;
            }

            if (_containers.TryGetContainer(parentUid, SharedBodySystem.GetPartSlotContainerId(slotName), out var slotContainer) &&
                slotContainer is ContainerSlot existingSlot &&
                existingSlot.ContainedEntity is { } existingPart)
            {
                if (!DetachAndDiscardPartSubtree(parentUid, slotName, existingPart))
                {
                    error = $"Не удалось освободить место для конечности {root.Name}.";
                    return false;
                }
            }

            _transform.SetCoordinates(partUid, Transform(body).Coordinates);
            if (!_body.AttachPart(parentUid, slotName, partUid, parentComp, newPartComp))
            {
                error = $"Не удалось установить сохранённую конечность {root.Name}.";
                return false;
            }

            attachedAny = true;
        }

        if (attachedAny)
            _body.RefreshBaseLayersFromParts(body);

        return true;
    }

    private bool TryPlaceBodyOrgans(
        EntityUid body,
        (PersistentInventoryRoot Root, EntityUid Entity)[] roots,
        out string? error)
    {
        error = null;
        var organRoots = roots
            .Where(r => r.Root.Kind == PersistentInventoryRootKind.BodyOrgan)
            .OrderBy(r => r.Root.Name, StringComparer.Ordinal)
            .ToArray();

        if (organRoots.Length == 0)
            return true;

        EntityUid? headWithCustomOptics = null;
        var installedLeftEye = false;
        var installedRightEye = false;

        foreach (var (root, organUid) in organRoots)
        {
            var separator = root.Name.LastIndexOf(':');
            if (separator <= 0 || separator >= root.Name.Length - 1)
            {
                error = $"Некорректный путь импланта органа: {root.Name}.";
                return false;
            }

            var partPath = root.Name[..separator];
            var organSlotId = root.Name[(separator + 1)..];
            if (!TryResolveBodyPartByPath(body, partPath, out var partUid, out var partComp))
            {
                error = $"Не удалось найти часть тела {partPath} для органа {organSlotId}.";
                return false;
            }

            if (TryComp<Wh40kImplantSpecComponent>(organUid, out var spec) &&
                !Wh40kAugmentCatalog.CanHost(EntityManager, body, partUid, spec))
            {
                error = $"Имплант {root.Name} несовместим с телом персонажа.";
                return false;
            }

            if (Wh40kAugmentCatalog.IsOpticalSlot(organSlotId))
            {
                headWithCustomOptics = partUid;
                if (organSlotId == Wh40kAugmentCatalog.LeftEye)
                    installedLeftEye = true;
                else if (organSlotId == Wh40kAugmentCatalog.RightEye)
                    installedRightEye = true;

                foreach (var (existingOrgan, existingComp) in _body.GetPartOrgans(partUid, partComp).ToArray())
                {
                    if (existingComp.SlotId == organSlotId ||
                        (organSlotId == Wh40kAugmentCatalog.Eyes && Wh40kAugmentCatalog.IsOpticalSlot(existingComp.SlotId)))
                    {
                        if (_body.RemoveOrgan(existingOrgan, existingComp))
                            Del(existingOrgan);
                    }
                }
            }
            else
            {
                foreach (var (existingOrgan, existingComp) in _body.GetPartOrgans(partUid, partComp).ToArray())
                {
                    if (existingComp.SlotId == organSlotId && _body.RemoveOrgan(existingOrgan, existingComp))
                        Del(existingOrgan);
                }
            }

            if (!_body.TryCreateOrganSlot(partUid, organSlotId, out _, partComp))
            {
                error = $"Не удалось создать слот органа {organSlotId} в {partPath}.";
                return false;
            }

            _transform.SetCoordinates(organUid, Transform(body).Coordinates);
            if (!_body.InsertOrgan(partUid, organUid, organSlotId, partComp))
            {
                error = $"Не удалось установить сохранённый орган {root.Name}.";
                return false;
            }
        }

        // Remove the default paired organic eyes when both left and right cybernetic eyes were restored.
        if (headWithCustomOptics is { } headUid &&
            installedLeftEye &&
            installedRightEye &&
            TryComp<BodyPartComponent>(headUid, out var headComp))
        {
            foreach (var (existingOrgan, existingComp) in _body.GetPartOrgans(headUid, headComp).ToArray())
            {
                if (existingComp.SlotId == Wh40kAugmentCatalog.Eyes && _body.RemoveOrgan(existingOrgan, existingComp))
                    Del(existingOrgan);
            }
        }

        return true;
    }

    private bool TryPlaceSubdermalImplants(
        EntityUid body,
        (PersistentInventoryRoot Root, EntityUid Entity)[] roots,
        out string? error)
    {
        error = null;
        var implantRoots = roots
            .Where(r => r.Root.Kind == PersistentInventoryRootKind.SubdermalImplant)
            .OrderBy(r => r.Root.Name, StringComparer.Ordinal)
            .ToArray();

        if (implantRoots.Length == 0)
            return true;

        var defaults = TryComp<ImplantedComponent>(body, out var implanted)
            ? implanted.ImplantContainer.ContainedEntities.ToList()
            : new List<EntityUid>();

        foreach (var (_, implantUid) in implantRoots)
        {
            if (!TryComp<SubdermalImplantComponent>(implantUid, out var implantComp))
            {
                error = "Сохранённая сущность не содержит SubdermalImplantComponent.";
                return false;
            }

            var original = defaults.FindIndex(uid => Prototype(uid) == Prototype(implantUid));
            if (original >= 0)
            {
                _subdermal.ForceRemove(body, defaults[original]);
                defaults.RemoveAt(original);
            }

            _transform.SetCoordinates(implantUid, Transform(body).Coordinates);
            _subdermal.ForceImplant(body, implantUid, implantComp);
            if (!TryComp<ImplantedComponent>(body, out var restored) || !restored.ImplantContainer.Contains(implantUid))
            {
                error = $"Не удалось восстановить подкожный имплант {ToPrettyString(implantUid)}.";
                return false;
            }
        }

        return true;
    }

    private bool TryResolveBodyPartByPath(
        EntityUid body,
        string path,
        out EntityUid partUid,
        out BodyPartComponent partComp)
    {
        partUid = default;
        partComp = default!;

        if (!TryComp<BodyComponent>(body, out var bodyComp) ||
            bodyComp.RootContainer.ContainedEntity is not { } rootUid ||
            !TryComp<BodyPartComponent>(rootUid, out var rootComp))
        {
            return false;
        }

        if (string.IsNullOrEmpty(path) || string.Equals(path, "torso", StringComparison.Ordinal))
        {
            partUid = rootUid;
            partComp = rootComp;
            return true;
        }

        var currentUid = rootUid;
        var currentComp = rootComp;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!_containers.TryGetContainer(currentUid, SharedBodySystem.GetPartSlotContainerId(segment), out var container) ||
                container is not ContainerSlot slot ||
                slot.ContainedEntity is not { } nextUid ||
                !TryComp<BodyPartComponent>(nextUid, out var nextComp))
            {
                return false;
            }

            currentUid = nextUid;
            currentComp = nextComp;
        }

        partUid = currentUid;
        partComp = currentComp;
        return true;
    }

    private bool DetachAndDiscardPartSubtree(EntityUid parentUid, string slotName, EntityUid partUid)
    {
        var subtree = _body.GetBodyPartChildren(partUid).Select(entry => entry.Id).ToArray();
        if (!_body.DetachPart(parentUid, slotName, partUid))
            return false;
        foreach (var child in subtree)
        {
            if (Exists(child) && !TerminatingOrDeleted(child))
                Del(child);
        }

        if (Exists(partUid) && !TerminatingOrDeleted(partUid))
            Del(partUid);

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
