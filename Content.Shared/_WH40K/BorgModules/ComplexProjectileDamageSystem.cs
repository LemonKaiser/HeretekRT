using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Whitelist;

namespace Content.Shared._WH40K.BorgModules;

/// <summary>
/// Replaces a projectile's impact damage with the first matching <see cref="ComplexProjectileDamageComponent"/> option.
/// </summary>
public sealed partial class ComplexProjectileDamageSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComplexProjectileDamageComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(Entity<ComplexProjectileDamageComponent> ent, ref ProjectileHitEvent args)
    {
        foreach (var option in ent.Comp.DamageOptions)
        {
            if (!_whitelist.CheckBoth(args.Target, option.Blacklist, option.Whitelist))
                continue;

            args.Damage = new DamageSpecifier(option.Damage);
            return;
        }
    }
}
