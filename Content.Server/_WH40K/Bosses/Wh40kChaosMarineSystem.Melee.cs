using System.Linq;
using System.Numerics;
using Content.Server._WH40K.Combat.PhantomStep;
using Content.Shared._WH40K.Bosses;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    private void UpdateMeleeAction(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (Now < state.ExecuteAt) return;
        if (Now >= state.EndsAt)
        {
            Recover(uid, brain, state, interrupted: state.Attack is MarineAttack.M3 or MarineAttack.M5);
            return;
        }
        if (state.Attack == MarineAttack.M3) UpdateMeleeRush(uid, brain, state);
        else if (state.Attack == MarineAttack.M5) UpdateMeleeThrows(uid, brain, state);
    }

    private EntityUid? MeleeTarget(EntityUid uid, Wh40kChaosMarineBrainComponent brain, bool alternate, float reach = 10)
    {
        return LiveTargets(uid, brain).Where(target => Visible(uid, target) &&
                Vector2.DistanceSquared(Local(uid, uid), Local(uid, target)) <= reach * reach)
            .OrderBy(target => alternate && target == brain.ActionTarget ? 1 : 0)
            .ThenBy(target => Vector2.DistanceSquared(Local(uid, uid), Local(uid, target)))
            .Select(target => (EntityUid?) target).FirstOrDefault();
    }

    private bool TryMeleeDash(EntityUid uid, Wh40kChaosMarineComponent state, Vector2 destination)
    {
        var origin = Local(uid, uid);
        var delta = destination - origin;
        if (delta.Length() > 3) destination = origin + Vector2.Normalize(delta) * 3;
        if (Vector2.DistanceSquared(origin, destination) < .16f || !ValidPoint(uid, state, destination, .5f) ||
            !CanWalkDirect(uid, Coordinates(state, destination))) return false;
        _steering.Unregister(uid);
        _physics.SetLinearVelocity(uid, Vector2.Zero);
        return _phantomStep.TryDashTo(uid, Coordinates(state, destination),
            TimeSpan.FromSeconds(Wh40kChaosMarineCombat.DashDuration(state)));
    }

    private void UpdateMeleeRush(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (Comp<WH40KPhantomStepComponent>(uid).Dashing || Now < brain.MeleeNextAt) return;
        switch (brain.MeleeStage)
        {
            case 0:
                if (state.Shot >= 3) { Recover(uid, brain, state); return; }
                brain.ActionTarget = MeleeTarget(uid, brain, state.Shot > 0, 5) ?? brain.ActionTarget;
                brain.MeleeReturn = Local(uid, uid);
                if (brain.ActionTarget is { } target && CanTarget(uid, target))
                {
                    var point = Local(uid, target);
                    var radial = brain.MeleeReturn - point;
                    var forward = radial.LengthSquared() > .001f ? Vector2.Normalize(radial) : Vector2.UnitX;
                    var side = new Vector2(-forward.Y, forward.X) * (state.Shot % 2 == 0 ? 1 : -1);
                    if (!TryMeleeDash(uid, state, point + forward * 1.65f + side * .65f))
                        TryMeleeDash(uid, state, point + forward * 1.65f);
                }
                brain.MeleeStage = 1;
                break;
            case 1:
                state.Origin = Local(uid, uid);
                if (brain.ActionTarget is { } victim && CanTarget(uid, victim)) state.Aim = Local(uid, victim);
                brain.MeleeStage = 2;
                brain.MeleeStageAt = Now;
                brain.MeleeNextAt = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(.46f, state));
                Face(uid, state);
                Dirty(uid, state);
                break;
            case 2:
                TryMeleeDash(uid, state, brain.MeleeReturn);
                brain.MeleeStage = 3;
                break;
            case 3:
                state.Shot++;
                brain.MeleeStage = 0;
                brain.MeleeNextAt = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(.18f, state));
                Dirty(uid, state);
                break;
        }
    }

    private void UpdateMeleeThrows(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (brain.MeleeStage == 0)
        {
            BeginMaceThrow(uid, brain, state);
            return;
        }
        if (brain.MeleeStage == 1 && Now >= brain.MeleeNextAt)
        {
            brain.MeleeStage = 2;
            brain.MeleeStageAt = Now;
            brain.MeleeNextAt = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(1, state));
            brain.ThrowReturnReach = state.Mace is { } head ? Vector2.Distance(Local(uid, uid), Local(uid, head)) : 1;
        }
        if (brain.MeleeStage != 2 || state.Mace is not { } mace) return;
        var retracted = Vector2.Distance(Local(uid, uid), Local(uid, mace)) <= 1.1f;
        if (Now < brain.MeleeStageAt + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(.26f, state))) return;
        if (!retracted)
        {
            if (Now >= brain.MeleeNextAt) Recover(uid, brain, state, interrupted: true);
            return;
        }
        state.Shot++;
        if (state.Shot >= Wh40kChaosMarineCombat.Spec(MarineAttack.M5).Shots)
            Recover(uid, brain, state);
        else
            BeginMaceThrow(uid, brain, state);
        Dirty(uid, state);
    }

    private void BeginMaceThrow(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        brain.ActionTarget = MeleeTarget(uid, brain, false) ?? brain.ActionTarget;
        state.Origin = Local(uid, uid);
        if (brain.ActionTarget is { } target && CanTarget(uid, target)) state.Aim = Local(uid, target);
        var delta = state.Aim - state.Origin;
        if (delta.Length() > 10) state.Aim = state.Origin + Vector2.Normalize(delta) * 10;
        brain.MeleeStage = 1;
        brain.MeleeStageAt = Now;
        // Let the real head reach the end of its .48 s throw before reeling it in; a spring has finite lag.
        brain.MeleeNextAt = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(.6f, state));
        Face(uid, state);
        Dirty(uid, state);
    }

    private MarineMacePose MacePose(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        var progress = Math.Clamp((float) ((Now - state.ExecuteAt).TotalSeconds /
            Math.Max(.001, (state.EndsAt - state.ExecuteAt).TotalSeconds)), 0, 1);
        if (state.Attack == MarineAttack.M3)
        {
            if (brain.MeleeStage != 2) return new(new Vector2(.65f, 0), -1);
            var prepare = Wh40kChaosMarineCombat.Delay(.12f, state);
            var elapsed = (float) (Now - brain.MeleeStageAt).TotalSeconds - prepare;
            var pose = Wh40kChaosMarineMacePhysics.Pose(MarineAttack.M3,
                elapsed / Wh40kChaosMarineCombat.Delay(.34f, state));
            return pose with { Strike = elapsed < 0 ? -1 : state.Shot };
        }
        if (state.Attack == MarineAttack.M5)
        {
            var returning = brain.MeleeStage != 1;
            var reach = returning ? brain.ThrowReturnReach : Vector2.Distance(state.Origin, state.Aim);
            var duration = Wh40kChaosMarineCombat.Delay(returning ? .42f : .48f, state);
            var pose = Wh40kChaosMarineMacePhysics.Throw(reach,
                (float) (Now - brain.MeleeStageAt).TotalSeconds / duration, returning);
            return pose with { Strike = returning ? -1 : state.Shot };
        }
        var sample = Wh40kChaosMarineMacePhysics.Pose(state.Attack, progress,
            Wh40kChaosMarineCombat.AttackSpeed(state.PhaseTwo, state.HeatStep));
        if (state.Attack == MarineAttack.M4 && brain.Target is { } enemy && CanTarget(uid, enemy))
        {
            var distance = Math.Clamp(Vector2.Distance(Local(uid, uid), Local(uid, enemy)), 1.25f, 2.4f);
            sample = sample with { Offset = sample.Offset * (distance / 2.4f) };
        }
        return sample;
    }
}
