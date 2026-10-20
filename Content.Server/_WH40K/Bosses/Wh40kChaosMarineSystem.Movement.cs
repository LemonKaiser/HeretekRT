using System.Numerics;
using Content.Server._WH40K.NPC;
using Content.Server.NPC.Components;
using Content.Shared._WH40K.Bosses;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    private const float MeleeApproachRange = 1.45f;

    private void UpdateMovement(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        var tactics = Comp<Wh40kChaosTacticsComponent>(uid);
        var wasLocked = tactics.DirectedMovementLocked;
        tactics.DirectedMovementLocked = MovementLocked(uid, state);
        if (wasLocked != tactics.DirectedMovementLocked)
            _speed.RefreshMovementSpeedModifiers(uid);
        tactics.Target = brain.Target;
        tactics.TargetVisible = brain.Target is { } enemy && CanTarget(uid, enemy) && Visible(uid, enemy);
        tactics.RangedRange = state.Stance == MarineStance.Melee ? 1.6f : 7;
        tactics.RetreatRange = state.Stance == MarineStance.Melee ? 0 : 3;
        if (tactics.DirectedMovementLocked)
        {
            tactics.DirectedDirection = Vector2.Zero;
            _steering.Unregister(uid);
            if (Wh40kChaosMarineCombat.IsRocket(state.Attack) || state.Attack is MarineAttack.R2 or MarineAttack.R3 or MarineAttack.R6 or MarineAttack.M3)
                _physics.SetLinearVelocity(uid, Vector2.Zero);
            return;
        }
        if (brain.ManualAttack || Now < brain.NextMovement) return;
        brain.NextMovement = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(.15f, state, .08f));
        tactics.DirectedDirection = Vector2.Zero;
        if (!state.Engaged || brain.Target is not { } target || !CanTarget(uid, target))
        {
            PatrolArena(uid, brain, state);
            return;
        }
        var origin = Local(uid, uid);
        var aim = Local(uid, target);
        var delta = aim - origin;
        var distance = delta.Length();
        if (state.Stance == MarineStance.Melee)
        {
            Pursue(uid, Transform(target).Coordinates, tactics.TargetVisible
                ? state.Attack == MarineAttack.M4 ? 1.8f : MeleeApproachRange : .9f);
            // Native seeking also brakes on arrival; forcing a full-speed direction bypasses that braking.
            return;
        }
        var preferred = 5.5f;
        if (!tactics.TargetVisible || distance > preferred + 1.5f || !CanWalkDirect(uid, Transform(target).Coordinates))
        {
            Pursue(uid, Transform(target).Coordinates, tactics.TargetVisible ? preferred : .9f);
            return;
        }
        if (distance < .1f) delta = Vector2.UnitX;
        var forward = Vector2.Normalize(delta);
        if (Now >= brain.NextOrbitChange)
        {
            brain.OrbitSide = -brain.OrbitSide;
            brain.NextOrbitChange = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(_random.NextFloat(2.5f, 4.5f), state));
        }
        var side = new Vector2(-forward.Y, forward.X) * brain.OrbitSide;
        var direction = side * .65f + forward * Math.Clamp((distance - preferred) * .65f, -.8f, .6f);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            direction = Vector2.Normalize(direction);
            var point = origin + direction * 1.5f;
            if (ValidPoint(uid, state, point, .8f) && CanWalkDirect(uid, Coordinates(state, point)))
            {
                Pursue(uid, Coordinates(state, point), .15f);
                tactics.DirectedDirection = _transform.GetWorldRotation(state.Anchor!.Value).RotateVec(direction);
                return;
            }
            brain.OrbitSide = -brain.OrbitSide;
            direction = -side * .65f + forward * Math.Clamp((distance - preferred) * .65f, -.8f, .6f);
        }
        Pursue(uid, Transform(target).Coordinates, preferred);
    }

    private void PatrolArena(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (Now < brain.NextPatrol) return;
        brain.NextPatrol = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(_random.NextFloat(3, 6), state));
        var arena = Comp<Wh40kBossArenaComponent>(uid);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var point = arena.Center + new Vector2(_random.NextFloat(-5, 5), _random.NextFloat(-5, 5));
            if (!SpawnPoint(uid, state, point) || !CanWalkDirect(uid, Coordinates(state, point))) continue;
            var steering = _steering.Register(uid, Coordinates(state, point));
            steering.DirectMove = true;
            steering.Flags = Wh40kChaosTacticsSystem.PatrolFlags;
            steering.Range = .35f;
            return;
        }
    }
}
