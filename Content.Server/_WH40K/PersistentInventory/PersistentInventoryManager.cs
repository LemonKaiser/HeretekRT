using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Robust.Shared.Network;

namespace Content.Server._WH40K.PersistentInventory;

public enum PersistentInventoryCacheStatus
{
    None = 0,
    Available = 1,
    Bound = 2,
    Staging = 3,
    Unavailable = 4,
    Quarantined = 5,
}

public sealed record PersistentInventoryCacheEntry(
    PersistentInventoryCacheStatus Status,
    PersistentInventorySnapshotHeader? Header,
    string? ErrorCode = null,
    PersistentInventoryStoredRevision? StoredRevision = null)
{
    public static readonly PersistentInventoryCacheEntry None =
        new(PersistentInventoryCacheStatus.None, null);
}

public sealed record PersistentInventoryRestoreReservation(
    NetUserId UserId,
    PersistentInventorySnapshotId SnapshotId,
    PersistentInventoryRevision ExpectedRevision,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryLifeId LifeId,
    PersistentInventoryStoredRevision StoredRevision);

public sealed record PersistentInventoryLifeLossReservation(
    NetUserId UserId,
    PersistentInventorySnapshotId SnapshotId,
    PersistentInventoryRevision ExpectedRevision,
    PersistentInventoryOperationId OperationId,
    PersistentInventoryLifeId LifeId);

/// <summary>
/// Loads only the server-side account header before spawn authorization and does not access ECS.
/// </summary>
public sealed class PersistentInventoryManager : IPostInjectInit
{
    [Dependency] private ILogManager _logManager = default!;

    private readonly IServerDbManager _db;
    private readonly ConcurrentDictionary<NetUserId, PersistentInventoryCacheEntry> _cache = new();
    private readonly ConcurrentDictionary<NetUserId, LoadState> _loadStates = new();
    private readonly ConcurrentDictionary<NetUserId, PersistentInventoryRestoreReservation> _restoreReservations = new();
    private readonly ConcurrentDictionary<NetUserId, PersistentInventoryLifeLossReservation> _lifeLossReservations = new();
    private readonly ConcurrentDictionary<NetUserId, byte> _deferredRemovals = new();

    private ISawmill? _sawmill;

    public PersistentInventoryServerEpoch ServerEpoch { get; } = PersistentInventoryServerEpoch.New();

    public PersistentInventoryManager(IServerDbManager db)
    {
        _db = db;
    }

