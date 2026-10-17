using System.Threading.Tasks;
using Content.Server._WH40K.Administration;
using Robust.Shared.Network;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem
{
    private async void RunPlayerOperation(NetUserId userId, Func<Task> operation)
    {
        var reset = EntityManager.System<Wh40kPlayerResetSystem>();
        if (reset.IsResetting(userId))
            return;
        try { await reset.RunPlayerOperationAsync(userId, operation); }
        catch (Exception error) { Log.Error($"Safety deposit operation failed for {userId}: {error}"); }
    }
}
