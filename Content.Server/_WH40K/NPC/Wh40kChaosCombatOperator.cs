using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Explosion.Components;
using Content.Shared.Ensnaring.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.NPC;
/// <summary>Switches innate ranged attacks to the existing melee AI when a target closes in.</summary>
public sealed partial class Wh40kChaosCombatOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    [DataField]
    public string TargetKey = "Target";

    [DataField]
    public float MeleeRange = 2f;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var valid = blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entities) &&
                    !_entities.Deleted(target) &&
                    (!_entities.TryGetComponent<MobStateComponent>(target, out var state) ||
                     state.CurrentState == MobState.Alive);
        return Task.FromResult<(bool, Dictionary<string, object>?)>((valid, null));
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        Update(blackboard, 0f);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var tactics = _entities.GetComponent<Wh40kChaosTacticsComponent>(owner);
        var safety = _entities.System<Wh40kChaosTacticsSystem>();
        if (_timing.CurTime >= tactics.NextTargetCheck ||
            !blackboard.TryGetValue<EntityUid>(TargetKey, out var previous, _entities) ||
            !safety.CanFightTarget(owner, previous, tactics))
        {
            tactics.NextTargetCheck = _timing.CurTime + TimeSpan.FromSeconds(0.5);
            var selected = safety.SelectCombatTarget(blackboard, tactics, MeleeRange, TargetKey);
            if (!selected.IsValid())
                return HTNOperatorStatus.Finished;
            blackboard.SetValue(TargetKey, selected);
            blackboard.SetValue("TargetCoordinates", new EntityCoordinates(selected, Vector2.Zero));
            safety.AssignTarget(owner, selected, tactics);
        }
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entities) ||
            _entities.Deleted(target) ||
            !_entities.TryGetComponent<TransformComponent>(target, out var targetXform))
            return HTNOperatorStatus.Failed;

        if (_entities.TryGetComponent<MobStateComponent>(target, out var state) &&
            state.CurrentState != MobState.Alive)
            return HTNOperatorStatus.Finished;

        if (!_entities.GetComponent<TransformComponent>(owner).Coordinates
                .TryDistance(_entities, targetXform.Coordinates, out var distance))
            return HTNOperatorStatus.Failed;

        var steeringSystem = _entities.System<NPCSteeringSystem>();
        safety.ObserveTarget(owner, target, tactics,
            blackboard.GetValueOrDefault<float>(blackboard.GetVisionRadiusKey(_entities), _entities));
        tactics.EvadingWithoutTarget = false;
        var visible = tactics.ClearShot;
        if (safety.TryUseCover(owner, target, tactics, distance, out var canShoot))
        {
            _entities.RemoveComponent<NPCMeleeCombatComponent>(owner);
            // Native aiming can track the target during the short exit; the shot guard still requires arrival.
            if (canShoot || tactics.CoverPeeking)
            {
                safety.PrepareRangedCombat(owner, target);
                if (canShoot)
                    safety.CheckShooting(owner, target, tactics, visible, distance);
                else
                    tactics.SilentSince = _timing.CurTime;
            }
            else
            {
                _entities.RemoveComponent<NPCRangedCombatComponent>(owner);
                tactics.SilentSince = _timing.CurTime;
            }
            _entities.System<SharedCombatModeSystem>().SetInCombatMode(owner, true);
            return HTNOperatorStatus.Continuing;
        }
        if (!safety.RetryPath(owner, target, tactics, visible))
            return HTNOperatorStatus.Failed;
        var directMove = visible && tactics.DirectPathClear;
        if (!tactics.TargetVisible)
        {
            _entities.RemoveComponent<NPCRangedCombatComponent>(owner);
            _entities.RemoveComponent<NPCMeleeCombatComponent>(owner);
            if (_entities.TryGetComponent<Wh40kChaosThrowerComponent>(owner, out var hiddenThrower))
                TryThrow(owner, target, hiddenThrower);
            else
                safety.CancelThrowWarning(owner);
            tactics.SilentSince = _timing.CurTime;
            if (tactics.LastSeenPosition is not { } remembered ||
                !remembered.IsValid(_entities) ||
                _timing.CurTime - tactics.LastSeenAt >
                    (safety.IsAlertedTo(tactics, target) ? tactics.ThreatMemoryDuration : tactics.MemoryDuration) ||
                _entities.System<SharedTransformSystem>().ToMapCoordinates(remembered).MapId !=
                _entities.GetComponent<TransformComponent>(owner).MapID)
            {
                safety.ForgetTarget(owner, target, tactics);
                return HTNOperatorStatus.Finished;
            }
            var searching = steeringSystem.Register(owner, remembered);
            // Approach the remembered corner closely enough to see around it before waiting.
            searching.Range = 0.3f;
            searching.InRangeMaxSpeed = 0.03f;
            searching.DirectMove = false;
            searching.ArriveOnLineOfSight = false;
            if (searching.Status == SteeringStatus.InRange)
            {
                if (tactics.SearchArrivedAt == TimeSpan.Zero)
                    tactics.SearchArrivedAt = _timing.CurTime;
                if (_timing.CurTime - tactics.SearchArrivedAt >= tactics.SearchWaitDuration)
                {
                    safety.ForgetTarget(owner, target, tactics);
                    return HTNOperatorStatus.Finished;
                }
            }
            else
                tactics.SearchArrivedAt = TimeSpan.Zero;
            return HTNOperatorStatus.Continuing;
        }
        _entities.TryGetComponent<Wh40kChaosThrowerComponent>(owner, out var thrower);
        var restrained = thrower != null && safety.IsRestrained(target);
        var cannotEnsnare = thrower != null && !_entities.HasComponent<EnsnareableComponent>(target) &&
                            safety.GetBlastRadius(thrower.Prototype) == 0f;

        var meleeDistance = _entities.HasComponent<NPCMeleeCombatComponent>(owner) ? MeleeRange + 0.75f : MeleeRange;
        // A small exit margin lets an NPC brake after retreating without immediately switching back to ranged combat.
        if (distance <= meleeDistance || restrained || cannotEnsnare || thrower == null && !_entities.HasComponent<GunComponent>(owner))
        {
            safety.CancelThrowWarning(owner, true);
            _entities.RemoveComponent<NPCRangedCombatComponent>(owner);
            if (directMove)
            {
                var melee = _entities.EnsureComponent<NPCMeleeCombatComponent>(owner);
                melee.Target = target;
                melee.MissChance = blackboard.GetValueOrDefault<float>(NPCBlackboard.MeleeMissChance, _entities);
            }
            else
            {
                // Obstacle handling temporarily leaves combat mode; native melee shutdown would discard its route.
                _entities.RemoveComponent<NPCMeleeCombatComponent>(owner);
            }
            var steering = steeringSystem.Register(owner, new EntityCoordinates(target, Vector2.Zero));
            // Leave room to follow a moving target before it leaves the weapon's reach.
            steering.Range = directMove ? MathF.Max(0.6f, MeleeRange - 0.4f) : 0.6f;
            steering.InRangeMaxSpeed = 0.03f;
            steering.ArriveOnLineOfSight = false;
            steering.DirectMove = directMove;
            if (steering.DirectMove && steering.Status == SteeringStatus.NoPath)
                steering.Status = SteeringStatus.Moving;
        }
        else
        {
            _entities.RemoveComponent<NPCMeleeCombatComponent>(owner);
            var steering = steeringSystem.Register(owner, new EntityCoordinates(target, Vector2.Zero));

            steering.Range = visible ? MathF.Min(tactics.RangedRange, 6f) : 0.6f;
            steering.ArriveOnLineOfSight = visible && distance <= tactics.RangedRange;
            steering.LineOfSightTimeRequired = 0f;
            steering.DirectMove = directMove;
            // A weapon's range is not an arrival radius when a wall or closed door blocks the shot.
            if (thrower != null)
            {
                steering.Range = visible ? thrower.Range : 0.6f;
                steering.ArriveOnLineOfSight = visible && distance <= thrower.Range;
                TryThrow(owner, target, thrower);
            }
            else
            {
                var ranged = safety.PrepareRangedCombat(owner, target);
                // Native ranged AI marks an empty battery as unspecified even when it recharges itself.
                if (ranged.Status == CombatStatus.Unspecified)
                    ranged.Status = CombatStatus.Normal;
                safety.CheckShooting(owner, target, tactics, visible, distance);
            }
            if (visible && steering.Status == SteeringStatus.NoPath)
                steering.Status = SteeringStatus.Moving;
        }

        _entities.System<SharedCombatModeSystem>().SetInCombatMode(owner, true);
        return HTNOperatorStatus.Continuing;
    }

    private void TryThrow(EntityUid owner, EntityUid target, Wh40kChaosThrowerComponent thrower)
    {
        var safety = _entities.System<Wh40kChaosTacticsSystem>();
        if (!_entities.System<NPCCombatSystem>().Enabled || _timing.CurTime < thrower.NextThrow ||
            !_entities.System<ActionBlockerSystem>().CanAttack(owner, target))
        {
            safety.CancelThrowWarning(owner, true);
            return;
        }
        var blastRadius = safety.GetBlastRadius(thrower.Prototype);
        if (!safety.TryGetThrowAim(owner, target, thrower, out var aim))
        {
            safety.CancelThrowWarning(owner, true);
            return;
        }
        var transform = _entities.System<SharedTransformSystem>();
        var distance = Vector2.Distance(transform.GetMapCoordinates(owner).Position, aim.Position);
        if (distance < thrower.MinRange || distance > thrower.Range ||
            blastRadius == 0f && (safety.IsRestrained(target) || !_entities.HasComponent<EnsnareableComponent>(target)))
        {
            safety.CancelThrowWarning(owner, true);
            return;
        }

        var ignoreSafety = blastRadius > 0f && safety.CanIgnoreThrowSafety(owner, target, thrower);
        if (blastRadius > 0f && !ignoreSafety && !safety.IsSafeAttack(owner, aim, blastRadius))
        {
            safety.RecordBlockedThrow(owner, target, thrower);
            safety.CancelThrowWarning(owner);
            return;
        }
        if (!safety.PrepareThrow(owner, target, thrower, blastRadius > 0f, ref aim, ignoreSafety))
            return;
        // Recheck the announced, fixed landing point when the warning finishes.
        if (!_entities.System<SharedInteractionSystem>().InRangeUnobstructed(owner, aim, thrower.Range))
        {
            safety.CancelThrowWarning(owner, true);
            return;
        }
        if (blastRadius > 0f && !ignoreSafety && !safety.IsSafeAttack(owner, aim, blastRadius))
        {
            safety.RecordBlockedThrow(owner, target, thrower);
            safety.CancelThrowWarning(owner);
            return;
        }

        var item = _entities.SpawnEntity(thrower.Prototype, _entities.GetComponent<TransformComponent>(owner).Coordinates);
        if (!_entities.System<ActionBlockerSystem>().CanThrow(owner, item))
        {
            _entities.DeleteEntity(item);
            safety.CancelThrowWarning(owner, true);
            return;
        }

        var destination = transform.ToCoordinates(aim);
        if (_entities.TryGetComponent<Wh40kChaosGrenadeComponent>(item, out var grenade))
            grenade.Destination = destination;
        _entities.System<ThrowingSystem>().TryThrow(item, destination, user: owner,
            pushbackRatio: 0f, recoil: false,
            compensateFriction: !_entities.HasComponent<LandAtCursorComponent>(item));
        if (!_entities.HasComponent<ThrownItemComponent>(item))
        {
            _entities.DeleteEntity(item);
            safety.CancelThrowWarning(owner, true);
            return;
        }
        if (_entities.HasComponent<OnUseTimerTriggerComponent>(item))
            _entities.System<TriggerSystem>().StartTimer(item, owner);
        thrower.NextThrow = _timing.CurTime + thrower.Cooldown;
        safety.CompleteThrow(owner, thrower);
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entities.System<Wh40kChaosTacticsSystem>().CancelObstacleInteraction(owner);
        _entities.System<Wh40kChaosTacticsSystem>().CancelThrowWarning(owner, true);
        _entities.RemoveComponent<NPCRangedCombatComponent>(owner);
        _entities.RemoveComponent<NPCMeleeCombatComponent>(owner);
        _entities.System<NPCSteeringSystem>().Unregister(owner);
        _entities.System<SharedCombatModeSystem>().SetInCombatMode(owner, false);
        if (_entities.TryGetComponent<Wh40kChaosTacticsComponent>(owner, out var tactics))
        {
            _entities.System<Wh40kChaosTacticsSystem>().ClearAssignment(owner, tactics);
            _entities.System<Wh40kChaosTacticsSystem>().CancelCover(tactics);
            tactics.Target = null;
            tactics.TargetVisible = false;
            tactics.MeleeReadyAt = TimeSpan.Zero;
            tactics.NextTargetCheck = TimeSpan.Zero;
        }
        blackboard.Remove<EntityUid>(TargetKey);
    }
}
