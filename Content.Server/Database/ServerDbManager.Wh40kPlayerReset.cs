using System.Threading;
using System.Threading.Tasks;
using Content.Shared.Preferences;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task WaitForPendingCommandsAsync(CancellationToken cancel = default);
    Task ResetWh40kPlayerAsync(NetUserId userId, HumanoidCharacterProfile temporaryProfile,
        Guid actor, string reason, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task ResetWh40kPlayerAsync(NetUserId userId, HumanoidCharacterProfile temporaryProfile,
        Guid actor, string reason, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.ResetWh40kPlayerAsync(userId, temporaryProfile, actor, reason, cancel));
    }
}
