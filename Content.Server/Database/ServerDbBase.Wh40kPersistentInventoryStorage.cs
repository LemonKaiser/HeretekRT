using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    private static long InventoryAuditBytes(string actor, string? reason)
        => 192L + Encoding.UTF8.GetByteCount(actor) + Encoding.UTF8.GetByteCount(reason ?? "");

    private static async Task<long> InventoryPayloadBytesAsync(ServerDbContext db, Guid user, CancellationToken cancel)
    {
        var blobs = await db.Wh40kPersistentInventoryBlobs.Where(row => row.UserId == user)
            .SumAsync(row => (long) row.Payload.Length, cancel);
        var legacy = await db.Wh40kPersistentInventoryRevisions.Where(row => row.UserId == user && row.BlobId == null)
            .SumAsync(row => (long) row.Payload.Length, cancel);
        return blobs + legacy;
    }

    private static async Task<Wh40kPersistentInventoryBlob?> StoreInventoryBlobAsync(
        ServerDbContext db, Guid user, byte[] payload, byte[] hash, CancellationToken cancel, int legacyRepairBytes = 0)
    {
        if (payload.Length > Math.Max(PersistentInventoryStoragePolicy.SnapshotBytes, legacyRepairBytes))
            return null;
        var existing = await db.Wh40kPersistentInventoryBlobs
            .SingleOrDefaultAsync(row => row.UserId == user && row.Sha256 == hash, cancel);
        if (existing != null)
        {
            // A hash is an index, not permission to replace different bytes.
            return existing.Payload.AsSpan().SequenceEqual(payload) ? existing : null;
        }
        if (await InventoryPayloadBytesAsync(db, user, cancel) + payload.Length > PersistentInventoryStoragePolicy.PayloadBytes &&
            (legacyRepairBytes <= PersistentInventoryStoragePolicy.SnapshotBytes || payload.Length > legacyRepairBytes))
            return null;
        var blob = new Wh40kPersistentInventoryBlob { Id = Guid.NewGuid(), UserId = user, Sha256 = hash, Payload = payload };
        db.Wh40kPersistentInventoryBlobs.Add(blob);
        return blob;
    }

    // Called inside the mutation transaction, after its first SaveChanges. A failed cleanup rolls
    // back the operation too. No world cleanup is authorized by a stage which exceeds the quota.
    private async Task MaintainInventoryStorageAsync(
        ServerDbContext db, Wh40kPersistentInventory account, DateTime now, CancellationToken cancel)
    {
        var user = account.UserId;
        var storage = await db.Wh40kPersistentInventoryStorage.SingleOrDefaultAsync(row => row.UserId == user, cancel);
        if (storage == null)
        {
            storage = new Wh40kPersistentInventoryStorage { UserId = user };
            db.Wh40kPersistentInventoryStorage.Add(storage);
        }
        var events = await db.Wh40kPersistentInventoryAudits.AsNoTracking()
            .Where(row => row.UserId == user && row.Revision > storage.LastCountedRevision)
            .OrderBy(row => row.Revision).ToListAsync(cancel);
        foreach (var entry in events)
        {
            switch ((PersistentInventoryAuditAction) entry.Action)
            {
                case PersistentInventoryAuditAction.Promoted: storage.Saves++; break;
                case PersistentInventoryAuditAction.Invalidated: storage.Losses++; break;
                case PersistentInventoryAuditAction.Lost:
                    storage.Losses++;
                    storage.LostSnapshotId = entry.SnapshotId;
                    storage.LostExpiresAt = NormalizeDatabaseTime(entry.CreatedAt) + PersistentInventoryStoragePolicy.LostLifetime;
                    break;
                case PersistentInventoryAuditAction.Recovered:
                    storage.Recoveries++;
                    if (entry.SnapshotId == storage.LostSnapshotId)
                    {
                        storage.LostSnapshotId = null;
                        storage.LostExpiresAt = null;
                    }
                    break;
            }
        }
        storage.LastCountedRevision = account.Revision;
        if (storage.LostExpiresAt is { } expires && NormalizeDatabaseTime(expires) <= now)
        {
            storage.LostSnapshotId = null;
            storage.LostExpiresAt = null;
        }
        if (account.StagingSnapshotId == null && account.SavePhase == (int) PersistentInventorySavePhase.None &&
            account.State is (int) PersistentInventorySnapshotState.Invalid or (int) PersistentInventorySnapshotState.LostByDisconnect &&
            NormalizeDatabaseTime(account.UpdatedAt) <= now - PersistentInventoryStoragePolicy.LostLifetime &&
            (account.CurrentSnapshotId != null || account.LastKnownGoodSnapshotId != null))
        {
            account.CurrentSnapshotId = null;
            account.LastKnownGoodSnapshotId = null;
            account.Revision = checked(account.Revision + 1);
            storage.LastCountedRevision = account.Revision;
        }
        var retained = new[] { account.CurrentSnapshotId, account.LastKnownGoodSnapshotId, account.StagingSnapshotId, storage.LostSnapshotId }
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToArray();
        await db.Wh40kPersistentInventoryRevisions.Where(row => row.UserId == user && !retained.Contains(row.SnapshotId))
            .ExecuteDeleteAsync(cancel);

        // Migrate only retained legacy payloads. Large old snapshots remain readable and are
        // reported as over budget; never truncate items to satisfy a new storage policy.
        var legacy = await db.Wh40kPersistentInventoryRevisions
            .Where(row => row.UserId == user && row.BlobId == null).ToListAsync(cancel);
        foreach (var revision in legacy)
        {
            var blob = db.Wh40kPersistentInventoryBlobs.Local.FirstOrDefault(row => row.UserId == user && row.Sha256.AsSpan().SequenceEqual(revision.PayloadSha256)) ??
                       await db.Wh40kPersistentInventoryBlobs.SingleOrDefaultAsync(row => row.UserId == user && row.Sha256 == revision.PayloadSha256, cancel);
            if (blob == null)
            {
                blob = new Wh40kPersistentInventoryBlob { Id = Guid.NewGuid(), UserId = user, Sha256 = revision.PayloadSha256, Payload = revision.Payload };
                db.Wh40kPersistentInventoryBlobs.Add(blob);
            }
            if (!blob.Payload.AsSpan().SequenceEqual(revision.Payload))
                throw new InvalidOperationException("Persistent inventory payload hash collision.");
            revision.BlobId = blob.Id;
            revision.Payload = Array.Empty<byte>();
        }

        var history = await db.Wh40kPersistentInventoryAudits.AsNoTracking().Where(row => row.UserId == user)
            .OrderByDescending(row => row.Id).Take(PersistentInventoryStoragePolicy.HistoryEntries).ToListAsync(cancel);
        var keep = new List<long>();
        long bytes = 0;
        foreach (var entry in history)
        {
            var size = InventoryAuditBytes(entry.Actor, entry.Reason);
            if (NormalizeDatabaseTime(entry.CreatedAt) < now - PersistentInventoryStoragePolicy.HistoryLifetime ||
                bytes + size > PersistentInventoryStoragePolicy.HistoryBytes)
                break;
            bytes += size;
            keep.Add(entry.Id);
        }
        storage.PrunedEvents += await db.Wh40kPersistentInventoryAudits
            .Where(row => row.UserId == user && !keep.Contains(row.Id)).ExecuteDeleteAsync(cancel);
        var cutoff = await db.Wh40kPersistentInventoryReceipts.Where(row => row.UserId == user)
            .OrderByDescending(row => row.Revision).Skip(PersistentInventoryStoragePolicy.ReceiptEntries - 1)
            .Select(row => (long?) row.Revision).FirstOrDefaultAsync(cancel);
        if (cutoff != null)
            await db.Wh40kPersistentInventoryReceipts.Where(row => row.UserId == user && row.Revision < cutoff && row.OperationId != account.OperationId)
                .ExecuteDeleteAsync(cancel);
        await db.SaveChangesAsync(cancel);
        await db.Wh40kPersistentInventoryBlobs.Where(blob => blob.UserId == user &&
            !db.Wh40kPersistentInventoryRevisions.Any(revision => revision.UserId == user && revision.BlobId == blob.Id))
            .ExecuteDeleteAsync(cancel);
    }

    public async Task<PersistentInventoryStorageUsage> GetPersistentInventoryStorageUsageAsync(NetUserId userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var user = userId.UserId;
        var snapshots = await db.DbContext.Wh40kPersistentInventoryRevisions.CountAsync(row => row.UserId == user, cancel);
        var blobs = await db.DbContext.Wh40kPersistentInventoryBlobs.CountAsync(row => row.UserId == user, cancel);
        var payload = await InventoryPayloadBytesAsync(db.DbContext, user, cancel);
        var history = await db.DbContext.Wh40kPersistentInventoryAudits.AsNoTracking().Where(row => row.UserId == user)
            .Select(row => new { row.Actor, row.Reason }).ToListAsync(cancel);
        var historyBytes = history.Sum(row => InventoryAuditBytes(row.Actor, row.Reason));
        var receipts = await db.DbContext.Wh40kPersistentInventoryReceipts.CountAsync(row => row.UserId == user, cancel);
        var storage = await db.DbContext.Wh40kPersistentInventoryStorage.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == user, cancel);
        return new(snapshots, blobs, payload, history.Count, historyBytes, receipts,
            payload + historyBytes + receipts * 128L + snapshots * 1024L + (storage == null ? 0 : 2048),
            storage?.PrunedEvents ?? 0, storage?.Saves ?? 0, storage?.Losses ?? 0, storage?.Recoveries ?? 0);
    }

    public async Task<PersistentInventoryMaintenanceResult> MaintainPersistentInventoryStorageAsync(Guid? afterUser, int limit = 100, CancellationToken cancel = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var db = await GetDb(cancel);
        var query = db.DbContext.Wh40kPersistentInventories.AsNoTracking();
        if (afterUser != null) query = query.Where(row => row.UserId.CompareTo(afterUser.Value) > 0);
        var users = await query.OrderBy(row => row.UserId).Select(row => row.UserId).Take(limit).ToListAsync(cancel);
        foreach (var user in users)
        {
            await using var transaction = await db.DbContext.Database.BeginTransactionAsync(cancel);
            db.DbContext.ChangeTracker.Clear();
            var account = await db.DbContext.Wh40kPersistentInventories.SingleAsync(row => row.UserId == user, cancel);
            // A maintenance pass must not race a save: use the same revision concurrency token.
            db.DbContext.Entry(account).Property(row => row.Revision).IsModified = true;
            try
            {
                await db.DbContext.SaveChangesAsync(cancel);
                await MaintainInventoryStorageAsync(db.DbContext, account, DateTime.UtcNow, cancel);
                await transaction.CommitAsync(cancel);
            }
            catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(cancel); }
        }
        if (users.Count < limit && db.DbContext.Database.IsNpgsql())
        {
            // Ordinary VACUUM reuses dead tuples without the exclusive lock of VACUUM FULL.
            foreach (var sql in new[]
            {
                "VACUUM (ANALYZE, SKIP_LOCKED) wh40k_persistent_inventory",
                "VACUUM (ANALYZE, SKIP_LOCKED) wh40k_persistent_inventory_revision",
                "VACUUM (ANALYZE, SKIP_LOCKED) wh40k_persistent_inventory_blob",
                "VACUUM (ANALYZE, SKIP_LOCKED) wh40k_persistent_inventory_audit",
                "VACUUM (ANALYZE, SKIP_LOCKED) wh40k_persistent_inventory_receipt",
                "VACUUM (ANALYZE, SKIP_LOCKED) wh40k_persistent_inventory_storage",
            })
                await db.DbContext.Database.ExecuteSqlRawAsync(sql, cancel);
        }
        return new(users.Count < limit ? null : users[^1], users.Count);
    }

    public async Task<PersistentInventoryStorageTotals> GetPersistentInventoryStorageTotalsAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var context = db.DbContext;
        var payload = await context.Wh40kPersistentInventoryBlobs.SumAsync(row => (long) row.Payload.Length, cancel) +
                      await context.Wh40kPersistentInventoryRevisions.SumAsync(row => (long) row.Payload.Length, cancel);
        var over = await context.Wh40kPersistentInventoryRevisions.GroupBy(row => row.UserId)
            .CountAsync(group => group.Sum(row => (long) row.CompressedBytes) > PersistentInventoryStoragePolicy.PayloadBytes ||
                                 group.Any(row => row.CompressedBytes > PersistentInventoryStoragePolicy.SnapshotBytes), cancel);
        return new(await context.Wh40kPersistentInventories.LongCountAsync(cancel), payload,
            await context.Wh40kPersistentInventoryAudits.LongCountAsync(cancel), over);
    }
}
