namespace Content.Server._WH40K.Administration.Events;

public sealed partial class EventAdminSystem
{
    public bool BlocksTeamDamage(EntityUid? source, EntityUid target)
    {
        return source is { } attacker && attacker != target &&
               _players.TryGetSessionByEntity(attacker, out var attackerSession) &&
               _players.TryGetSessionByEntity(target, out var targetSession) &&
               _members.TryGetValue(attackerSession.UserId.UserId, out var attackerTeam) &&
               _members.TryGetValue(targetSession.UserId.UserId, out var targetTeam) &&
               attackerTeam == targetTeam &&
               _teams.TryGetValue(targetTeam, out var team) && !team.FriendlyFire;
    }
}
