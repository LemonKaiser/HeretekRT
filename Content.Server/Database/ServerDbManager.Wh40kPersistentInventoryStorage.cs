using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<PersistentInventoryStorageUsage> GetPersistentInventoryStorageUsageAsync(NetUserId userId, CancellationToken cancel = default);
    Task<PersistentInventoryStorageTotals> GetPersistentInventoryStorageTotalsAsync(CancellationToken cancel = default);
    Task<PersistentInventoryMaintenanceResult> MaintainPersistentInventoryStorageAsync(Guid? afterUser, int limit = 100, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<PersistentInventoryStorageUsage> GetPersistentInventoryStorageUsageAsync(NetUserId userId, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetPersistentInventoryStorageUsageAsync(userId, cancel));
    public Task<PersistentInventoryStorageTotals> GetPersistentInventoryStorageTotalsAsync(CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetPersistentInventoryStorageTotalsAsync(cancel));
    public Task<PersistentInventoryMaintenanceResult> MaintainPersistentInventoryStorageAsync(Guid? afterUser, int limit = 100, CancellationToken cancel = default)
        => RunDbCommand(() => _db.MaintainPersistentInventoryStorageAsync(afterUser, limit, cancel));
}
