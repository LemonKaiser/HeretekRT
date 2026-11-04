using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<Wh40kShuttleSlot?> GetWh40kShuttleAsync(Guid owner, bool payload = false);
    Task<List<Wh40kShuttleSlot>> GetPendingWh40kShuttlesAsync();
    Task<bool> WriteWh40kShuttleAsync(Wh40kShuttleSlot slot, long expectedRevision);
    Task<bool> ResolveWh40kShuttleAsync(Guid owner, long revision, Guid epoch, bool restore, int result);
}

public sealed partial class ServerDbManager
{
    public Task<Wh40kShuttleSlot?> GetWh40kShuttleAsync(Guid owner, bool payload = false)
        => RunDbCommand(() => _db.GetWh40kShuttleAsync(owner, payload));
    public Task<List<Wh40kShuttleSlot>> GetPendingWh40kShuttlesAsync()
        => RunDbCommand(() => _db.GetPendingWh40kShuttlesAsync());
    public Task<bool> WriteWh40kShuttleAsync(Wh40kShuttleSlot slot, long expectedRevision)
        => RunDbCommand(() => _db.WriteWh40kShuttleAsync(slot, expectedRevision));
    public Task<bool> ResolveWh40kShuttleAsync(Guid owner, long revision, Guid epoch, bool restore, int result)
        => RunDbCommand(() => _db.ResolveWh40kShuttleAsync(owner, revision, epoch, restore, result));
}
