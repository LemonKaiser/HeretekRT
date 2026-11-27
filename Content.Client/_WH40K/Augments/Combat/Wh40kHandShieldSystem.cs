using Content.Shared._WH40K.Augments.Combat;

namespace Content.Client._WH40K.Augments.Combat;

public sealed class Wh40kHandShieldSystem : SharedWh40kHandShieldSystem
{
    protected override void ProjectileBlocked(Entity<Wh40kHandShieldUserComponent> ent, Wh40kHandShieldComponent shield,
        EntityUid projectile, float damage)
    {
        if (IsClientSide(projectile))
            QueueDel(projectile);
    }
}
