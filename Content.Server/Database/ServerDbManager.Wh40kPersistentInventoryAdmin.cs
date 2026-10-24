using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<IReadOnlyList<PersistentInventoryRevisionMetadata>> GetPersistentInventoryRevisionsAsync(
        NetUserId userId, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<IReadOnlyList<PersistentInventoryRevisionMetadata>> GetPersistentInventoryRevisionsAsync(
        NetUserId userId, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetPersistentInventoryRevisionsAsync(userId, cancel));
}
