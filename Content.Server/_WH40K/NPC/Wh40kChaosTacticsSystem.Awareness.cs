using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;

    public void ObserveTarget(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp, float vision)
    {
        if (comp.Target != target)
        {
            CancelThrowWarning(uid);
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
        }
        if (_timing.CurTime < comp.NextSightCheck)
            return;
        comp.NextSightCheck = _timing.CurTime + TimeSpan.FromSeconds(0.2);
        if (IsAlertedTo(comp, target))
            vision = MathF.Max(vision, AlertVisionRange);
        comp.TargetVisible = _interaction.InRangeUnobstructed(uid, target, vision, CollisionGroup.Opaque,
            e => _factions.IsEntityFriendly(uid, e));
        if (!comp.TargetVisible)
            return;
        var targetTransform = Transform(target);
        comp.LastSeenPosition = _transform.WithEntityId(targetTransform.Coordinates,
            targetTransform.GridUid ?? targetTransform.MapUid!.Value);
        comp.LastSeenAt = _timing.CurTime;
    }

    public void IgnoreTarget(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp)
    {
        CancelThrowWarning(uid);
        comp.IgnoredTarget = target;
        comp.IgnoreUntil = _timing.CurTime + TimeSpan.FromSeconds(5);
        comp.LastSeenPosition = null;
        comp.TargetVisible = false;
        comp.MeleeReadyAt = TimeSpan.Zero;
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
            steering.Status != SteeringStatus.NoPath && (clearShot || steering.FailedPathCount < NPCSteeringComponent.FailedPathLimit) ||
            _timing.CurTime < comp.NextPathRetry || !_blocker.CanMove(uid))
            return true;
        comp.NextPathRetry = _timing.CurTime + TimeSpan.FromSeconds(1.25);
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
        ranged = EnsureComp<NPCRangedCombatComponent>(uid);
        ranged.Target = target;
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
