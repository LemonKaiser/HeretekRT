using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<bool> ReserveWh40kMedicalCooldownAsync(Guid user, Guid operation, long now, long until, CancellationToken cancel = default);
    Task<bool> ReleaseWh40kMedicalCooldownAsync(Guid user, Guid operation, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<bool> ReserveWh40kMedicalCooldownAsync(Guid user, Guid operation, long now, long until, CancellationToken cancel = default)
        => RunDbCommand(() => _db.ReserveWh40kMedicalCooldownAsync(user, operation, now, until, cancel));

    public Task<bool> ReleaseWh40kMedicalCooldownAsync(Guid user, Guid operation, CancellationToken cancel = default)
        => RunDbCommand(() => _db.ReleaseWh40kMedicalCooldownAsync(user, operation, cancel));
}
