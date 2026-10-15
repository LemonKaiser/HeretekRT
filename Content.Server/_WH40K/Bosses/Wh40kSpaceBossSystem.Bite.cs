using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kSpaceBossSystem
{
    [Dependency] private DamageableSystem _damage = default!;
    private readonly HashSet<EntityUid> _biteVictims = new();

    private void OnBiteDamage(EntityUid uid, Wh40kSpaceBossComponent component, MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count != 1 || HasComp<MobStateComponent>(args.HitEntities[0]))
            return;

        // Use structural damage for the first hull segment as well as the rest of the bite.
        args.BonusDamage += component.BiteHullDamage - args.BaseDamage;
    }

    private void OnBiteApplied(EntityUid uid, Wh40kSpaceBossComponent component, MeleeDamageAppliedEvent args)
    {
        if (!TryComp(args.Target, out TransformComponent? primary) ||
            !TryComp<MeleeWeaponComponent>(uid, out var weapon) || IsProtectedTarget(args.Target))
            return;

        var point = args.ImpactCoordinates;
        var direction = _transform.GetWorldRotation(uid).ToVec();
        var livingDamage = _melee.GetDamage(uid, uid, weapon);
        _biteVictims.Clear();
        _lookup.GetEntitiesInRange(point.MapId, point.Position, component.BiteRadius, _biteVictims, LookupFlags.Uncontained);
        foreach (var victim in _biteVictims)
        {
            if (victim == uid || victim == args.Target || TerminatingOrDeleted(victim) ||
                !HasComp<DamageableComponent>(victim) || !TryComp(victim, out TransformComponent? xform) ||
                xform.MapID != point.MapId || IsProtectedTarget(victim) || IsBossAlly(uid, victim))
                continue;

            var living = HasComp<MobStateComponent>(victim);
            if (xform.GridUid != primary.GridUid && !(living && xform.GridUid == null))
                continue;
            var offset = _transform.GetWorldPosition(xform) - point.Position;
            var depth = Vector2.Dot(offset, direction);
            if (offset.LengthSquared() > component.BiteRadius * component.BiteRadius ||
                depth < -component.BiteReach || depth > component.BiteDepth)
                continue;

            _damage.TryChangeDamage(victim, living ? livingDamage : component.BiteHullDamage,
                origin: uid, tool: uid, armorPenetration: weapon.ArmorPenetration,
                partMultiplier: weapon.ClickPartDamageMultiplier, impactCoordinates: point);
        }
        // Boarding follows a hull breach, not a passenger bitten through an open deck.
        if (!HasComp<MobStateComponent>(args.Target))
            QueueBoardingWave(uid, component, primary.GridUid, point);
    }
}
