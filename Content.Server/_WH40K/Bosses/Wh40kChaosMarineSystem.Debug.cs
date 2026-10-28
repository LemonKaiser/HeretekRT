using Content.Shared._WH40K.Bosses;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    /// <summary>Starts an announced attack through the same path as the combat director.</summary>
    public bool TryStartAttack(EntityUid uid, MarineAttack attack, EntityUid target, params EntityUid[] additionalTargets)
    {
        if (!TryComp<Wh40kChaosMarineBrainComponent>(uid, out var brain) ||
            !TryComp<Wh40kChaosMarineComponent>(uid, out var state) || !CanTarget(uid, target) ||
            !Enum.IsDefined(attack) || attack is MarineAttack.None or MarineAttack.Transition or MarineAttack.Summon ||
            state.TransitionProtected || _mobs.IsDead(uid))
            return false;
        if (TryComp<Wh40kChaosMarineTestComponent>(uid, out var test) && test.Attack is { } locked && attack != locked)
            return false;
        CancelAction(uid, brain, state);
        brain.ManualAttack = true;
        brain.Target = target;
        FindTargets(uid, brain);
        if (!brain.Targets.Contains(target)) brain.Targets.Add(target);
        foreach (var additional in additionalTargets)
            if (CanTarget(uid, additional) && !brain.Targets.Contains(additional)) brain.Targets.Add(additional);
        brain.Target = target;
        state.Engaged = true;
        if (attack == MarineAttack.Punch)
            brain.CloseSince[target] = Now - TimeSpan.FromSeconds(Wh40kChaosMarineCombat.PunchProximitySeconds);
        state.RecoverUntil = TimeSpan.Zero;
        return TryBegin(uid, brain, state, attack);
    }

    private void ConfigureTest(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (!TryComp<Wh40kChaosMarineTestComponent>(uid, out var test)) return;
        brain.WavesDone = test.CompletedWaves;
        if (test.Attack is { } attack)
            state.Stance = Wh40kChaosMarineCombat.IsMelee(attack) ? MarineStance.Melee : MarineStance.Ranged;
        if (state.PhaseTwo)
        {
            if (_bosses.TryGetHealth(uid, out var health, out var maximum))
                state.HeatStep = Wh40kChaosMarineCombat.Overheat(health, maximum);
            KeepFire(uid, state);
        }
        Dirty(uid, state);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void ThinkTest(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state,
        Wh40kChaosMarineTestComponent test, MarineAttack attack)
    {
        if (attack == MarineAttack.Punch) UpdateClose(uid, brain);
        if (state.Attack != MarineAttack.None || Now < state.RecoverUntil || Now < brain.NextAttack ||
            Now < test.NextCycle || brain.Target is not { } target || attack == MarineAttack.None)
            return;
        if (attack == MarineAttack.Summon)
        {
            // Keep a complete circle/materialization cycle, then remove this test's previous wave.
            ClearOwned(uid, brain);
            brain.SpawnedMinions.Clear();
            brain.WavesDone = 0;
            brain.PendingWave = 1;
            brain.PendingSummonCount = test.SummonCount;
            test.NextCycle = Now + TimeSpan.FromSeconds(12.5);
            BeginAction(uid, brain, state, attack);
            RequestVoice(uid, brain, 70);
            return;
        }
        if (attack == MarineAttack.Transition)
        {
            state.PhaseTwo = false;
            state.HeatStep = 0;
            _fire.Extinguish(uid);
            BeginAction(uid, brain, state, attack);
            state.TransitionProtected = true;
            Dirty(uid, state);
            RequestVoice(uid, brain, 90);
            return;
        }
        if (attack == MarineAttack.Punch && (!brain.CloseSince.TryGetValue(target, out var since) ||
                Now - since < TimeSpan.FromSeconds(Wh40kChaosMarineCombat.PunchProximitySeconds)))
            return;
        if (Wh40kChaosMarineCombat.IsBasic(attack) &&
            Now < (attack == MarineAttack.R1 ? brain.NextBasicRanged : brain.NextBasicMelee)) return;
        if (TryBegin(uid, brain, state, attack)) return;
        Pursue(uid, Transform(target).Coordinates, state.Stance == MarineStance.Melee ? MeleeApproachRange : 5.5f);
    }

    private bool MovementLocked(EntityUid uid, Wh40kChaosMarineComponent state) =>
        Wh40kChaosMarineCombat.MovementLocked(state, Now) ||
        TryComp<Wh40kChaosMarineTestComponent>(uid, out var test) && !test.Movement;
}
