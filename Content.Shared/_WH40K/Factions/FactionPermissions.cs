using Content.Shared._Mono.Company;

namespace Content.Shared._WH40K.Factions;

public static class FactionPermissions
{
    public static bool CanLeave(FactionRole? role) => role is FactionRole.Member or FactionRole.Officer;

    public static bool CanAccessStorage(FactionRole? role) => role is FactionRole.Leader or FactionRole.Officer;

    public static bool CanRemove(FactionRole? actor, FactionRole? target, bool self) =>
        !self && target is FactionRole.Member or FactionRole.Officer &&
        (actor == FactionRole.Leader || actor == FactionRole.Officer && target == FactionRole.Member);
}
