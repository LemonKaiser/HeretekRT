using System.Linq;
using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    [Dependency] private ExplosionSystem _explosions = default!;

    private void InitializeShots()
    {
        SubscribeLocalEvent<Wh40kChaosMarineBrainComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<Wh40kMarineShotComponent, ProjectileHitEvent>(OnShotHit);
        SubscribeLocalEvent<Wh40kMarineShotComponent, PreventCollideEvent>(OnShotCollide);
        SubscribeLocalEvent<Wh40kMarineShotComponent, StartCollideEvent>(OnShotContact,
            before: [typeof(Content.Server.Projectiles.ProjectileSystem)]);
        SubscribeLocalEvent<Wh40kChaosMarineComponent, ProjectileReflectAttemptEvent>(OnRicochet,
            after: new[] { typeof(ReflectSystem) });
    }

    private void OnRicochet(EntityUid uid, Wh40kChaosMarineComponent state, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled && !TerminatingOrDeleted(args.ProjUid))
            Effect(uid, state, Local(uid, args.ProjUid), MarineEffect.Ricochet, .65f, .25f);
    }

    private void OnGunShot(EntityUid uid, Wh40kChaosMarineBrainComponent brain, ref GunShotEvent args)
    {
        var state = Comp<Wh40kChaosMarineComponent>(uid);
        foreach (var (projectile, _) in args.Ammo)
            if (projectile is { } bullet && HasComp<ProjectileComponent>(bullet))
                ConfigureShot(bullet, uid, state, state.Aim, seal: state.Attack == MarineAttack.R5);
    }

    private void ConfigureShot(EntityUid projectile, EntityUid uid, Wh40kChaosMarineComponent state,
        Vector2 destination, bool seal = false, bool rocket = false)
    {
        var shot = EnsureComp<Wh40kMarineShotComponent>(projectile);
        shot.Boss = uid;
        shot.Destination = Coordinates(state, destination);
        shot.Seal = seal;
        shot.Rocket = rocket;
        shot.Sequence = state.Sequence;
        shot.Independent = state.Attack == MarineAttack.R1;
        shot.CancellationSequence = Comp<Wh40kChaosMarineBrainComponent>(uid).CancellationSequence;
        shot.PreviousPosition = _transform.GetWorldPosition(projectile);
        shot.BouncesRemaining = state.Attack == MarineAttack.R4 ? 2 : 0;
        shot.Expires = Now + TimeSpan.FromSeconds(state.Attack == MarineAttack.R4 ? 5 : rocket ? 3 : 2);
        if (state.Attack == MarineAttack.R4 && TryComp<Robust.Shared.Spawners.TimedDespawnComponent>(projectile, out var despawn))
            despawn.Lifetime = 5;
        Comp<Wh40kChaosMarineBrainComponent>(uid).ProjectilesFired++;
    }

    private void OnShotCollide(EntityUid uid, Wh40kMarineShotComponent shot, ref PreventCollideEvent args)
    {
        if (SameBounceSurface(shot, args.OtherEntity))
            args.Cancelled = true;
        if ((shot.Seal || shot.GroundRocket) && HasComp<Content.Shared.Mobs.Components.MobStateComponent>(args.OtherEntity))
            args.Cancelled = true;
        if (TryComp<ProjectileComponent>(uid, out var projectile) && shot.Boss == projectile.Shooter &&
            shot.Boss is { } boss && (args.OtherEntity == boss || _factions.IsEntityFriendly(boss, args.OtherEntity)))
            args.Cancelled = true;
    }

    private void OnShotHit(EntityUid uid, Wh40kMarineShotComponent shot, ref ProjectileHitEvent args)
    {
        args.Handled = true;
        if (shot.Spent) return;
        var projectile = Comp<ProjectileComponent>(uid);
        if (SameBounceSurface(shot, args.Target))
        {
            projectile.ImpactCancelled = true;
            return;
        }
        if (shot.Boss is { } owner && projectile.Shooter == owner &&
            (!OwnedShotActive(owner, shot) || !_arena.Contains(owner, uid)))
        {
            shot.Spent = true;
            QueueDel(uid);
            return;
        }
        if (projectile.Shooter != shot.Boss) shot.Seal = shot.Rocket = false;
        if (shot.Boss is { } boss && projectile.Shooter == boss &&
            (args.Target == boss || _factions.IsEntityFriendly(boss, args.Target)))
        {
            projectile.ImpactCancelled = true;
            return;
        }
        if (TryBounceShot(uid, shot, projectile, args.Target)) return;
        if (shot.GroundRocket)
        {
            // New cover can intercept a barrage, but must never relocate its marked detonation.
            shot.Spent = projectile.ProjectileSpent = true;
            MarkRocketResolved(shot);
            QueueDel(uid);
            return;
        }
        if (!shot.Rocket)
            _damage.TryChangeDamage(args.Target, projectile.Damage, origin: args.Shooter, tool: uid, canSever: false);
        FinishShot(uid, shot, projectile, Transform(uid).Coordinates);
    }

    private void UpdateShots(float frameTime)
    {
        var query = EntityQueryEnumerator<Wh40kMarineShotComponent, ProjectileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var shot, out var projectile, out var xform))
        {
            if (Paused(uid) || shot.Spent) continue;
            var reflected = projectile.Shooter != shot.Boss;
            if (reflected)
            {
                shot.Seal = shot.Rocket = false;
                if (Now >= shot.Expires) QueueDel(uid);
                continue;
            }
            if (shot.Boss is not { } boss || !OwnedShotActive(boss, shot) || !_arena.Contains(boss, uid))
            {
                QueueDel(uid);
                continue;
            }
            if (Now >= shot.Expires)
            {
                if (shot.GroundRocket) { MarkRocketResolved(shot); QueueDel(uid); }
                else if (shot.Rocket) FinishShot(uid, shot, projectile, xform.Coordinates);
                else QueueDel(uid);
                continue;
            }
            if (!GuideRocket(uid, shot, projectile, frameTime)) { QueueDel(uid); continue; }
            if (!shot.Seal && !shot.Rocket) continue;
            var destination = _transform.ToMapCoordinates(shot.Destination);
            var position = _transform.GetWorldPosition(xform);
            if (xform.MapID != destination.MapId) { QueueDel(uid); continue; }
            if (Wh40kChaosMarineCombat.SegmentDistance(destination.Position, shot.PreviousPosition, position) <= (shot.GroundRocket ? .12f : .4f))
                FinishShot(uid, shot, projectile, shot.Destination);
            shot.PreviousPosition = position;
        }
    }

    private bool OwnedShotActive(EntityUid boss, Wh40kMarineShotComponent shot) =>
        !TerminatingOrDeleted(boss) && !_mobs.IsDead(boss) && _npcs.Enabled &&
        TryComp<Wh40kChaosMarineComponent>(boss, out var state) && state.Engaged &&
        (shot.Independent
            ? TryComp<Wh40kChaosMarineBrainComponent>(boss, out var brain) && brain.CancellationSequence == shot.CancellationSequence
            : state.Sequence == shot.Sequence);

    private bool ShotsInFlight(EntityUid boss, int sequence)
    {
        var query = EntityQueryEnumerator<Wh40kMarineShotComponent, ProjectileComponent>();
        while (query.MoveNext(out var uid, out var shot, out var projectile))
            if (shot.Boss == boss && shot.Sequence == sequence && !shot.Spent &&
                projectile.Shooter == boss && !TerminatingOrDeleted(uid)) return true;
        return false;
    }

    private void FinishShot(EntityUid uid, Wh40kMarineShotComponent shot, ProjectileComponent projectile, EntityCoordinates position)
    {
        if (shot.Spent) return;
        shot.Spent = true;
        projectile.ProjectileSpent = true;
        if (shot.Seal && shot.Boss == projectile.Shooter && shot.Boss is { } boss &&
            TryComp<Wh40kChaosMarineBrainComponent>(boss, out var brain) &&
            TryComp<Wh40kChaosMarineComponent>(boss, out var state) && state.Sequence == shot.Sequence &&
            state.Attack == MarineAttack.R5)
        {
            var map = _transform.ToMapCoordinates(position);
            var point = Vector2.Transform(map.Position, _transform.GetInvWorldMatrix(state.Anchor!.Value));
            // Never stack relocated seals into an unavoidable multi-explosion.
            if (ValidPoint(boss, state, point, 1.35f) && brain.Seals.All(s =>
                    Vector2.Distance(_transform.ToMapCoordinates(s.Position).Position, map.Position) >= 2.3f))
            {
                var effect = Effect(boss, state, point, MarineEffect.Seal, 1,
                    MathF.Max(.1f, (float) (state.EndsAt - Now).TotalSeconds));
                brain.Seals.Add(new MarineSeal(effect, Coordinates(state, point)));
            }
        }
        else
            Explode(position, projectile.Shooter, shot.Rocket, barrage: shot.GroundRocket);
        if (shot.GroundRocket) MarkRocketResolved(shot);
        QueueDel(uid);
    }

    private void Explode(EntityCoordinates position, EntityUid? source, bool rocket = false, bool seal = false, bool barrage = false)
    {
        const float slope = 8;
        var radius = barrage ? 1 : rocket ? 1.5f : seal ? 1 : 1.25f;
        var max = barrage ? 5 : rocket ? 3.8f : 2.5f;
        _explosions.QueueExplosion(position, barrage ? "Wh40kMarineBarrageExplosion" : "Wh40kMarineExplosion",
            barrage ? max : _explosions.RadiusToIntensity(radius, slope, max), slope, max, source,
            tileBreakScale: 0, maxTileBreak: 0, canCreateVacuum: false);
        if (source is { } boss && TryComp<Wh40kChaosMarineComponent>(boss, out var state) && state.Anchor is { } anchor)
        {
            var local = Vector2.Transform(_transform.ToMapCoordinates(position).Position, _transform.GetInvWorldMatrix(anchor));
            Effect(boss, state, local, barrage ? MarineEffect.BarrageImpact : MarineEffect.Explosion,
                barrage ? .5f : radius, barrage ? .5f : .6f);
        }
    }

    private void UpdateRanged(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        var attack = state.Attack;
        if (Wh40kChaosMarineCombat.IsRocket(attack)) { UpdateRockets(uid, brain, state); return; }
        if (brain.ShotRetryUntil != TimeSpan.Zero && Now >= brain.ShotRetryUntil && Now < brain.NextShot)
        {
            Recover(uid, brain, state, interrupted: true);
            return;
        }
        var spec = Wh40kChaosMarineCombat.Spec(attack);
        var shots = spec.Shots;
        if (state.Shot < shots)
        {
            if (attack == MarineAttack.R2)
            {
                var origin = Local(uid, uid);
                var displacement = origin - state.Origin;
                if (displacement.LengthSquared() > .00001f)
                {
                    state.Points = state.Points.Select(p => p + displacement).ToArray();
                    state.Origin = origin;
                    Dirty(uid, state);
                }
            }
            if (Now >= state.ExecuteAt && attack is MarineAttack.R2 or MarineAttack.R3 or MarineAttack.R6)
            {
                _steering.Unregister(uid);
                _physics.SetLinearVelocity(uid, Vector2.Zero);
            }
            if (attack != MarineAttack.R4 && Now >= brain.NextAim && Now < brain.NextShot - TimeSpan.FromSeconds(Wh40kChaosMarineCombat.AimLock(state)))
            {
                brain.NextAim = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(.1f, state, .05f));
                PrepareShot(uid, brain, state);
                Face(uid, state);
                Dirty(uid, state);
            }
            if (Now >= brain.NextShot)
            {
                if (attack == MarineAttack.R6 && !DashShotReady(uid, brain, state)) return;
                if (TryComp<GunComponent>(uid, out var gun))
                {
                    if (attack is MarineAttack.R2 or MarineAttack.R5) state.Aim = state.Points[state.Shot];
                    // The gun's own gate must agree with the director, including R3's accelerating cadence.
                    var cadence = Wh40kChaosMarineCombat.ShotInterval(attack, state.Shot + 1, state);
                    gun.FireRateModified = 1 / cadence;
                    DirtyField(uid, gun, nameof(GunComponent.FireRateModified));
                    if (gun.NextFire > Now)
                    {
                        brain.NextShot = state.NextShotAt = gun.NextFire;
                        Dirty(uid, state);
                        return;
                    }
                    if (attack is MarineAttack.R3 or MarineAttack.R4 or MarineAttack.R6)
                        PrepareShot(uid, brain, state);
                    Face(uid, state);
                    var fired = brain.ProjectilesFired;
                    _guns.AttemptShoot(uid, uid, gun, Coordinates(state, state.Aim));
                    if (fired == brain.ProjectilesFired)
                    {
                        // A temporary native weapon/action gate must not discard an entire announced pattern.
                        if (brain.ShotRetryUntil == TimeSpan.Zero) brain.ShotRetryUntil = Now + TimeSpan.FromSeconds(1);
                        if (Now >= brain.ShotRetryUntil) { Recover(uid, brain, state, interrupted: true); return; }
                        brain.NextShot = state.NextShotAt = gun.NextFire > Now ? gun.NextFire :
                            Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(.1f, state, .05f));
                        Dirty(uid, state);
                        return;
                    }
                    brain.ShotRetryUntil = TimeSpan.Zero;
                }
                state.Shot++;
                brain.DashPending = false;
                var interval = Wh40kChaosMarineCombat.ShotInterval(attack, state.Shot, state);
                brain.NextShot = Now + TimeSpan.FromSeconds(interval);
                state.NextShotAt = brain.NextShot;
                PrepareShot(uid, brain, state);
                Dirty(uid, state);
            }
        }
        if (attack == MarineAttack.R1 && state.Shot > 0)
        {
            Recover(uid, brain, state);
            return;
        }
        if (state.Shot < shots || ShotsInFlight(uid, state.Sequence)) return;
        if (attack == MarineAttack.R5)
        {
            if (!brain.SealsPrimed)
            {
                brain.SealsPrimed = true;
                state.EndsAt = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(1.1f, state, .6f));
                foreach (var seal in brain.Seals)
                    if (TryComp<Wh40kMarineEffectComponent>(seal.Effect, out var effect))
                    {
                        effect.EndsAt = state.EndsAt;
                        Dirty(seal.Effect, effect);
                    }
                Dirty(uid, state);
            }
            if (Now < state.EndsAt) return;
            foreach (var seal in brain.Seals)
            {
                Explode(seal.Position, uid, seal: true);
                QueueDel(seal.Effect);
            }
            brain.Seals.Clear();
        }
        Recover(uid, brain, state);
    }

    private void PrepareShot(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (state.Attack is MarineAttack.R2 or MarineAttack.R5)
        {
            if (state.Shot < state.Points.Length) state.Aim = state.Points[state.Shot];
            return;
        }
        if (brain.Target is not { } player || !CanTarget(uid, player)) return;
        state.Aim = state.Attack == MarineAttack.R4 && Now >= brain.NextShot
            ? BankShotAim(uid, player) : Local(uid, player);
    }

    private EntityUid Effect(EntityUid uid, Wh40kChaosMarineComponent state, Vector2 point, MarineEffect kind,
        float radius, float duration)
    {
        var entity = Spawn(kind == MarineEffect.Summon ? "Wh40kMarineSummonCircle" : "Wh40kMarineEffect", Coordinates(state, point));
        var effect = Comp<Wh40kMarineEffectComponent>(entity);
        effect.Kind = kind;
        effect.Boss = uid;
        effect.Radius = radius;
        effect.StartedAt = Now;
        effect.EndsAt = Now + TimeSpan.FromSeconds(duration);
        Dirty(entity, effect);
        return entity;
    }

    private void Sound(EntityUid uid, string path, float volume = 0) =>
        _audio.PlayPvs(new SoundPathSpecifier(path, AudioParams.Default.WithVolume(volume)), uid);
}
