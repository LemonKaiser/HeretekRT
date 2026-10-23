using Content.Server._WH40K.SectorMap.Components;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed class KoronusHuntingTurretSystem : EntitySystem
{
    [Dependency] private KoronusHuntingTargetSystem _targets = default!;
    [Dependency] private NPCUtilitySystem _utility = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private MobStateSystem _mobs = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KoronusHuntingTurretComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<KoronusHuntingProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    public EntityUid SelectTarget(NPCBlackboard blackboard)
    {
        var best = EntityUid.Invalid;
        var bestScore = 0f;
        // Retain native range, visibility, access and target scoring, filtering before choosing the best.
        foreach (var (target, score) in _utility.GetEntities(blackboard, "NearbyGunTargets", bestOnly: false).Entities)
        {
            if (score <= bestScore || !_targets.IsValidTarget(target))
                continue;
            best = target;
            bestScore = score;
        }
        return best;
    }

    private void OnShotAttempted(Entity<KoronusHuntingTurretComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.User == ent.Owner &&
            TryComp<NPCRangedCombatComponent>(ent, out var ranged) && _targets.IsValidTarget(ranged.Target))
            return;

        // A player can take over an NPC or its faction can change after the HTN plan was created.
        args.Cancel();
        RemCompDeferred<NPCRangedCombatComponent>(ent);
    }

    private void OnProjectileHit(Entity<KoronusHuntingProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        args.Handled = true;
        if (_targets.IsValidTarget(args.Target))
        {
            // Use the actual death threshold rather than excessive damage that would gib the loot.
            if (_thresholds.TryGetDeadThreshold(args.Target, out var threshold) &&
                TryComp<DamageableComponent>(args.Target, out var damageable))
            {
                var damage = new DamageSpecifier();
                damage.DamageDict["Blunt"] = FixedPoint2.Max(FixedPoint2.Zero, threshold.Value - damageable.TotalDamage);
                _damage.TryChangeDamage(args.Target, damage, ignoreResistances: true, origin: args.Shooter,
                    ignoreGlobalModifiers: true, canSever: false, tool: ent);
            }

            // Some mobs use organ health instead of global damage thresholds. Keep normal death events
            // and the corpse in either case, without deleting the mob or touching its inventory.
            if (Exists(args.Target))
                _mobs.ChangeMobState(args.Target, MobState.Dead, origin: args.Shooter);
        }
        QueueDel(ent);
    }
}
