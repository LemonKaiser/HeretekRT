using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Damage;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    private const float HearingRange = 18f;
    private const float AlertVisionRange = 24f;
    private const float ShotDangerWidth = 1.5f;
    private static readonly TimeSpan HearingCooldown = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan DodgeDuration = TimeSpan.FromSeconds(0.45);
    private static readonly TimeSpan DodgeCooldown = TimeSpan.FromSeconds(0.9);

    [Dependency] private NPCRetaliationSystem _retaliation = default!;
    [Dependency] private NPCSystem _npcs = default!;
    [Dependency] private HTNSystem _htn = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextHeardShot = new();

    private void InitializeReactions()
    {
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, DamageChangedEvent>(OnThreatDamage);
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, AttackedEvent>(OnThreatMelee);
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnHeardGunShot);
        SubscribeLocalEvent<GunComponent, ComponentShutdown>(OnNoiseSourceShutdown);
    }

    private void OnNoiseSourceShutdown(EntityUid uid, GunComponent comp, ComponentShutdown args)
    {
        _nextHeardShot.Remove(uid);
    }

    private void OnThreatDamage(EntityUid uid, Wh40kChaosTacticsComponent comp, DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Origin is { } attacker && RememberThreat(uid, attacker, comp, true))
            StartDodge(uid, attacker, comp);
    }

    private void OnThreatMelee(EntityUid uid, Wh40kChaosTacticsComponent comp, AttackedEvent args)
    {
        if (RememberThreat(uid, args.User, comp, true))
            StartDodge(uid, args.User, comp);
    }

    private void OnHeardGunShot(Entity<GunComponent> gun, ref GunShotEvent args)
    {
        if (_nextHeardShot.TryGetValue(gun, out var next) && _timing.CurTime < next)
            return;
        _nextHeardShot[gun] = _timing.CurTime + HearingCooldown;

        var attacker = args.User;
        if (Deleted(attacker) || !TryComp<MobStateComponent>(attacker, out var state) || state.CurrentState != MobState.Alive)
            return;

        var origin = _transform.GetMapCoordinates(gun);
        var shot = args.ToCoordinates.IsValid(EntityManager)
            ? _transform.ToMapCoordinates(args.ToCoordinates).Position - origin.Position
            : Vector2.Zero;
        if (shot.LengthSquared() > 0.01f)
            shot = Vector2.Normalize(shot);
        var sound = gun.Comp.SoundGunshotModified;
        var audibleRange = sound == null ? 0f : MathF.Min(HearingRange, sound.Params.MaxDistance) *
            MathF.Pow(10f, MathF.Min(0f, sound.Params.Volume) / 20f);

        // One local spatial query per weapon per cooldown, rather than tracking every projectile each tick.
        foreach (var (uid, comp) in _lookup.GetEntitiesInRange<Wh40kChaosTacticsComponent>(origin, AlertVisionRange))
        {
            if (uid == attacker || Paused(uid) || _factions.IsEntityFriendly(uid, attacker))
                continue;
            var position = _transform.GetMapCoordinates(uid);
            var offset = position.Position - origin.Position;
            var along = Vector2.Dot(offset, shot);
            var threatened = shot != Vector2.Zero && along >= 0f &&
                (offset - shot * along).LengthSquared() <= ShotDangerWidth * ShotDangerWidth &&
                _interaction.InRangeUnobstructed(gun.Owner, uid, AlertVisionRange,
                    CollisionGroup.Impassable | CollisionGroup.InteractImpassable);
            if (!threatened && !origin.InRange(position, audibleRange))
                continue;
            if (!RememberThreat(uid, attacker, comp, threatened))
                continue;
            if (threatened)
                StartDodge(uid, attacker, comp);
        }
    }

    private bool RememberThreat(EntityUid uid, EntityUid attacker, Wh40kChaosTacticsComponent comp, bool attacked)
    {
        if (!_npcs.Enabled || Paused(uid) || Deleted(attacker) ||
            !TryComp<MobStateComponent>(uid, out var state) || state.CurrentState != MobState.Alive ||
            !TryComp<MobStateComponent>(attacker, out var attackerState) || attackerState.CurrentState != MobState.Alive ||
            !TryComp<HTNComponent>(uid, out var htn) || !htn.Enabled ||
            TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind ||
            !TryComp<NPCRetaliationComponent>(uid, out var retaliation))
            return false;

        var attackerTransform = Transform(attacker);
        if (attackerTransform.MapID != Transform(uid).MapID || attackerTransform.MapUid == null ||
            !attacked && comp.TargetVisible && comp.Target != null && comp.Target != attacker ||
            !_retaliation.TryRetaliate((uid, retaliation), attacker))
            return false;

        comp.ThreatTarget = attacker;
        comp.ThreatPosition = _transform.WithEntityId(attackerTransform.Coordinates,
            attackerTransform.GridUid ?? attackerTransform.MapUid.Value);
        comp.ThreatObservedAt = _timing.CurTime;
        comp.ThreatUntil = _timing.CurTime + comp.ThreatMemoryDuration;
        if (comp.IgnoredTarget == attacker)
            comp.IgnoredTarget = null;
        if (comp.Target == attacker && !comp.TargetVisible)
        {
            comp.LastSeenPosition = comp.ThreatPosition;
            comp.LastSeenAt = comp.ThreatObservedAt;
        }

        // The current combat operator reads this on its next update; an idle plan is replaced by HTN.
        var newTarget = !htn.Blackboard.TryGetValue<EntityUid>("Target", out var current, EntityManager) || current != attacker;
        htn.Blackboard.SetValue("Target", attacker);
        htn.Blackboard.SetValue("TargetCoordinates", comp.ThreatPosition.Value);
        if (!_npcs.IsAwake(uid, htn))
            _npcs.WakeNPC(uid, htn);
        if (newTarget)
        {
            comp.NextSightCheck = TimeSpan.Zero;
            _htn.Replan(htn);
        }
        return true;
    }

    private void StartDodge(EntityUid uid, EntityUid attacker, Wh40kChaosTacticsComponent comp)
    {
        if (_timing.CurTime < comp.NextDodge || !_blocker.CanMove(uid))
            return;
        var incoming = _transform.GetMapCoordinates(uid).Position - _transform.GetMapCoordinates(attacker).Position;
        if (incoming.LengthSquared() < 0.01f)
            return;
        incoming = Vector2.Normalize(incoming);
        if (comp.NextDodge == TimeSpan.Zero)
            comp.DodgeLeft = (uid.GetHashCode() & 1) == 0;
        else
            comp.DodgeLeft = !comp.DodgeLeft;
        comp.DodgeDirection = new Vector2(-incoming.Y, incoming.X) * (comp.DodgeLeft ? 1f : -1f);
        comp.DodgeUntil = _timing.CurTime + DodgeDuration;
        comp.NextDodge = _timing.CurTime + DodgeCooldown;
        comp.NextDirectionUpdate = TimeSpan.Zero;
    }

    public bool IsAlertedTo(Wh40kChaosTacticsComponent comp, EntityUid target)
    {
        return comp.ThreatTarget == target && _timing.CurTime < comp.ThreatUntil;
    }

    public bool TryGetThreatTarget(EntityUid uid, Wh40kChaosTacticsComponent comp, out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (comp.ThreatTarget is not { } attacker || !IsAlertedTo(comp, attacker) || Deleted(attacker) ||
            comp.IgnoredTarget == attacker && _timing.CurTime < comp.IgnoreUntil ||
            !TryComp<MobStateComponent>(attacker, out var state) || state.CurrentState != MobState.Alive ||
            Transform(attacker).MapID != Transform(uid).MapID)
            return false;
        target = attacker;
        return true;
    }
}
