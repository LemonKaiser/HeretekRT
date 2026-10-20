using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WH40K.Bosses;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    [Dependency] private TriggerSystem _triggers = default!;

    private void UpdateSmoke(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (Now < brain.NextShot) return;
        var spec = Wh40kChaosMarineCombat.Spec(MarineAttack.E);
        if (state.Shot >= spec.Shots)
        {
            Recover(uid, brain, state);
            return;
        }

        var origin = Local(uid, uid);
        var aim = state.Aim - state.Origin;
        var angle = MathF.Atan2(aim.Y, aim.X) + state.Shot * MathF.Tau / spec.Shots;
        var point = origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * spec.Reach;
        var arena = Comp<Wh40kBossArenaComponent>(uid);
        point = Vector2.Clamp(point, arena.Center - arena.HalfSize + new Vector2(.5f),
            arena.Center + arena.HalfSize - new Vector2(.5f));
        point = ClipLine(uid, origin, point, .25f);

        // Native grenades: real flight, fuse, hiss, spreading occluders and 30-second cleanup.
        // Smoke has no Opaque collision fixture, so the boss's wall-aware targeting sees through it.
        var grenade = Spawn("SmokeGrenade", Transform(uid).Coordinates);
        if (Vector2.DistanceSquared(origin, point) > .01f)
            _throwing.TryThrow(grenade, Coordinates(state, point), 6, user: uid,
                pushbackRatio: 0, compensateFriction: true, recoil: false);
        _triggers.StartTimer(grenade, uid);
        state.Shot++;
        brain.NextShot = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(.15f, state, .1f));
        state.NextShotAt = brain.NextShot;
        Dirty(uid, state);
        if (state.Shot >= spec.Shots) Recover(uid, brain, state);
    }
}
