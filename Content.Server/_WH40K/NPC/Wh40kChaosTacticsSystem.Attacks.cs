using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Ensnaring.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [ViewVariables] public long ThrowAimCheckCount;

    public bool IsRestrained(EntityUid target)
    {
        return CompOrNull<EnsnareableComponent>(target)?.IsEnsnared == true ||
               CompOrNull<StaminaComponent>(target)?.Critical == true;
    }

    public bool TryGetThrowAim(EntityUid uid, EntityUid target, Wh40kChaosThrowerComponent thrower, out MapCoordinates aim)
    {
        ThrowAimCheckCount++;
        aim = default;
        var comp = Comp<Wh40kChaosTacticsComponent>(uid);
        if (!CanFightTarget(uid, target, comp))
            return false;
        var explosive = GetBlastRadius(thrower.Prototype) > 0f;
        if (!comp.TargetVisible)
        {
            // A stationary enemy may have ducked behind a corner. Throw at the observed point, never its hidden position.
            if (!explosive || comp.Target != target || comp.CampPosition == null ||
                comp.LastSeenAt - comp.StationarySince < TimeSpan.FromSeconds(2) ||
                _timing.CurTime - comp.LastSeenAt > TimeSpan.FromSeconds(2) ||
                comp.LastSeenPosition is not { } remembered || !remembered.IsValid(EntityManager))
                return false;
            aim = _transform.ToMapCoordinates(remembered);
        }
        else if (explosive && _timing.CurTime - comp.StationarySince >= TimeSpan.FromSeconds(2) &&
                 comp.CampPosition is { } camp && camp.IsValid(EntityManager))
            aim = _transform.ToMapCoordinates(camp);
        else
        {
            aim = _transform.GetMapCoordinates(target);
            var distance = Vector2.Distance(_transform.GetMapCoordinates(uid).Position, aim.Position);
            if (TryComp<PhysicsComponent>(target, out var body))
            {
                var lead = body.LinearVelocity * MathF.Min(distance / 10f, 0.6f);
                if (lead.LengthSquared() > 1.25f * 1.25f)
                    lead = Vector2.Normalize(lead) * 1.25f;
                aim = new MapCoordinates(aim.Position + lead, aim.MapId);
            }
        }
        var origin = _transform.GetMapCoordinates(uid);
        return origin.MapId == aim.MapId && origin.InRange(aim, thrower.Range) &&
               !origin.InRange(aim, thrower.MinRange - 0.01f) &&
               _interaction.InRangeUnobstructed(uid, aim, thrower.Range);
    }

    public bool CanIgnoreThrowSafety(EntityUid uid, EntityUid target, Wh40kChaosThrowerComponent comp)
    {
        return comp.SafetyBlockedTarget == target &&
               _timing.CurTime - comp.SafetyBlockedSince >= TimeSpan.FromSeconds(10) &&
               TryComp<Wh40kChaosTacticsComponent>(uid, out var tactics) &&
               tactics.Target == target && tactics.TargetVisible;
    }

    public void RecordBlockedThrow(EntityUid uid, EntityUid target, Wh40kChaosThrowerComponent comp)
    {
        if (comp.SafetyBlockedTarget == target ||
            !TryComp<Wh40kChaosTacticsComponent>(uid, out var tactics) ||
            tactics.Target != target || !tactics.TargetVisible)
            return;
        comp.SafetyBlockedTarget = target;
        comp.SafetyBlockedSince = _timing.CurTime;
    }

    public bool PrepareThrow(EntityUid uid, EntityUid target, Wh40kChaosThrowerComponent comp,
        bool explosive, ref MapCoordinates aim, bool ignoreSafety = false)
    {
        if (comp.WarningTarget is { } warned && warned != target)
            CancelThrowWarning(uid);
        if (comp.WarningTarget == null)
        {
            var reservation = EnsureComp<Wh40kChaosAttackReservationsComponent>(target);
            var owner = explosive ? reservation.Grenadier : reservation.Trapper;
            var until = explosive ? reservation.GrenadeUntil : reservation.BolaUntil;
            if (!ignoreSafety && until > _timing.CurTime && owner is { } other && !Deleted(other) &&
                (!TryComp<MobStateComponent>(other, out var state) || state.CurrentState == MobState.Alive) &&
                (other == uid || _factions.IsEntityFriendly(uid, other)))
            {
                if (explosive)
                    RecordBlockedThrow(uid, target, comp);
                return false;
            }
            if (explosive)
            {
                reservation.Grenadier = uid;
                reservation.GrenadeUntil = _timing.CurTime + comp.Windup + TimeSpan.FromSeconds(3);
            }
            else
            {
                reservation.Trapper = uid;
                reservation.BolaUntil = _timing.CurTime + comp.Windup + TimeSpan.FromSeconds(2);
            }
            comp.WarningTarget = target;
            var targetTransform = Transform(target);
            comp.WarningAim = _transform.WithEntityId(_transform.ToCoordinates(aim),
                targetTransform.GridUid ?? targetTransform.MapUid!.Value);
            comp.WarningExplosive = explosive;
            comp.ReadyAt = _timing.CurTime + comp.Windup;
            if (explosive)
                Warn(uid, "wh40k-chaos-grenade-warning",
                    new SoundPathSpecifier("/Audio/Items/smoke_grenade_prime.ogg"));
            return comp.Windup <= TimeSpan.Zero;
        }
        if (comp.WarningAim is not { } anchored || !anchored.IsValid(EntityManager))
        {
            CancelThrowWarning(uid);
            return false;
        }
        var fixedAim = _transform.ToMapCoordinates(anchored);
        if (fixedAim.MapId != aim.MapId ||
            Vector2.Distance(fixedAim.Position, aim.Position) > 1.25f ||
            _timing.CurTime - comp.ReadyAt > TimeSpan.FromSeconds(1))
        {
            CancelThrowWarning(uid);
            return false;
        }
        aim = fixedAim;
        return _timing.CurTime >= comp.ReadyAt;
    }

    public void CancelThrowWarning(EntityUid uid, bool resetSafety = false)
    {
        if (!TryComp<Wh40kChaosThrowerComponent>(uid, out var comp))
            return;
        if (resetSafety)
            comp.SafetyBlockedTarget = null;
        if (comp.WarningTarget is { } target && TryComp<Wh40kChaosAttackReservationsComponent>(target, out var reservation))
        {
            if (comp.WarningExplosive && reservation.Grenadier == uid)
                reservation.GrenadeUntil = _timing.CurTime;
            else if (!comp.WarningExplosive && reservation.Trapper == uid)
                reservation.BolaUntil = _timing.CurTime;
        }
        comp.WarningTarget = null;
        comp.WarningAim = null;
        comp.ReadyAt = TimeSpan.Zero;
    }

    public void CompleteThrow(EntityUid uid, Wh40kChaosThrowerComponent comp)
    {
        if (comp.WarningTarget is { } target && TryComp<Wh40kChaosAttackReservationsComponent>(target, out var reservation))
        {
            if (comp.WarningExplosive && reservation.Grenadier == uid)
                reservation.GrenadeUntil = _timing.CurTime + TimeSpan.FromSeconds(3);
            else if (!comp.WarningExplosive && reservation.Trapper == uid)
                reservation.BolaUntil = _timing.CurTime + TimeSpan.FromSeconds(2);
        }
        comp.WarningTarget = null;
        comp.WarningAim = null;
        comp.ReadyAt = TimeSpan.Zero;
        comp.SafetyBlockedTarget = null;
    }

    private void OnMeleeAttempt(EntityUid uid, Wh40kChaosTacticsComponent comp, ref AttemptMeleeEvent args)
    {
        if (args.Cancelled || args.User != uid || comp.MeleeWindup <= TimeSpan.Zero ||
            !TryComp<NPCMeleeCombatComponent>(uid, out var melee) || !TryComp<MeleeWeaponComponent>(uid, out var weapon))
            return;
        // Door and wall strikes must not inherit a windup aimed at a distant combat target.
        var target = melee.Target;

        if (Deleted(target) || !_interaction.InRangeUnobstructed(uid, target, weapon.Range))
            return;
        if (comp.MeleeReadyAt == TimeSpan.Zero || comp.MeleeWarningTarget != target ||
            _timing.CurTime - comp.MeleeReadyAt > TimeSpan.FromSeconds(0.5))
        {
            comp.MeleeWarningTarget = target;
            comp.MeleeReadyAt = _timing.CurTime + comp.MeleeWindup;
            weapon.NextAttack = comp.MeleeReadyAt;
            args.Cancelled = true;
            return;
        }
        if (_timing.CurTime < comp.MeleeReadyAt)
        {
            args.Cancelled = true;
            comp.MeleeReadyAt = TimeSpan.Zero;
            weapon.NextAttack = _timing.CurTime + TimeSpan.FromSeconds(0.2);
            return;
        }
        comp.MeleeReadyAt = TimeSpan.Zero;
    }

    private void OnMeleeDamageApplied(EntityUid uid, Wh40kChaosTacticsComponent comp, MeleeDamageAppliedEvent args)
    {
        if (args.User == uid && comp.MeleeStunDuration > TimeSpan.Zero &&
            IsCombatTarget(args.Target) && !_factions.IsEntityFriendly(uid, args.Target))
            _stun.TryStun(args.Target, comp.MeleeStunDuration, true);
    }

    private void CheckMeleeWarning(EntityUid uid, Wh40kChaosTacticsComponent comp)
    {
        if (comp.MeleeReadyAt == TimeSpan.Zero)
            return;
        if (comp.MeleeWarningTarget is not { } target || Deleted(target) || !_blocker.CanAttack(uid, target) ||
            !IsCombatTarget(target) ||
            !_interaction.InRangeUnobstructed(uid, target, 2.25f))
            comp.MeleeReadyAt = TimeSpan.Zero;
    }

    private void Warn(EntityUid uid, string message, SoundSpecifier sound)
    {
        _popups.PopupEntity(Loc.GetString(message), uid, PopupType.MediumCaution);
        _audio.PlayPvs(sound, uid);
    }
}
