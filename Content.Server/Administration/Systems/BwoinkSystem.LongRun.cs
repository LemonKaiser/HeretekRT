using System.Threading;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    /// <summary>
    /// Number of Discord-only relay messages rejected by the safety caps.
    /// In-game AHELP delivery is performed before this auxiliary queue.
    /// </summary>
    public long GetLongRunDroppedRelayCount() => Interlocked.Read(ref _droppedDiscordRelayMessages);
}
