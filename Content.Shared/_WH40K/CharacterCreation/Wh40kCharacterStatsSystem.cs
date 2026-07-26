using Content.Shared._Goobstation.DoAfter;
using Content.Shared.DoAfter;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._WH40K.CharacterCreation;

/// <summary>
/// Applies the account-resolved runtime characteristics through existing combat and movement extension events.
/// The same component is replicated to the client so predicted melee and projectile feedback use
/// the exact calculation that the server validates.
/// </summary>
public sealed class Wh40kCharacterStatsSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kCharacterStatsComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<Wh40kCharacterStatsComponent, GetMeleeAttackRateEvent>(OnGetMeleeAttackRate);
        SubscribeLocalEvent<Wh40kCharacterStatsComponent, GetDoAfterDelayMultiplierEvent>(OnGetDoAfterDelayMultiplier);
        SubscribeLocalEvent<Wh40kCharacterStatsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<Wh40kCharacterStatsComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
        SubscribeLocalEvent<GunComponent, GunGetAmmoSpreadEvent>(OnGunGetAmmoSpread);
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private static void OnGetMeleeDamage(Entity<Wh40kCharacterStatsComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.User != ent.Owner)
            return;

        args.Damage *= Wh40kCharacteristicEffects.GetDamageMultiplier(ent.Comp.Melee);
    }

    private static void OnGetMeleeAttackRate(Entity<Wh40kCharacterStatsComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        if (args.User != ent.Owner)
            return;

        // One point shortens the interval by 0.5%, capped at 50%. Negative totals use
        // the reciprocal penalty and are capped as well, keeping malformed old data harmless.
        var cooldownMultiplier = Wh40kCharacteristicEffects.GetMeleeCooldownMultiplier(ent.Comp.Melee);
        args.Multipliers *= 1f / cooldownMultiplier;
    }

    private static void OnGetDoAfterDelayMultiplier(
        Entity<Wh40kCharacterStatsComponent> ent,
        ref GetDoAfterDelayMultiplierEvent args)
    {
        // DoAfter multiplies its duration by this value. At +10 Intelligence this is
        // 1 / 1.10, i.e. a genuine ten-percent speed increase rather than a display-only value.
        // The mechanical speedup stops at x2 while the permanent characteristic remains uncapped.
        var speed = Wh40kCharacteristicEffects.GetDoAfterSpeedMultiplier(ent.Comp.Intelligence);
        args.Multiplier /= speed;
    }

    private static void OnRefreshMovementSpeed(
        Entity<Wh40kCharacterStatsComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(Wh40kCharacteristicEffects.GetMovementSpeedMultiplier(ent.Comp.Agility));
    }

    private static void OnGunRefreshModifiers(
        Entity<Wh40kCharacterStatsComponent> ent,
        ref GunRefreshModifiersEvent args)
    {
        if (args.Gun.Comp.Holder != ent.Owner)
            return;

        var precision = Wh40kCharacteristicEffects.GetRangedPrecisionMultiplier(ent.Comp.Ranged);
        args.CameraRecoilScalar *= precision;
        args.AngleIncrease *= precision;
        args.MinAngle *= precision;
        args.MaxAngle *= precision;
    }

    private void OnGunGetAmmoSpread(Entity<GunComponent> gun, ref GunGetAmmoSpreadEvent args)
    {
        if (gun.Comp.Holder is not { } holder ||
            !TryComp<Wh40kCharacterStatsComponent>(holder, out var stats))
        {
            return;
        }

        // ProjectileSpreadComponent is separate from the recoil angle. Applying the same
        // multiplier here keeps shotguns and other multi-projectile ammunition consistent.
        args.Spread *= Wh40kCharacteristicEffects.GetRangedPrecisionMultiplier(stats.Ranged);
    }

    private void OnProjectileHit(Entity<ProjectileComponent> projectile, ref ProjectileHitEvent args)
    {
        if (args.Shooter is not { } shooter || !TryComp<Wh40kCharacterStatsComponent>(shooter, out var stats))
            return;

        args.Damage *= Wh40kCharacteristicEffects.GetDamageMultiplier(stats.Ranged);
    }

    public static float GetDoAfterSpeedMultiplier(int intelligence)
    {
        return Wh40kCharacteristicEffects.GetDoAfterSpeedMultiplier(intelligence);
    }
}
