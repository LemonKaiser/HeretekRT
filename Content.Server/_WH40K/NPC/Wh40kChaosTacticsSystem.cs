using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Events;
using Content.Server.NPC.Systems;
using Content.Shared.Explosion.Components;
using Content.Shared.Ensnaring.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Melee;
using Microsoft.Extensions.ObjectPool;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedDoAfterSystem _obstacleDoAfter = default!;

    private float _hazardAccumulator;
    // Steering runs on worker threads; every lookup must rent its own cleared buffer.
    private readonly ObjectPool<HashSet<Entity<NpcFactionMemberComponent>>> _factionSetPool =
        new DefaultObjectPool<HashSet<Entity<NpcFactionMemberComponent>>>(new SetPolicy<Entity<NpcFactionMemberComponent>>());

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, ShotAttemptedEvent>(OnShotAttempt);
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, NPCSteeringEvent>(OnSteering);
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, AttemptMeleeEvent>(OnMeleeAttempt);
        InitializeReactions();
        InitializePatrol();
        InitializeSquad();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _hazardAccumulator += frameTime;
        if (_hazardAccumulator < 0.2f)
            return;
        _hazardAccumulator = 0f;

        var hazards = false;
        var grenades = EntityQueryEnumerator<ActiveTimerTriggerComponent, ExplosiveComponent>();
        while (grenades.MoveNext(out _, out _, out var explosive))
        {
            if (explosive.Exploded)
                continue;
            hazards = true;
            break;
        }
        var query = EntityQueryEnumerator<Wh40kChaosTacticsComponent, ActiveNPCComponent>();
        while (query.MoveNext(out var uid, out var comp, out _))
        {
            if (Paused(uid))
                continue;
            comp.EscapeDirection = hazards ? GetEscapeDirection(uid, comp) : Vector2.Zero;
            comp.SeparationDirection = GetSeparationDirection(uid, comp);
            CheckMovementProgress(uid, comp);
            CheckMeleeWarning(uid, comp);
            if (comp.EscapeDirection == Vector2.Zero && comp.SeparationDirection == Vector2.Zero)
            {
                if (comp.EvadingWithoutTarget && comp.Target == null)
                    _steering.Unregister(uid);
                comp.EvadingWithoutTarget = false;
                continue;
            }

            if (!TryComp<NPCSteeringComponent>(uid, out var steering))
            {
                steering = _steering.Register(uid, Transform(uid).Coordinates);
                comp.EvadingWithoutTarget = comp.Target == null;
            }
            // An unreachable combat/idle destination must not prevent emergency movement.
            if (steering.Status == SteeringStatus.NoPath)
                steering.Status = SteeringStatus.Moving;
        }
    }

    public float GetBlastRadius(EntProtoId prototype)
    {
        return _prototypes.Index(prototype).TryComp<ExplosiveComponent>(out var explosive, _factory)
            ? GetBlastRadius(explosive)
            : 0f;
    }

    private float GetBlastRadius(ExplosiveComponent explosive)
    {
        // The explosion system's estimate is approximate; allow for tiles and mob fixtures.
        return _explosion.IntensityToRadius(explosive.TotalIntensity, explosive.IntensitySlope,
            explosive.MaxIntensity) + 1.25f;
    }

    public bool IsSafeAttack(EntityUid owner, MapCoordinates aim, float blastRadius = 0f)
    {
        var origin = _transform.GetMapCoordinates(owner);
        if (origin.MapId != aim.MapId)
            return false;

        var delta = aim.Position - origin.Position;
        var distance = delta.Length();
        if (distance <= blastRadius || distance <= 0.01f)
            return false;

        TryComp<Wh40kChaosTacticsComponent>(owner, out var tactics);
        var range = blastRadius > 0 ? distance : MathF.Max(distance, tactics?.RangedRange ?? distance);
        var direction = delta / distance;
        var spread = (float) Math.Tan(tactics?.FriendlyFireSpread.Theta ?? 0);
        var friends = _factionSetPool.Get();
        try
        {
            _lookup.GetEntitiesInRange(origin, range + blastRadius + 1f, friends);
            foreach (var (friend, faction) in friends)
            {
                if (friend == owner || !_factions.IsEntityFriendly(owner, (friend, faction)) ||
                    TryComp<MobStateComponent>(friend, out var state) && state.CurrentState != MobState.Alive)
                    continue;
                var position = _transform.GetMapCoordinates(friend);
                if (position.MapId != origin.MapId)
                    continue;

                var velocity = TryComp<PhysicsComponent>(friend, out var body) ? body.LinearVelocity : Vector2.Zero;
                // Check current positions and a short prediction, without assuming allies keep moving for the whole fuse.
                for (var step = 0; step < 2; step++)
                {
                    var predicted = position.Position + velocity * (step * 0.25f);
                    if (blastRadius > 0 && Vector2.Distance(predicted, aim.Position) <= blastRadius)
                        return false;

                    var offset = predicted - origin.Position;
                    var along = Vector2.Dot(offset, direction);
                    if (along < 0 || along > range + 0.5f)
                        continue;

                    var width = 0.65f + along * spread;
                    if ((offset - direction * along).LengthSquared() <= width * width)
                        return false;
                }
            }
        }
        finally
        {
            _factionSetPool.Return(friends);
        }
        return true;
    }

    private void OnShotAttempt(EntityUid uid, Wh40kChaosTacticsComponent comp, ref ShotAttemptedEvent args)
    {
        if (args.User != uid || args.Used.Comp.ShootCoordinates is not { } coordinates)
            return;

        var aim = _transform.ToMapCoordinates(coordinates);
        var origin = _transform.GetMapCoordinates(uid);
        var targetPoint = comp.Target is { } enemy && !Deleted(enemy) ? _transform.GetMapCoordinates(enemy) : aim;
        if (origin.MapId != targetPoint.MapId ||
            Vector2.DistanceSquared(origin.Position, targetPoint.Position) > comp.RangedRange * comp.RangedRange ||
            _timing.CurTime < comp.ResumeFire || _timing.CurTime < comp.RegroupUntil && !origin.InRange(targetPoint, 3f) ||
            comp.CoverPosition != null && (!comp.CoverPeeking || comp.CoverPhaseUntil == TimeSpan.Zero))
        {
            args.Cancel();
            return;
        }

        if (comp.BurstDuration > TimeSpan.Zero && comp.BurstEnd != TimeSpan.Zero &&
            _timing.CurTime >= comp.BurstEnd)
        {
            comp.BurstEnd = TimeSpan.Zero;
            comp.ResumeFire = _timing.CurTime + comp.BurstPause;
            args.Cancel();
        }
    }

    private void OnGunShot(EntityUid uid, Wh40kChaosTacticsComponent comp, ref GunShotEvent args)
    {
        if (args.User == uid && comp.CoverPeeking && comp.CoverFirstFireAt == TimeSpan.Zero)
            comp.CoverFirstFireAt = _timing.CurTime;
        if (args.User == uid && comp.BurstDuration > TimeSpan.Zero && comp.BurstEnd == TimeSpan.Zero)
            comp.BurstEnd = _timing.CurTime + comp.BurstDuration;
    }

    private void OnSteering(EntityUid uid, Wh40kChaosTacticsComponent comp, ref NPCSteeringEvent args)
    {
        var direction = comp.EscapeDirection;
        // Follow the selected route into shelter instead of interrupting it with a sideways dodge.
        if (direction == Vector2.Zero && comp.CoverPosition != null)
        {
            StopTacticalMovement(comp, args.Steering);
            return;
        }
        // Native seeking owns obstacle interactions. Sidesteps must not interrupt prying or pull a crowd off its path.
        if (direction == Vector2.Zero && (IsClearingObstacle(args.Steering) ||
            comp.Target != null && !args.Steering.DirectMove))
        {
            StopTacticalMovement(comp, args.Steering);
            return;
        }
        var separating = false;
        var origin = _transform.GetMapCoordinates(uid);
        var dodging = direction == Vector2.Zero && _timing.CurTime < comp.DodgeUntil;
        if (dodging)
        {
            direction = comp.DodgeDirection;
            var pursuit = comp.Target is { } enemyTarget && !Deleted(enemyTarget)
                ? _transform.GetMapCoordinates(enemyTarget)
                : comp.ThreatPosition is { } threat && threat.IsValid(EntityManager)
                    ? _transform.ToMapCoordinates(threat)
                    : (MapCoordinates?) null;
            if (pursuit is { } target && target.MapId == origin.MapId)
            {
                var toward = target.Position - origin.Position;
                var attackRange = HasComp<NPCMeleeCombatComponent>(uid)
                    ? Comp<MeleeWeaponComponent>(uid).Range
                    : TryComp<Wh40kChaosThrowerComponent>(uid, out var throwing) ? throwing.Range : comp.RangedRange;
                if (toward.LengthSquared() > attackRange * attackRange)
                {
                    var forward = Vector2.Normalize(toward);
                    // Keep a lateral component while closing the distance, even if another player fired the shot.
                    var side = comp.DodgeDirection - forward * Vector2.Dot(comp.DodgeDirection, forward);
                    direction = forward + side * 0.8f;
                }
            }
        }

        if (direction == Vector2.Zero && _timing.CurTime < comp.RegroupUntil &&
            comp.Target is { } regroupTarget && !Deleted(regroupTarget) &&
            comp.RegroupOrigin is { } regroupOrigin && regroupOrigin.IsValid(EntityManager))
        {
            var targetPosition = _transform.GetMapCoordinates(regroupTarget);
            var from = _transform.ToMapCoordinates(regroupOrigin);
            if (origin.InRange(targetPosition, 3f))
                comp.RegroupUntil = TimeSpan.Zero;
            else if (!origin.InRange(from, 2f))
            {
                Array.Clear(args.Steering.Interest);
                args.Steering.CanSeek = false;
                comp.TacticalDirection = Vector2.Zero;
                return;
            }
            else
                direction = origin.Position - targetPosition.Position;
        }

        if (direction == Vector2.Zero && (comp.Target != null && !comp.TargetVisible ||
            _timing.CurTime < comp.MovementRetryAfter))
        {
            StopTacticalMovement(comp, args.Steering);
            return;
        }

        if (direction == Vector2.Zero && !HasComp<NPCMeleeCombatComponent>(uid) &&
            comp.Target is { } enemy && !Deleted(enemy))
        {
            var aim = _transform.GetMapCoordinates(enemy);
            var delta = aim.Position - origin.Position;
            var distance = delta.Length();
            if (aim.MapId != origin.MapId || distance <= 2f ||
                !_interaction.InRangeUnobstructed(uid, enemy, MathF.Max(distance, comp.RangedRange)))
            {
                StopTacticalMovement(comp, args.Steering);
                return;
            }

            var retreat = TryComp<Wh40kChaosThrowerComponent>(uid, out var thrower)
                ? MathF.Max(comp.RetreatRange, thrower.MinRange + 0.5f)
                : comp.RetreatRange;
            var blastRadius = thrower == null ? 0f : GetBlastRadius(thrower.Prototype);
            // Reach the weapon's effective range before making room beside another shooter.
            if (distance > (thrower?.Range ?? comp.RangedRange))
            {

                args.Steering.ArriveOnLineOfSight = false;
                StopTacticalMovement(comp, args.Steering);
                return;
            }
            if (distance < retreat &&
                !(TryComp<EnsnareableComponent>(enemy, out var ensnared) && ensnared.IsEnsnared))
                direction = -delta;
            // Grenadiers still need a safe throwing lane and blast area.
            else if (thrower != null && blastRadius > 0 && !CanIgnoreThrowSafety(uid, enemy, thrower) && !IsSafeAttack(uid, aim, blastRadius))
            {

                direction = new Vector2(-delta.Y, delta.X);
            }
            // Arrival at a firing position must also discard interest left by ordinary path following.
            else if (distance <= (thrower?.Range ?? comp.RangedRange))
            {

                Array.Clear(args.Steering.Interest);
            }
            // A short range gun must step sideways around a neighbour instead of backing out of range forever.
            if (direction == Vector2.Zero && comp.SeparationDirection != Vector2.Zero)
            {
                direction = comp.SeparationDirection;
                separating = true;

                if (thrower == null && (distance > comp.RangedRange - 0.3f && Vector2.Dot(direction, -delta) > 0 ||
                    distance < 2.9f && Vector2.Dot(direction, delta) > 0))
                {
                    var side = new Vector2(-delta.Y, delta.X);
                    var preference = Vector2.Dot(direction, side);
                    direction = side * (MathF.Abs(preference) > 0.01f ? MathF.Sign(preference) :
                        (uid.GetHashCode() & 1) == 0 ? 1 : -1);
                }
            }
        }

        if (direction == Vector2.Zero)
        {
            direction = comp.SeparationDirection;
            separating = direction != Vector2.Zero;
        }
        // Native blending never reaches exactly zero; stale sidestep interest otherwise keeps the mob walking.
        if (direction.LengthSquared() < 0.01f)
        {
            StopTacticalMovement(comp, args.Steering);
            return;
        }

        direction = FindClearDirection(uid, comp, Vector2.Normalize(direction), args.OffsetRotation, args.Steering.Radius, separating, dodging);
        if (direction == Vector2.Zero)
        {
            Array.Clear(args.Steering.Interest);
            return;
        }

        var localDirection = Vector2.Normalize(args.OffsetRotation.RotateVec(direction));
        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
            args.Steering.Interest[i] = MathF.Max(0f, Vector2.Dot(localDirection, NPCSteeringSystem.Directions[i]));
        args.Steering.CanSeek = false;
    }

    private Vector2 GetSeparationDirection(EntityUid uid, Wh40kChaosTacticsComponent comp)
    {
        comp.SeparationNeighbours.Clear();
        var origin = _transform.GetMapCoordinates(uid);
        var away = Vector2.Zero;
        var closestDistance = float.MaxValue;
        var closestDirection = Vector2.Zero;
        var friends = _factionSetPool.Get();
        try
        {
            _lookup.GetEntitiesInRange(origin, 1.8f, friends);
            foreach (var (friend, faction) in friends)
            {
                if (friend == uid || !_factions.IsEntityFriendly(uid, (friend, faction)) ||
                    TryComp<MobStateComponent>(friend, out var state) && state.CurrentState != MobState.Alive)
                    continue;
                var position = _transform.GetMapCoordinates(friend);
                if (position.MapId != origin.MapId)
                    continue;
                comp.SeparationNeighbours.Add(position.Position);
                // One member of each pair yields; moving both makes dense groups chase and push each other.
                if (friend.GetHashCode() > uid.GetHashCode())
                    continue;
                var delta = origin.Position - position.Position;
                var distance = delta.Length();
                if (distance >= 0.9f)
                    continue;
                var normal = distance > 0.01f ? delta / distance : new Angle(uid.GetHashCode() * 2.39996323).ToVec();
                away += normal * (0.9f - distance);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestDirection = normal;
                }
            }
        }
        finally
        {
            _factionSetPool.Return(friends);
        }
        // Close neighbours and symmetric crowds must not cancel each other's separation.
        if (closestDistance < 0.7f || away.LengthSquared() < 0.01f && closestDistance < 0.85f)
            return closestDirection;
        return away.LengthSquared() > 0.0025f ? away : Vector2.Zero;
    }

    private static void StopTacticalMovement(Wh40kChaosTacticsComponent comp, NPCSteeringComponent steering)
    {
        if (comp.TacticalDirection == Vector2.Zero)
            return;
        comp.TacticalDirection = Vector2.Zero;

        Array.Clear(steering.Interest);
    }

    private bool IsClearingObstacle(NPCSteeringComponent steering)
    {
        return steering.DoAfterId != null && _obstacleDoAfter.GetStatus(steering.DoAfterId) == DoAfterStatus.Running;
    }

    private Vector2 FindClearDirection(EntityUid uid, Wh40kChaosTacticsComponent comp, Vector2 desired,
        Angle offsetRotation, float radius, bool separating, bool dodging)
    {

        if (_timing.CurTime < comp.NextDirectionUpdate)
            return comp.TacticalDirection;

        var best = Vector2.Zero;
        var bestScore = float.NegativeInfinity;
        var origin = _transform.GetMapCoordinates(uid);
        var rangedRange = TryComp<Wh40kChaosThrowerComponent>(uid, out var thrower) ? thrower.Range : comp.RangedRange;
        var rangedTarget = (separating || dodging) && comp.TargetVisible && !HasComp<NPCMeleeCombatComponent>(uid) &&
                           comp.Target is { } enemy && !Deleted(enemy)
            ? _transform.GetMapCoordinates(enemy)
            : (MapCoordinates?) null;
        foreach (var local in NPCSteeringSystem.Directions)
        {
            var candidate = (-offsetRotation).RotateVec(local);
            if (comp.EscapeDirection == Vector2.Zero && _timing.CurTime < comp.AvoidUntil &&
                Vector2.Dot(candidate, comp.AvoidDirection) > 0.7f)
                continue;
            var destination = origin.Position + candidate;
            if (rangedTarget is { } target && origin.InRange(target, rangedRange) &&
                (Vector2.Distance(destination, target.Position) < 2.75f ||
                 Vector2.Distance(destination, target.Position) > rangedRange))
                continue;
            var alignment = Vector2.Dot(candidate, desired);
            // In a corner, separation may need to go around a neighbour in the opposite direction.
            if (alignment < -0.1f && comp.EscapeDirection != Vector2.Zero)
            // Escaping a live grenade must still avoid advancing towards its blast area.
            // Prefer an empty neighbouring spot instead of pushing into the next member of the crowd.
                continue;
            var score = alignment + (dodging ? 0f : Vector2.Dot(candidate, comp.TacticalDirection) * 0.35f);
            if (separating)
            {

                foreach (var neighbour in comp.SeparationNeighbours)
                    score -= MathF.Max(0f, 0.9f - Vector2.Distance(destination, neighbour)) * 4f;
            }
            if (score <= bestScore)
                continue;
            if (!HasClearance(uid, candidate, radius))
                continue;
            bestScore = score;
            best = candidate;
        }
        comp.TacticalDirection = best;
        comp.NextDirectionUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.2);
        return best;
    }

    private bool HasClearance(EntityUid uid, Vector2 direction, float radius)
    {
        var origin = _transform.GetMapCoordinates(uid);
        var edge = new Vector2(-direction.Y, direction.X) * (radius + 0.05f);
        // A centre ray alone admits gaps narrower than the mob and cuts corners against walls.
        for (var i = -1; i <= 1; i++)
        {
            var start = new MapCoordinates(origin.Position + edge * i, origin.MapId);
            var end = new MapCoordinates(start.Position + direction * 1.25f, origin.MapId);
            if (!_interaction.InRangeUnobstructed(start, end, 1.5f,
                    CollisionGroup.Impassable | CollisionGroup.InteractImpassable, e => e == uid))
                return false;
        }
        return true;
    }

    private Vector2 GetEscapeDirection(EntityUid uid, Wh40kChaosTacticsComponent comp)
    {
        var direction = Vector2.Zero;
        var closestDirection = Vector2.Zero;
        var closestDistance = float.MaxValue;
        var origin = _transform.GetMapCoordinates(uid);
        var opposing = false;
        foreach (var (grenade, _) in _lookup.GetEntitiesInRange<ActiveTimerTriggerComponent>(origin, 12f))
        {
            if (!TryComp<ExplosiveComponent>(grenade, out var explosive) || explosive.Exploded ||
                !_interaction.InRangeUnobstructed(uid, grenade, 12f))
                continue;

            var point = _transform.GetMapCoordinates(grenade);
            if (TryComp<ThrownItemComponent>(grenade, out var thrown) && !thrown.Landed &&
                TryComp<Wh40kChaosGrenadeComponent>(grenade, out var grenadeComp) &&
                grenadeComp.Destination is { } destination && destination.IsValid(EntityManager))
                point = _transform.ToMapCoordinates(destination);

            var away = origin.Position - point.Position;
            var distance = away.Length();
            if (point.MapId != origin.MapId || distance > GetBlastRadius(explosive) + 1f)
                continue;

            if (away.LengthSquared() < 0.01f)
                away = comp.LastSeenPosition is { } remembered && remembered.IsValid(EntityManager)
                    ? origin.Position - _transform.ToMapCoordinates(remembered).Position
                    : Vector2.UnitX;
            var normal = Vector2.Normalize(away.LengthSquared() < 0.01f ? Vector2.UnitX : away);
            opposing |= closestDirection != Vector2.Zero && Vector2.Dot(closestDirection, normal) < -0.5f;
            direction += normal / MathF.Max(distance, 0.5f);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestDirection = normal;
            }
        }

        if (closestDirection == Vector2.Zero || !opposing && direction.LengthSquared() >= 0.01f)
            return direction;
        var side = new Vector2(-closestDirection.Y, closestDirection.X);
        // Leave the shared axis and preserve the chosen side when equally close grenades swap order.
        return Vector2.Dot(side, comp.TacticalDirection) < 0f ? -side : side;
    }
}
