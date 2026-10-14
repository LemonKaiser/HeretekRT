using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Ensnaring.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public bool IsRestrained(EntityUid target)
    {
        return CompOrNull<EnsnareableComponent>(target)?.IsEnsnared == true ||
               CompOrNull<StaminaComponent>(target)?.Critical == true;
    }

    public bool PrepareThrow(EntityUid uid, EntityUid target, Wh40kChaosThrowerComponent comp,
        bool explosive, ref MapCoordinates aim)
    {
        if (comp.WarningTarget is { } warned && warned != target)
            CancelThrowWarning(uid);
        if (comp.WarningTarget == null)
        {
            var reservation = EnsureComp<Wh40kChaosAttackReservationsComponent>(target);
            var owner = explosive ? reservation.Grenadier : reservation.Trapper;
            var until = explosive ? reservation.GrenadeUntil : reservation.BolaUntil;
            if (until > _timing.CurTime && owner is { } other && !Deleted(other) &&
                (other == uid || _factions.IsEntityFriendly(uid, other)))
                return false;
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
            comp.WarningAim = aim;
            comp.WarningExplosive = explosive;
            comp.ReadyAt = _timing.CurTime + comp.Windup;
            if (explosive)
                Warn(uid, "wh40k-chaos-grenade-warning",
                    new SoundPathSpecifier("/Audio/Items/smoke_grenade_prime.ogg"));
            return comp.Windup <= TimeSpan.Zero;
        }
        if (comp.WarningAim is not { } fixedAim || fixedAim.MapId != aim.MapId ||
            Vector2.Distance(fixedAim.Position, aim.Position) > 1.25f ||
            _timing.CurTime - comp.ReadyAt > TimeSpan.FromSeconds(1))
        {
            CancelThrowWarning(uid);
            return false;
        }
        aim = fixedAim;
        return _timing.CurTime >= comp.ReadyAt;
    }

    public void CancelThrowWarning(EntityUid uid)
    {
        if (!TryComp<Wh40kChaosThrowerComponent>(uid, out var comp))
            return;
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

    public void CompleteThrow(Wh40kChaosThrowerComponent comp)
    {
        if (comp.WarningTarget is { } target && TryComp<Wh40kChaosAttackReservationsComponent>(target, out var reservation))
        {
            if (comp.WarningExplosive)
                reservation.GrenadeUntil = _timing.CurTime + TimeSpan.FromSeconds(3);
            else
                reservation.BolaUntil = _timing.CurTime + TimeSpan.FromSeconds(2);
        }
        comp.WarningTarget = null;
        comp.WarningAim = null;
        comp.ReadyAt = TimeSpan.Zero;
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
        var aim = _transform.GetMapCoordinates(target);
        if (comp.MeleeReadyAt == TimeSpan.Zero || comp.MeleeWarningTarget != target ||
            _timing.CurTime - comp.MeleeReadyAt > TimeSpan.FromSeconds(0.5))
        {
            comp.MeleeWarningTarget = target;
            comp.MeleeWarningPosition = aim;
            comp.MeleeReadyAt = _timing.CurTime + comp.MeleeWindup;
            weapon.NextAttack = comp.MeleeReadyAt;
            args.Cancelled = true;
            return;
        }
        if (_timing.CurTime < comp.MeleeReadyAt || comp.MeleeWarningPosition is not { } announced ||
            announced.MapId != aim.MapId || Vector2.Distance(announced.Position, aim.Position) > 0.8f ||
            !_interaction.InRangeUnobstructed(uid, target, weapon.Range))
        {
            args.Cancelled = true;
            comp.MeleeReadyAt = TimeSpan.Zero;
            weapon.NextAttack = _timing.CurTime + TimeSpan.FromSeconds(0.2);
            return;
        }
        comp.MeleeReadyAt = TimeSpan.Zero;
    }

    private void CheckMeleeWarning(EntityUid uid, Wh40kChaosTacticsComponent comp)
    {
        if (comp.MeleeReadyAt == TimeSpan.Zero)
            return;
        if (comp.MeleeWarningTarget is not { } target || Deleted(target) || !_blocker.CanAttack(uid, target) ||
            CompOrNull<MobStateComponent>(target)?.CurrentState != MobState.Alive ||
            !_interaction.InRangeUnobstructed(uid, target, 2.25f))
            comp.MeleeReadyAt = TimeSpan.Zero;
    }

    private void Warn(EntityUid uid, string message, SoundSpecifier sound)
    {
        _popups.PopupEntity(Loc.GetString(message), uid, PopupType.MediumCaution);
        _audio.PlayPvs(sound, uid);
    }
}
