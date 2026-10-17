using System.Linq;
using System.Threading.Tasks;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Dialogue;

public sealed partial class DialogueSystem
{
    public async Task PreparePlayerResetAsync(NetUserId userId)
    {
        CloseOrDiscardSessionsForUser(userId);
        var pending = _persistentMemoryWriteTasks.Where(entry => entry.Key.UserId == userId).Select(entry => entry.Value)
            .Concat(_persistentMemoryLoadTasks.Where(entry => entry.Key.UserId == userId).Select(entry => entry.Value))
            .ToArray();
        await Task.WhenAll(pending);
        ClearPersistentMemoryCache(userId);
    }
}
