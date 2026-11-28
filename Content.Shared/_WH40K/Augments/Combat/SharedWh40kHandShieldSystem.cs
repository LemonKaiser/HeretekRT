using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Shared arc-coverage check so both server and predicting clients stop blocked projectiles cleanly.
/// </summary>
public abstract partial class SharedWh40kHandShieldSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kHandShieldUserComponent, ProjectileReflectAttemptEvent>(OnProjectile);
    }

    private void OnProjectile(Entity<Wh40kHandShieldUserComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var travel = _physics.GetMapLinearVelocity(args.ProjUid) - _physics.GetMapLinearVelocity(ent);
        if (!Covers(ent, -travel, out var shield))
            return;

        args.Cancelled = true;
        ProjectileBlocked(ent, shield, args.ProjUid, args.Component.Damage.GetTotal().Float());
    }

    protected bool Covers(Entity<Wh40kHandShieldUserComponent> ent, Vector2 from,
        [NotNullWhen(true)] out Wh40kHandShieldComponent? shield)
    {
        if (!TryComp(ent.Comp.Implant, out shield) || shield.Collapsed || from.LengthSquared() < 1e-6f)
            return false;

        var facing = _transform.GetWorldRotation(ent).ToWorldVec();
        return Vector2.Dot(facing, Vector2.Normalize(from)) >= Math.Cos(shield.Arc.Theta / 2);
    }

    protected abstract void ProjectileBlocked(Entity<Wh40kHandShieldUserComponent> ent, Wh40kHandShieldComponent shield,
        EntityUid projectile, float damage);
}
