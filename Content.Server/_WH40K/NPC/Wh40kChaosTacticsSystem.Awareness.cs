using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;

    [ViewVariables] public long SightCheckCount;
    [ViewVariables] public long MovementRayCount;
    [ViewVariables] public long CombatRayCount;
    [ViewVariables] public long PathRetryCount;

    public void CancelObstacleInteraction(EntityUid uid)
    {
        if (!TryComp<NPCSteeringComponent>(uid, out var steering) || steering.DoAfterId is not { } id)
            return;
        if (_obstacleDoAfter.GetStatus(id) == DoAfterStatus.Running)
            _obstacleDoAfter.Cancel(id);
        steering.DoAfterId = null;
    }

    private bool HasClearShot(EntityUid uid, EntityUid target, float distance)
    {
        CombatRayCount++;
        return _interaction.InRangeUnobstructed(uid, target, distance + 0.1f,
            GetShotMask(uid));
    }

    private CollisionGroup GetShotMask(EntityUid uid)
        => HasComp<HitscanBatteryAmmoProviderComponent>(uid) ? CollisionGroup.Opaque :
            CollisionGroup.Impassable | CollisionGroup.BulletImpassable;

    public NPCRangedCombatComponent PrepareRangedCombat(EntityUid uid, EntityUid target)
    {
        var ranged = EnsureComp<NPCRangedCombatComponent>(uid);
        ranged.Target = target;
        ranged.BulletMask = GetShotMask(uid);
        return ranged;
    }

    public bool CanMoveDirectly(EntityUid uid, EntityUid target)
        => CanMoveDirectly(uid, new EntityCoordinates(target, System.Numerics.Vector2.Zero));

    public bool CanMoveDirectly(EntityUid uid, EntityCoordinates destination)
    {
        if (!TryComp<PhysicsComponent>(uid, out var body))
            return false;
        var start = _transform.GetMapCoordinates(uid);
        var end = _transform.ToMapCoordinates(destination);
        if (start.MapId != end.MapId)
            return false;
        var delta = end.Position - start.Position;
        var distance = delta.Length();
        if (distance < 0.01f)
            return true;
        var radius = CompOrNull<NPCSteeringComponent>(uid)?.Radius ?? 0.35f;
        var edge = new Vector2(-delta.Y, delta.X) / distance * (radius + 0.05f);
        for (var i = -1; i <= 1; i++)
        {
            MovementRayCount++;
            if (!_interaction.InRangeUnobstructed(
                    new MapCoordinates(start.Position + edge * i, start.MapId),
                    new MapCoordinates(end.Position + edge * i, end.MapId), distance + 0.1f,
                    (CollisionGroup) body.CollisionMask,
                    e => e == uid || e == destination.EntityId || HasComp<MobStateComponent>(e)))
                return false;
        }
        return true;
    }

    public void ObserveTarget(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp, float vision)
    {
        if (comp.Target != target)
        {
            CancelThrowWarning(uid, true);
            if (comp.CoverPosition != null)
                CancelCover(comp);
            comp.CampPosition = null;
            comp.Target = target;
            comp.LastSeenPosition = IsAlertedTo(comp, target) ? comp.ThreatPosition : null;
            if (comp.LastSeenPosition != null)
                comp.LastSeenAt = comp.ThreatObservedAt;
            comp.TargetVisible = false;
            comp.NextSightCheck = TimeSpan.Zero;
            comp.PathRetries = 0;
            comp.PathProgressPosition = Transform(uid).Coordinates.Position;
            comp.NextPathRetry = TimeSpan.Zero;
            comp.SilentSince = _timing.CurTime;
            comp.MeleeReadyAt = TimeSpan.Zero;
            comp.SearchArrivedAt = TimeSpan.Zero;
            if (TryComp<NPCRangedCombatComponent>(uid, out var ranged))
            {
                ranged.TargetInLOS = false;
                ranged.LOSAccumulator = 0f;
                ranged.ShootAccumulator = 0f;
            }
        }
        if (_timing.CurTime < comp.NextSightCheck)
            return;
        comp.NextSightCheck = _timing.CurTime + TimeSpan.FromSeconds(0.2);
        SightCheckCount++;
        comp.TargetVisible = CanSeeTarget(uid, target, comp, vision);
        comp.DirectPathClear = comp.TargetVisible && CanMoveDirectly(uid, target);
        var distance = Vector2.Distance(_transform.GetMapCoordinates(uid).Position, _transform.GetMapCoordinates(target).Position);
        comp.ClearShot = comp.TargetVisible && HasClearShot(uid, target, distance);
        if (!comp.TargetVisible)
        {
            if (TryComp<Wh40kChaosThrowerComponent>(uid, out var thrower))
                thrower.SafetyBlockedTarget = null;
            return;
        }
        comp.SearchArrivedAt = TimeSpan.Zero;
        if (comp.DirectPathClear || comp.ClearShot &&
            (HasComp<GunComponent>(uid) && distance <= comp.RangedRange ||
             TryComp<Wh40kChaosThrowerComponent>(uid, out var throwing) && distance <= throwing.Range))
            CancelObstacleInteraction(uid);
        var targetTransform = Transform(target);
        comp.LastSeenPosition = _transform.WithEntityId(targetTransform.Coordinates,
            targetTransform.GridUid ?? targetTransform.MapUid!.Value);
        comp.LastSeenAt = _timing.CurTime;
        if (HasComp<Wh40kChaosThrowerComponent>(uid) &&
            (comp.CampPosition is not { } camp || !camp.IsValid(EntityManager) ||
             !camp.TryDistance(EntityManager, comp.LastSeenPosition.Value, out var travel) || travel > 0.75f))
        {
            comp.CampPosition = comp.LastSeenPosition;
            comp.StationarySince = _timing.CurTime;
        }
    }

    public void IgnoreTarget(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp)
    {
        ForgetTarget(uid, target, comp);
        comp.IgnoredTarget = target;
        comp.IgnoreUntil = _timing.CurTime + TimeSpan.FromSeconds(5);
    }

    public void ForgetTarget(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp)
    {
        CancelThrowWarning(uid, true);
        comp.LastSeenPosition = null;
        comp.TargetVisible = false;
        comp.ClearShot = false;
        comp.DirectPathClear = false;
        comp.MeleeReadyAt = TimeSpan.Zero;
        comp.SearchArrivedAt = TimeSpan.Zero;
        if (comp.IgnoredTarget == target)
            comp.IgnoredTarget = null;
        if (comp.ThreatTarget == target)
        {
            comp.ThreatTarget = null;
            comp.ThreatPosition = null;
            comp.ThreatUntil = TimeSpan.Zero;
        }
    }

    public bool RetryPath(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp, bool clearShot)
    {
        var position = Transform(uid).Coordinates.Position;
        if (Vector2.Distance(position, comp.PathProgressPosition) > 0.5f)
        {
            comp.PathProgressPosition = position;
            comp.PathRetries = 0;
        }
        if (!TryComp<NPCSteeringComponent>(uid, out var steering) ||
            IsClearingObstacle(steering) ||
            steering.Status != SteeringStatus.NoPath && (clearShot || steering.FailedPathCount < NPCSteeringComponent.FailedPathLimit) ||
            _timing.CurTime < comp.NextPathRetry || !_blocker.CanMove(uid))
            return true;
        comp.NextPathRetry = _timing.CurTime + TimeSpan.FromSeconds(1.25);
        PathRetryCount++;
        if (++comp.PathRetries >= 3)
        {
            IgnoreTarget(uid, target, comp);
            return false;
        }
        _steering.Unregister(uid);
        return true;
    }

    public void CheckShooting(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp, bool clearShot, float distance)
    {
        if (_timing.CurTime < comp.NextFireCheck)
            return;
        comp.NextFireCheck = _timing.CurTime + TimeSpan.FromSeconds(0.2);
        if (!TryComp<GunComponent>(uid, out var gun) || !TryComp<NPCRangedCombatComponent>(uid, out var ranged))
            return;
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(uid, ref ammo);
        if (gun.LastFire != comp.ObservedLastFire || !clearShot || distance > comp.RangedRange ||
            ammo.Count <= 0 || _timing.CurTime < gun.NextFire || _timing.CurTime < comp.ResumeFire ||
            HasComp<MeleeWeaponComponent>(uid) && Comp<MeleeWeaponComponent>(uid).NextAttack > _timing.CurTime ||
            !_blocker.CanAttack(uid, target) || !EntityManager.System<NPCCombatSystem>().Enabled)
        {
            comp.ObservedLastFire = gun.LastFire;
            comp.SilentSince = _timing.CurTime;
            return;
        }
        if (_timing.CurTime - comp.SilentSince < TimeSpan.FromSeconds(2.5))
            return;

        RemComp<NPCRangedCombatComponent>(uid);
        // Recreate only the aiming state; retain ammunition, weapon cooldowns and burst pauses.
        ranged = PrepareRangedCombat(uid, target);
        ranged.Status = CombatStatus.Normal;
        gun.ShootCoordinates = null;
        gun.Target = target;
        comp.FireRecoveries++;
        comp.SilentSince = _timing.CurTime;
    }

    private void CheckMovementProgress(EntityUid uid, Wh40kChaosTacticsComponent comp)
    {
        var position = Transform(uid).Coordinates.Position;
        if (comp.TacticalDirection == Vector2.Zero || Vector2.Distance(position, comp.ProgressPosition) > 0.2f)
        {
            comp.ProgressPosition = position;
            comp.ProgressSince = _timing.CurTime;
            return;
        }
        if (_timing.CurTime - comp.ProgressSince < TimeSpan.FromSeconds(1.25) || !_blocker.CanMove(uid))
            return;
        // Keep a chosen escape side briefly while native collision avoidance stays active.
        comp.AvoidDirection = comp.TacticalDirection;
        comp.AvoidUntil = _timing.CurTime + TimeSpan.FromSeconds(1.5);
        comp.NextDirectionUpdate = TimeSpan.Zero;
        comp.TacticalDirection = Vector2.Zero;
        comp.ProgressSince = _timing.CurTime;
        comp.MovementRetries++;
        if (comp.MovementRetries % 3 == 0 && comp.EscapeDirection == Vector2.Zero)
            comp.MovementRetryAfter = _timing.CurTime + TimeSpan.FromSeconds(1);
    }
}
