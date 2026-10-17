using System.Numerics;
using System.Linq;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Physics;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kSpaceBossSystem
{
    [Dependency] private DamageableSystem _damage = default!;
    private readonly HashSet<EntityUid> _biteVictims = new();

    private void OnBiteDamage(EntityUid uid, Wh40kSpaceBossComponent component, MeleeHitEvent args)
    {
        component.BiteVictims.Clear();
        if (!args.IsHit || args.HitEntities.Count != 1 ||
            !TryComp(args.HitEntities[0], out TransformComponent? primary) ||
            IsProtectedTarget(args.HitEntities[0]))
            return;

        // Use structural damage for the first hull segment as well as the rest of the bite.
        if (!HasComp<MobStateComponent>(args.HitEntities[0]))
            args.BonusDamage += component.BiteHullDamage - args.BaseDamage;

        var point = _transform.GetMapCoordinates(primary);
        var origin = _transform.GetWorldPosition(uid);
        var direction = _transform.GetWorldRotation(uid).ToVec();
        _biteVictims.Clear();
        _lookup.GetEntitiesInRange(point.MapId, point.Position, component.BiteRadius, _biteVictims, LookupFlags.Uncontained);
        foreach (var victim in _biteVictims)
        {
            if (victim == uid || victim == args.HitEntities[0] || TerminatingOrDeleted(victim) ||
                !HasComp<DamageableComponent>(victim) || !TryComp(victim, out TransformComponent? xform) ||
                xform.MapID != point.MapId || IsProtectedTarget(victim) || IsBossAlly(uid, victim))
                continue;

            var living = HasComp<MobStateComponent>(victim);
            if (xform.GridUid != primary.GridUid && !(living && xform.GridUid == null))
                continue;

            var position = _transform.GetWorldPosition(xform);
            var offset = position - point.Position;
            if (offset.LengthSquared() > component.BiteRadius * component.BiteRadius ||
                MathF.Abs(Vector2.Dot(offset, direction)) > component.BiteDepth)
                continue;

            var delta = position - origin;
            var distance = delta.Length();
            // Snapshot visibility before any hull is destroyed: one bite cannot expose
            // and damage the next wall or passengers behind it during the same attack.
            if (distance > 0.001f && _physics.IntersectRay(point.MapId,
                    new CollisionRay(origin, delta / distance, (int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable)),
                    distance, uid, false).Any(hit => hit.HitEntity != victim &&
                        !HasComp<ShipShieldComponent>(hit.HitEntity) && !IsBossAlly(uid, hit.HitEntity)))
                continue;

            component.BiteVictims.Add(victim);
        }
    }

    private void OnBiteApplied(EntityUid uid, Wh40kSpaceBossComponent component, MeleeDamageAppliedEvent args)
    {
        if (!TryComp<MeleeWeaponComponent>(uid, out var weapon) ||
            !TerminatingOrDeleted(args.Target) && IsProtectedTarget(args.Target))
            return;

        var point = args.ImpactCoordinates;
        var livingDamage = _melee.GetDamage(uid, uid, weapon);
        foreach (var victim in component.BiteVictims)
        {
            if (TerminatingOrDeleted(victim) || IsProtectedTarget(victim))
                continue;

            var living = HasComp<MobStateComponent>(victim);
            _damage.TryChangeDamage(victim, living ? livingDamage : component.BiteHullDamage,
                origin: uid, tool: uid, armorPenetration: weapon.ArmorPenetration,
                partMultiplier: weapon.ClickPartDamageMultiplier, impactCoordinates: point);
        }
        component.BiteVictims.Clear();
        // Boarding follows a hull breach, not a passenger bitten through an open deck.
        if (!HasComp<MobStateComponent>(args.Target) && TryComp(args.Target, out TransformComponent? primary))
            QueueBoardingWave(uid, component, primary.GridUid, point);

        var xform = Transform(uid);
        var direction = point.Position - _transform.GetWorldPosition(xform);
        if (direction.LengthSquared() > 0.001f)
            BeginRetreat(uid, component, xform, direction.Normalized());
    }
}
