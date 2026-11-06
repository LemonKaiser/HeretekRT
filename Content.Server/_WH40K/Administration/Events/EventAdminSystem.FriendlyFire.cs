using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;

namespace Content.Server._WH40K.Administration.Events;

public sealed partial class EventAdminSystem
{
    private void OnBeforeTeamDamage(EntityUid uid, DamageableComponent component, ref BeforeDamageChangedEvent args)
    {
        if (!args.Cancelled && args.Damage.AnyPositive() && BlocksTeamDamage(args.Origin, uid))
            args.Cancelled = true;
    }

    private void OnBeforeTeamStaminaDamage(EntityUid uid, StaminaComponent component, ref BeforeStaminaDamageEvent args)
    {
        if (!args.Cancelled && args.Value > 0 && BlocksTeamDamage(args.Source, uid))
            args.Cancelled = true;
    }

    private bool BlocksTeamDamage(EntityUid? source, EntityUid target)
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
