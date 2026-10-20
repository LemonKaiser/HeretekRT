using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Interaction;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    [Dependency] private RotateToFaceSystem _rotateToFace = default!;

    private Vector2[] BarragePoints(EntityUid uid, Wh40kChaosMarineComponent state, MarineAttack attack)
    {
        var arena = Comp<Wh40kBossArenaComponent>(uid);
        var candidates = new List<Vector2>();
        var vertical = _random.Prob(.5f);
        var parity = _random.Next(2);
        for (var x = (int) MathF.Floor(arena.Center.X - arena.HalfSize.X); x < arena.Center.X + arena.HalfSize.X; x++)
        for (var y = (int) MathF.Floor(arena.Center.Y - arena.HalfSize.Y); y < arena.Center.Y + arena.HalfSize.Y; y++)
        {
            var point = new Vector2(x + .5f, y + .5f);
            var index = attack == MarineAttack.S2 ? x + y : vertical ? x : y;
            if (attack != MarineAttack.S4 && (index & 1) != parity) continue;
            if (BarragePointAvailable(uid, state, point)) candidates.Add(point);
        }
        var center = Comp<Wh40kChaosMarineBrainComponent>(uid).Target is { } target ? Local(uid, target) : Local(uid, uid);
        if (attack == MarineAttack.S3)
        {
            // Four ten-cell stripes, with one completely unmarked row between adjacent stripes.
            return candidates.GroupBy(p => vertical ? p.X : p.Y)
                .OrderBy(g => MathF.Abs(g.Key - (vertical ? center.X : center.Y)))
                .SelectMany(g => g.OrderBy(p => MathF.Abs((vertical ? p.Y : p.X) - (vertical ? center.Y : center.X)))
                    .Take(10).OrderBy(p => vertical ? p.Y : p.X))
                .Take(Wh40kChaosMarineCombat.BarrageLimit).ToArray();
        }
        if (attack == MarineAttack.S2)
            candidates = candidates.OrderBy(p => Vector2.DistanceSquared(p, center)).Take(Wh40kChaosMarineCombat.BarrageLimit).ToList();
        _random.Shuffle(candidates);
        return candidates.Take(Wh40kChaosMarineCombat.BarrageLimit).ToArray();
    }

    private bool BarragePointAvailable(EntityUid uid, Wh40kChaosMarineComponent state, Vector2 point)
    {
        if (!SpawnPoint(uid, state, point)) return false;
        var origin = Local(uid, uid);
        if (Vector2.DistanceSquared(origin, point) < 2.25f) return false;
        var side = Vector2.Normalize(new Vector2(-(point - origin).Y, (point - origin).X)) * .16f;
        return ClearLine(uid, origin, point) && ClearLine(uid, origin + side, point + side) &&
            ClearLine(uid, origin - side, point - side);
    }

    private void UpdateRockets(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (Now < state.ExecuteAt) return;
        if (state.Shot < state.Points.Length && Now >= brain.NextShot)
        {
            var barrage = Wh40kChaosMarineCombat.IsBarrage(state.Attack);
            // Catch up to the scheduled 0.1 s rhythm without allowing an entire stalled salvo in one tick.
            for (var i = 0; i < (barrage ? 2 : 1) && state.Shot < state.Points.Length; i++, state.Shot++)
                LaunchRocket(uid, brain, state, barrage);
            brain.NextShot += TimeSpan.FromSeconds(Wh40kChaosMarineCombat.ShotInterval(state.Attack, state.Shot, state));
            if (brain.NextShot < Now) brain.NextShot = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.ShotInterval(state.Attack, state.Shot, state));
            state.NextShotAt = brain.NextShot;
            Dirty(uid, state);
        }
        if (state.Shot >= state.Points.Length && !ShotsInFlight(uid, state.Sequence)) Recover(uid, brain, state);
    }

    private void LaunchRocket(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state, bool barrage)
    {
        var index = state.Shot;
        EntityUid? target = null;
        var point = state.Points[index];
        if (barrage)
        {
            // Cover built during the warning cancels this point rather than moving its explosion onto a player.
            if (!BarragePointAvailable(uid, state, point)) { ResolveRocket(uid, state, index); return; }
        }
        else
        {
            var targets = state.RocketTargets.Where(t => CanTarget(uid, t)).ToArray();
            if (targets.Length == 0) return;
            target = targets[index % targets.Length];
            point = Local(uid, target.Value);
        }
        state.Aim = point;
        Face(uid, state);
        var rocket = Spawn("Wh40kMarineRocket", Transform(uid).Coordinates);
        ConfigureShot(rocket, uid, state, point, rocket: true);
        var shot = Comp<Wh40kMarineShotComponent>(rocket);
        shot.GroundRocket = barrage;
        shot.RocketIndex = index;
        shot.HomingTarget = target;
        shot.Expires = Now + TimeSpan.FromSeconds(6);
        var direction = _arena.World(state.Anchor!.Value, point) - _transform.GetWorldPosition(uid);
        _guns.ShootProjectile(rocket, direction, Vector2.Zero, uid, uid, barrage ? 22 : Wh40kChaosMarineCombat.HomingRocketSpeed);
        Sound(uid, "/Audio/Weapons/Guns/Gunshots/rocket_launcher.ogg", barrage ? -12 : -3);
    }

    private bool GuideRocket(EntityUid uid, Wh40kMarineShotComponent shot, ProjectileComponent projectile, float frameTime)
    {
        if (shot.HomingTarget is not { } target) return true;
        if (shot.Boss is not { } boss || !CanTarget(boss, target)) return false;
        shot.Destination = Transform(target).Coordinates;
        var delta = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(uid);
        if (delta.LengthSquared() < .001f) return true;
        _rotateToFace.TryRotateTo(uid, delta.ToWorldAngle() + projectile.Angle, frameTime,
            new Angle(MathF.Tau), Wh40kChaosMarineCombat.HomingRocketTurnRate);
        var direction = (_transform.GetWorldRotation(uid) - projectile.Angle).ToWorldVec();
        _physics.SetLinearVelocity(uid, direction * Wh40kChaosMarineCombat.HomingRocketSpeed -
            _physics.GetMapLinearVelocity(Transform(uid).ParentUid));
        return true;
    }

    private void ResolveRocket(EntityUid boss, Wh40kChaosMarineComponent state, int index)
    {
        if (state.ResolvedRockets.Contains(index)) return;
        state.ResolvedRockets.Add(index);
        Dirty(boss, state);
    }

    private void MarkRocketResolved(Wh40kMarineShotComponent shot)
    {
        if (shot.Boss is { } boss && TryComp<Wh40kChaosMarineComponent>(boss, out var state) && state.Sequence == shot.Sequence)
            ResolveRocket(boss, state, shot.RocketIndex);
    }
}
