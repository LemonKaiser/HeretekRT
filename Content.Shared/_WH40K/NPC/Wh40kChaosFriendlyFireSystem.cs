using Content.Shared.Explosion.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;

namespace Content.Shared._WH40K.NPC;
/// <summary>Chaos NPC shots pass through friends; explosives keep their normal collision and damage rules.</summary>
public sealed partial class Wh40kChaosFriendlyFireSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;

    public bool ShouldPass(EntityUid? shooter, EntityUid other)
    {
        return shooter is { } owner && HasComp<Wh40kChaosCombatantComponent>(owner) &&
               _factions.IsEntityFriendly(owner, other);
    }

    public bool ShouldPassProjectile(EntityUid projectile, EntityUid? shooter, EntityUid other)
    {
        return !HasComp<ExplosiveComponent>(projectile) && ShouldPass(shooter, other);
    }

    public bool ShouldPassProjectile(EntityUid projectile, EntityUid other)
    {
        var shooter = CompOrNull<ProjectileComponent>(projectile)?.Shooter ??
                      CompOrNull<ThrownItemComponent>(projectile)?.Thrower;
        return ShouldPassProjectile(projectile, shooter, other);
    }
}
