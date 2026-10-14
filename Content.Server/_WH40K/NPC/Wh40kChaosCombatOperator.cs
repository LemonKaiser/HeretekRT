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
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
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
        if (safety.TryGetThreatTarget(owner, tactics, out var attacker))
            blackboard.SetValue(TargetKey, attacker);
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
        var visible = _entities.System<SharedInteractionSystem>().InRangeUnobstructed(owner, target,
            distance + 0.1f, CollisionGroup.Impassable | CollisionGroup.InteractImpassable);
        if (!safety.RetryPath(owner, target, tactics, visible))
            return HTNOperatorStatus.Failed;
        if (!tactics.TargetVisible)
        {
            _entities.RemoveComponent<NPCRangedCombatComponent>(owner);
            _entities.RemoveComponent<NPCMeleeCombatComponent>(owner);
            safety.CancelThrowWarning(owner);
            tactics.SilentSince = _timing.CurTime;
            if (tactics.LastSeenPosition is not { } remembered ||
                !remembered.IsValid(_entities) ||
                _timing.CurTime - tactics.LastSeenAt >
                    (safety.IsAlertedTo(tactics, target) ? tactics.ThreatMemoryDuration : tactics.MemoryDuration) ||
                _entities.System<SharedTransformSystem>().ToMapCoordinates(remembered).MapId !=
                _entities.GetComponent<TransformComponent>(owner).MapID)
            {
                safety.IgnoreTarget(owner, target, tactics);
                return HTNOperatorStatus.Finished;
            }
            var searching = steeringSystem.Register(owner, remembered);
            searching.Range = 0.6f;
            searching.InRangeMaxSpeed = 0.03f;
            searching.DirectMove = false;
            searching.ArriveOnLineOfSight = false;
            return HTNOperatorStatus.Continuing;
        }
        _entities.TryGetComponent<Wh40kChaosThrowerComponent>(owner, out var thrower);
        var restrained = thrower != null && safety.IsRestrained(target);

        var meleeDistance = _entities.HasComponent<NPCMeleeCombatComponent>(owner) ? MeleeRange + 0.75f : MeleeRange;
        // A small exit margin lets an NPC brake after retreating without immediately switching back to ranged combat.
        if (distance <= meleeDistance || restrained || thrower == null && !_entities.HasComponent<GunComponent>(owner))
        {
            safety.CancelThrowWarning(owner);
            _entities.RemoveComponent<NPCRangedCombatComponent>(owner);
            var melee = _entities.EnsureComponent<NPCMeleeCombatComponent>(owner);
            melee.Target = target;
            melee.MissChance = blackboard.GetValueOrDefault<float>(NPCBlackboard.MeleeMissChance, _entities);
            var steering = steeringSystem.Register(owner, new EntityCoordinates(target, Vector2.Zero));
            steering.Range = visible ? MeleeRange : 0.6f;
            steering.InRangeMaxSpeed = 0.03f;
            steering.ArriveOnLineOfSight = false;
            steering.DirectMove = visible;
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
            steering.DirectMove = visible;
            // A weapon's range is not an arrival radius when a wall or closed door blocks the shot.
            if (thrower != null)
            {
                steering.Range = visible ? thrower.Range : 0.6f;
                steering.ArriveOnLineOfSight = visible && distance <= thrower.Range;
                TryThrow(owner, target, targetXform, distance, thrower);
            }
            else
            {
                var ranged = _entities.EnsureComponent<NPCRangedCombatComponent>(owner);
                ranged.Target = target;
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

    private void TryThrow(EntityUid owner, EntityUid target, TransformComponent targetXform,
        float distance, Wh40kChaosThrowerComponent thrower)
    {
        var safety = _entities.System<Wh40kChaosTacticsSystem>();
        var blastRadius = safety.GetBlastRadius(thrower.Prototype);
        if (!_entities.System<NPCCombatSystem>().Enabled ||
            _timing.CurTime < thrower.NextThrow || distance < thrower.MinRange || distance > thrower.Range ||
            blastRadius == 0f && safety.IsRestrained(target) ||
            !_entities.System<ActionBlockerSystem>().CanAttack(owner, target) ||
            !_entities.System<SharedInteractionSystem>().InRangeUnobstructed(owner, target, thrower.Range))
        {
            safety.CancelThrowWarning(owner);
            return;
        }

        var transform = _entities.System<SharedTransformSystem>();
        var aim = transform.GetMapCoordinates(target, targetXform);
        if (_entities.TryGetComponent<PhysicsComponent>(target, out var body))
        {
            var lead = body.LinearVelocity * MathF.Min(distance / 10f, 0.6f);
            if (lead.LengthSquared() > 1.25f * 1.25f)
                lead = Vector2.Normalize(lead) * 1.25f;
            aim = new MapCoordinates(aim.Position + lead, aim.MapId);
        }

        if (blastRadius > 0f && !safety.IsSafeAttack(owner, aim, blastRadius) ||
            !_entities.System<SharedInteractionSystem>().InRangeUnobstructed(owner, aim, thrower.Range))
        {
            safety.CancelThrowWarning(owner);
            return;
        }
        if (!safety.PrepareThrow(owner, target, thrower, blastRadius > 0f, ref aim))
            return;
        // Recheck the announced, fixed landing point when the warning finishes.
        if (blastRadius > 0f && !safety.IsSafeAttack(owner, aim, blastRadius) ||
            !_entities.System<SharedInteractionSystem>().InRangeUnobstructed(owner, aim, thrower.Range))
        {
            safety.CancelThrowWarning(owner);
            return;
        }

        var item = _entities.SpawnEntity(thrower.Prototype, _entities.GetComponent<TransformComponent>(owner).Coordinates);
        if (!_entities.System<ActionBlockerSystem>().CanThrow(owner, item))
        {
            _entities.DeleteEntity(item);
            safety.CancelThrowWarning(owner);
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
            safety.CancelThrowWarning(owner);
            return;
        }
        if (_entities.HasComponent<OnUseTimerTriggerComponent>(item))
            _entities.System<TriggerSystem>().StartTimer(item, owner);
        thrower.NextThrow = _timing.CurTime + thrower.Cooldown;
        safety.CompleteThrow(thrower);
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entities.System<Wh40kChaosTacticsSystem>().CancelThrowWarning(owner);
        _entities.RemoveComponent<NPCRangedCombatComponent>(owner);
        _entities.RemoveComponent<NPCMeleeCombatComponent>(owner);
        _entities.System<NPCSteeringSystem>().Unregister(owner);
        _entities.System<SharedCombatModeSystem>().SetInCombatMode(owner, false);
        if (_entities.TryGetComponent<Wh40kChaosTacticsComponent>(owner, out var tactics))
        {
            tactics.Target = null;
            tactics.TargetVisible = false;
            tactics.MeleeReadyAt = TimeSpan.Zero;
        }
        blackboard.Remove<EntityUid>(TargetKey);
    }
}