    public async Task<PersistentInventoryCacheEntry> LoadAsync(
        NetUserId userId,
        CancellationToken cancel = default)
    {
        var loadState = _loadStates.GetOrAdd(userId, _ => new LoadState());
        await loadState.Gate.WaitAsync(cancel);

        try
        {
            var generation = Volatile.Read(ref loadState.Generation);
            PersistentInventoryCacheEntry entry;
            try
            {
                var header = await _db.GetPersistentInventoryHeaderAsync(userId, cancel);
                entry = FromHeader(header);
                if (HeaderRequiresStoredRevision(header))
                {
                    var metadata = header?.CurrentVerified;
                    if (metadata == null)
                    {
                        entry = Unavailable(header, "DatabaseInvariant");
                    }
                    else
                    {
                        var stored = await _db.GetPersistentInventoryRevisionAsync(
                            userId,
                            metadata.SnapshotId,
                            cancel);
                        entry = IsMatchingRevision(userId, metadata, stored)
                            ? new PersistentInventoryCacheEntry(
                                PersistentInventoryCacheStatus.Available,
                                header,
                                StoredRevision: stored)
                            : Unavailable(header, "DatabaseInvariant");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _sawmill?.Error(
                    $"Не удалось загрузить header persistent inventory для {userId}: {exception.GetType().Name}.");
                entry = new PersistentInventoryCacheEntry(
                    PersistentInventoryCacheStatus.Unavailable,
                    null,
                    exception.GetType().Name);
            }

            if (Volatile.Read(ref loadState.Retired) == 0 &&
                Volatile.Read(ref loadState.Generation) == generation &&
                _loadStates.TryGetValue(userId, out var currentLoadState) &&
                ReferenceEquals(currentLoadState, loadState))
            {
                if (entry.Status == PersistentInventoryCacheStatus.Unavailable &&
                    _cache.TryGetValue(userId, out var current) &&
                    current.Status == PersistentInventoryCacheStatus.Bound)
                {
                    return current;
                }

                _cache[userId] = entry;
            }

            return entry;
        }
        finally
        {
            loadState.Gate.Release();
        }
    }

    public void SetTransientNone(NetUserId userId)
    {
        SupersedeLoads(userId);
        _cache[userId] = PersistentInventoryCacheEntry.None;
    }

    public PersistentInventoryCacheEntry Get(NetUserId userId)
    {
        return _cache.GetValueOrDefault(
            userId,
            new PersistentInventoryCacheEntry(
                PersistentInventoryCacheStatus.Unavailable,
                null,
                "NotLoaded"));
    }

    public bool TryReserveRestore(
        NetUserId userId,
        out PersistentInventoryRestoreReservation reservation)
    {
        reservation = default!;
        if (_lifeLossReservations.ContainsKey(userId))
            return false;

        if (!_cache.TryGetValue(userId, out var entry) ||
            entry.Status != PersistentInventoryCacheStatus.Available ||
            entry.Header is not { State: PersistentInventorySnapshotState.Active } header ||
            header.CurrentVerified is not { } current ||
            entry.StoredRevision is not { } stored)
        {
            return false;
        }

        var candidate = new PersistentInventoryRestoreReservation(
            userId,
            current.SnapshotId,
            header.Revision,
            PersistentInventoryOperationId.New(),
            PersistentInventoryLifeId.New(),
            stored);
        if (!_restoreReservations.TryAdd(userId, candidate))
            return false;

        if (!_cache.TryGetValue(userId, out var currentEntry) || !ReferenceEquals(currentEntry, entry))
        {
            _restoreReservations.TryRemove(
                new KeyValuePair<NetUserId, PersistentInventoryRestoreReservation>(userId, candidate));
            return false;
        }

        reservation = candidate;
        return true;
    }

    public bool TryBeginLifeLoss(
        NetUserId userId,
        PersistentInventoryLifeId lifeId,
        out PersistentInventoryLifeLossReservation reservation)
    {
        reservation = default!;
        if (_restoreReservations.ContainsKey(userId) ||
            !_cache.TryGetValue(userId, out var entry) ||
            entry.Header is not
            {
                State: PersistentInventorySnapshotState.Bound,
                CurrentVerified: { } current,
                LifeId: { } currentLife,
            } header ||
            currentLife != lifeId)
        {
            return false;
        }

        var candidate = new PersistentInventoryLifeLossReservation(
            userId,
            current.SnapshotId,
            header.Revision,
            PersistentInventoryOperationId.New(),
            lifeId);
        if (!_lifeLossReservations.TryAdd(userId, candidate))
            return false;

        if (!_cache.TryGetValue(userId, out var currentEntry) || !ReferenceEquals(currentEntry, entry))
        {
            _lifeLossReservations.TryRemove(
                new KeyValuePair<NetUserId, PersistentInventoryLifeLossReservation>(userId, candidate));
            return false;
        }

        reservation = candidate;
        return true;
    }

    public bool IsLifeLossPending(NetUserId userId)
    {
        return _lifeLossReservations.ContainsKey(userId);
    }

    public bool TryRefreshLifeLoss(
        PersistentInventoryLifeLossReservation reservation,
        PersistentInventorySnapshotHeader header,
        out PersistentInventoryLifeLossReservation refreshed)
    {
        refreshed = reservation;
        if (header is not
            {
                State: PersistentInventorySnapshotState.Bound,
                CurrentVerified: { } current,
                LifeId: { } lifeId,
            } ||
            current.SnapshotId != reservation.SnapshotId ||
            lifeId != reservation.LifeId)
        {
            return false;
        }

        refreshed = reservation with { ExpectedRevision = header.Revision };
        return _lifeLossReservations.TryUpdate(
            reservation.UserId,
            refreshed,
            reservation);
    }

    public void CompleteLifeLoss(
        PersistentInventoryLifeLossReservation reservation,
        PersistentInventorySnapshotHeader header)
    {
        if (!_lifeLossReservations.TryRemove(
                new KeyValuePair<NetUserId, PersistentInventoryLifeLossReservation>(
                    reservation.UserId,
                    reservation)))
        {
            return;
        }

        SupersedeLoads(reservation.UserId);
        _cache[reservation.UserId] = FromHeader(header);
        CompleteDeferredRemove(reservation.UserId);
    }

    public void CompleteRestore(
        PersistentInventoryRestoreReservation reservation,
        PersistentInventorySnapshotHeader header)
    {
        if (!_restoreReservations.TryRemove(
                new KeyValuePair<NetUserId, PersistentInventoryRestoreReservation>(
                    reservation.UserId,
                    reservation)))
        {
            return;
        }

        SupersedeLoads(reservation.UserId);
        _cache[reservation.UserId] = FromHeader(header);
    }

    public void UpdateFromMutation(NetUserId userId, PersistentInventorySnapshotHeader header)
    {
        SupersedeLoads(userId);
        _cache[userId] = FromHeader(header);
    }

    public void ReleaseRestore(PersistentInventoryRestoreReservation reservation)
    {
        _restoreReservations.TryRemove(
            new KeyValuePair<NetUserId, PersistentInventoryRestoreReservation>(
                reservation.UserId,
                reservation));
        CompleteDeferredRemove(reservation.UserId);
    }

    public bool IsRestoreReservationCurrent(PersistentInventoryRestoreReservation reservation)
    {
        return _restoreReservations.TryGetValue(reservation.UserId, out var current) &&
               ReferenceEquals(current, reservation);
    }

    public void Remove(NetUserId userId)
    {
        if (_restoreReservations.ContainsKey(userId) ||
            _lifeLossReservations.ContainsKey(userId) ||
            _cache.TryGetValue(userId, out var entry) &&
            entry.Status == PersistentInventoryCacheStatus.Bound)
        {
            _deferredRemovals[userId] = 0;
            return;
        }

        _deferredRemovals.TryRemove(userId, out _);
        SupersedeLoads(userId);
        _cache.TryRemove(userId, out _);
        if (_loadStates.TryGetValue(userId, out var loadState))
        {
            Volatile.Write(ref loadState.Retired, 1);
            Interlocked.Increment(ref loadState.Generation);
            _loadStates.TryRemove(new KeyValuePair<NetUserId, LoadState>(userId, loadState));
        }
    }

    private void SupersedeLoads(NetUserId userId)
    {
        if (_loadStates.TryGetValue(userId, out var loadState))
            Interlocked.Increment(ref loadState.Generation);
    }

    private void CompleteDeferredRemove(NetUserId userId)
    {
        if (_deferredRemovals.ContainsKey(userId))
            Remove(userId);
    }

    internal static PersistentInventoryCacheEntry FromHeader(PersistentInventorySnapshotHeader? header)
    {
        if (header == null)
            return PersistentInventoryCacheEntry.None;

        var status = header.State switch
        {
            PersistentInventorySnapshotState.Active => PersistentInventoryCacheStatus.Unavailable,
            PersistentInventorySnapshotState.Bound => PersistentInventoryCacheStatus.Bound,
            PersistentInventorySnapshotState.Staging => PersistentInventoryCacheStatus.Staging,
            PersistentInventorySnapshotState.Quarantined => PersistentInventoryCacheStatus.Quarantined,
            PersistentInventorySnapshotState.Aborted => FromVerifiedState(header.VerifiedState),
            PersistentInventorySnapshotState.None
                or PersistentInventorySnapshotState.Invalid
                or PersistentInventorySnapshotState.LostByDisconnect => PersistentInventoryCacheStatus.None,
            _ => PersistentInventoryCacheStatus.Unavailable,
        };

        return new PersistentInventoryCacheEntry(status, header);
    }

    private static PersistentInventoryCacheStatus FromVerifiedState(PersistentInventorySnapshotState state)
    {
        return state switch
        {
            PersistentInventorySnapshotState.Active => PersistentInventoryCacheStatus.Unavailable,
            PersistentInventorySnapshotState.Bound => PersistentInventoryCacheStatus.Bound,
            PersistentInventorySnapshotState.Quarantined => PersistentInventoryCacheStatus.Quarantined,
            PersistentInventorySnapshotState.None
                or PersistentInventorySnapshotState.Invalid
                or PersistentInventorySnapshotState.LostByDisconnect
                or PersistentInventorySnapshotState.Aborted => PersistentInventoryCacheStatus.None,
            _ => PersistentInventoryCacheStatus.Unavailable,
        };
    }

    private static bool HeaderRequiresStoredRevision(PersistentInventorySnapshotHeader? header)
    {
        return header?.State == PersistentInventorySnapshotState.Active ||
               header is
               {
                   State: PersistentInventorySnapshotState.Aborted,
                   VerifiedState: PersistentInventorySnapshotState.Active,
               };
    }

    private static PersistentInventoryCacheEntry Unavailable(
        PersistentInventorySnapshotHeader? header,
        string errorCode)
    {
        return new PersistentInventoryCacheEntry(
            PersistentInventoryCacheStatus.Unavailable,
            header,
            errorCode);
    }

    private static bool IsMatchingRevision(
        NetUserId userId,
        PersistentInventoryRevisionMetadata metadata,
        PersistentInventoryStoredRevision? stored)
    {
        return stored != null &&
               stored.AccountId.Value == userId.UserId &&
               stored.Metadata.SnapshotId == metadata.SnapshotId &&
               stored.Metadata.PayloadSha256.SequenceEqual(metadata.PayloadSha256) &&
               stored.Metadata.SchemaVersion == metadata.SchemaVersion &&
               stored.Metadata.PolicyId == metadata.PolicyId &&
               stored.Metadata.ItemCount == metadata.ItemCount &&
               stored.Metadata.EntityCount == metadata.EntityCount &&
               stored.Metadata.UncompressedBytes == metadata.UncompressedBytes &&
               stored.Metadata.CompressedBytes == metadata.CompressedBytes;
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill("wh40k.persistent_inventory");
    }

    private sealed class LoadState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public long Generation;
        public int Retired;
    }
}
