using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public async Task<IReadOnlyList<PersistentInventoryRevisionMetadata>> GetPersistentInventoryRevisionsAsync(
        NetUserId userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var revisions = await db.DbContext.Wh40kPersistentInventoryRevisions.AsNoTracking()
            .Where(revision => revision.UserId == userId.UserId)
            .OrderByDescending(revision => revision.SavedAt)
            .Take(100)
            .Select(revision => new Wh40kPersistentInventoryRevision
            {
                SnapshotId = revision.SnapshotId,
                SchemaVersion = revision.SchemaVersion,
                PolicyId = revision.PolicyId,
                CapturedRoleId = revision.CapturedRoleId,
                CapturedProfileName = revision.CapturedProfileName,
                PayloadSha256 = revision.PayloadSha256,
                ItemCount = revision.ItemCount,
                EntityCount = revision.EntityCount,
                UncompressedBytes = revision.UncompressedBytes,
                CompressedBytes = revision.CompressedBytes,
                OperationId = revision.OperationId,
                CreatedAt = revision.CreatedAt,
                SavedAt = revision.SavedAt,
            })
            .ToListAsync(cancel);
        return revisions.Select(ToPersistentInventoryRevisionMetadata).ToArray();
    }
}
