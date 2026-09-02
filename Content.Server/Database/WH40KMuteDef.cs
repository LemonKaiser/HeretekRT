using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed class WH40KMuteDef(
    int? id,
    NetUserId userId,
    WH40KMuteType type,
    string reason,
    NetUserId? mutingAdmin,
    DateTimeOffset muteTime,
    DateTimeOffset? expirationTime,
    WH40KUnmuteDef? unmute)
{
    public int? Id { get; } = id;
    public NetUserId UserId { get; } = userId;
    public WH40KMuteType Type { get; } = type;
    public string Reason { get; } = reason;
    public NetUserId? MutingAdmin { get; } = mutingAdmin;
    public DateTimeOffset MuteTime { get; } = muteTime;
    public DateTimeOffset? ExpirationTime { get; } = expirationTime;
    public WH40KUnmuteDef? Unmute { get; } = unmute;

    public bool IsActive(DateTimeOffset now)
    {
        return Unmute == null && (ExpirationTime == null || ExpirationTime > now);
    }
}
