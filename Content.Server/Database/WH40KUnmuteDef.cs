using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed class WH40KUnmuteDef(int muteId, NetUserId? unmutingAdmin, DateTimeOffset unmuteTime)
{
    public int MuteId { get; } = muteId;
    public NetUserId? UnmutingAdmin { get; } = unmutingAdmin;
    public DateTimeOffset UnmuteTime { get; } = unmuteTime;
}
