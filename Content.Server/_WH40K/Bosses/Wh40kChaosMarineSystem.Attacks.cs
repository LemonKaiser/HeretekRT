using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Physics;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedCameraRecoilSystem _cameraRecoil = default!;

    private void Think(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        brain.ManualAttack = false;
        if (brain.Targets.Count == 0)
        {
            if (state.Engaged)
            {
                state.Engaged = false;
                brain.EmptySince = Now;
                brain.CloseSince.Clear();
                if (state.Attack != MarineAttack.Transition)
                    CancelAction(uid, brain, state);
                CancelSummons(brain);
                Dirty(uid, state);
            }
            if (brain.Activated && Now - brain.EmptySince > TimeSpan.FromSeconds(10))
                ClearOwned(uid, brain);
            return;
        }
        if (!state.Engaged)
        {
            state.Engaged = true;
            Dirty(uid, state);
        }
        if (!brain.Activated)
        {
            brain.Activated = true;
            brain.NextRocket = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(60, state));
            brain.NextSmoke = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.SmokeCooldownSeconds);
            brain.NextJump = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(Wh40kChaosMarineCombat.JumpInitialDelaySeconds, state));
            brain.NextStance = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(6, state));
            brain.NextPattern = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(Wh40kChaosMarineCombat.PatternGapSeconds, state));
        }
        if (TryComp<Wh40kChaosMarineTestComponent>(uid, out var test) && test.Attack is { } testAttack)
        {
            ThinkTest(uid, brain, state, test, testAttack);
            return;
        }
        if (state.Attack is not (MarineAttack.Transition or MarineAttack.Summon or MarineAttack.Jump))
            UpdateClose(uid, brain);
        if (TryStartSummon(uid, brain, state)) return;
        if (state.Attack != MarineAttack.None || Now < state.RecoverUntil || Now < brain.NextAttack || brain.Target is not { } target)
            return;
        if (Now >= brain.NextPunch && brain.CloseSince.Any(p => Now - p.Value >= TimeSpan.FromSeconds(Wh40kChaosMarineCombat.PunchProximitySeconds)))
        {
            brain.Target = brain.CloseSince.First(p => Now - p.Value >= TimeSpan.FromSeconds(Wh40kChaosMarineCombat.PunchProximitySeconds)).Key;
            BeginAction(uid, brain, state, MarineAttack.Punch);
            return;
        }
        var distance = Vector2.Distance(Local(uid, uid), Local(uid, target));
        if (Now >= brain.NextSmoke && TryBegin(uid, brain, state, MarineAttack.E)) return;
        if (Now >= brain.NextRocket && Visible(uid, target) && distance > 3)
        {
            var rocket = PatternOrder(brain, [MarineAttack.S1, MarineAttack.S2, MarineAttack.S3, MarineAttack.S4]).First();
            if (TryBegin(uid, brain, state, rocket))
            {
                brain.NextRocket = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(_random.NextFloat(90, 120), state));
                return;
            }
        }
        if (Now >= brain.NextJump && distance > 3 &&
            distance <= Wh40kChaosMarineCombat.Spec(MarineAttack.Jump).Reach && Visible(uid, target))
        {
            BeginAction(uid, brain, state, MarineAttack.Jump);
            brain.NextJump = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(Wh40kChaosMarineCombat.JumpCooldownSeconds, state));
            return;
        }
        if (Now >= brain.NextStance &&
            (state.Stance == MarineStance.Ranged && distance < 3 || state.Stance == MarineStance.Melee && distance > 7 ||
             Now >= brain.NextStance + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(4, state))))
        {
            BeginAction(uid, brain, state, MarineAttack.Switch);
            brain.NextStance = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(_random.NextFloat(9, 13), state));
            return;
        }
        if (state.Stance == MarineStance.Melee && (distance > 5.8f || !Visible(uid, target)) ||
            state.Stance == MarineStance.Ranged && distance > 12 && Visible(uid, target))
        {
            Pursue(uid, Transform(target).Coordinates, !Visible(uid, target) ? .9f : state.Stance == MarineStance.Melee ? MeleeApproachRange : 6);
            TryBasicAttack(uid, brain, state);
            return;
        }
        if (Now < brain.NextPattern)
        {
            TryBasicAttack(uid, brain, state);
            return;
        }
        MarineAttack[] candidates = state.Stance == MarineStance.Ranged
            ? Visible(uid, target) ? [MarineAttack.R2, MarineAttack.R3, MarineAttack.R4, MarineAttack.R5, MarineAttack.R6] : [MarineAttack.R3, MarineAttack.R4]
            : distance > 5 ? [MarineAttack.M5]
            : distance > 3 ? [MarineAttack.M2, MarineAttack.M3, MarineAttack.M5, MarineAttack.M6]
            : [MarineAttack.M2, MarineAttack.M3, MarineAttack.M4, MarineAttack.M5, MarineAttack.M6];
        foreach (var attack in PatternOrder(brain, candidates))
        {
            if (attack == brain.Previous && brain.Repeats >= 2)
                continue;
            if (TryBegin(uid, brain, state, attack))
                return;
        }
        Pursue(uid, Transform(target).Coordinates, !Visible(uid, target) ? .9f : state.Stance == MarineStance.Melee ? MeleeApproachRange : 5.5f);
        TryBasicAttack(uid, brain, state);
    }

    private IEnumerable<MarineAttack> PatternOrder(Wh40kChaosMarineBrainComponent brain, MarineAttack[] candidates)
    {
        _random.Shuffle(candidates);
        return candidates.OrderBy(attack => brain.PatternUses.GetValueOrDefault(attack))
            .ThenBy(attack => attack == brain.Previous ? 1 : 0);
    }

    private bool TryBasicAttack(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        var melee = state.Stance == MarineStance.Melee;
        if (Now < (melee ? brain.NextBasicMelee : brain.NextBasicRanged)) return false;
        return TryBegin(uid, brain, state, melee ? MarineAttack.M1 : MarineAttack.R1);
    }

    private void UpdateClose(EntityUid uid, Wh40kChaosMarineBrainComponent brain)
    {
        foreach (var player in brain.CloseSince.Keys.ToArray())
            if (!CanTarget(uid, player) || !brain.Targets.Contains(player) || Vector2.Distance(Local(uid, uid), Local(uid, player)) > 2 || !Visible(uid, player))
                brain.CloseSince.Remove(player);
        foreach (var player in LiveTargets(uid, brain))
            if (Vector2.Distance(Local(uid, uid), Local(uid, player)) <= 2 && Visible(uid, player))
                brain.CloseSince.TryAdd(player, Now);
    }

    private bool TryBegin(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state, MarineAttack attack)
    {
        if (brain.Target is not { } target || !CanTarget(uid, target))
            return false;
        var point = Local(uid, target);
        if (attack == MarineAttack.M1 &&
            (Vector2.Distance(Local(uid, uid), point) > Wh40kChaosMarineCombat.Spec(attack).Reach || !Visible(uid, target)))
            return false;
        if (Wh40kChaosMarineCombat.IsMelee(attack) && attack != MarineAttack.M1)
        {
            var spec = Wh40kChaosMarineCombat.Spec(attack);
            var reach = spec.Reach;
            if (Vector2.Distance(Local(uid, uid), point) > reach) return false;
        }
        var points = attack switch
        {
            MarineAttack.R5 => Wh40kChaosMarineCombat.SealPoints(point),
            MarineAttack.S1 => new Vector2[4],
            MarineAttack.S2 or MarineAttack.S3 or MarineAttack.S4 => BarragePoints(uid, state, attack),
            _ => [],
        };
        if (Wh40kChaosMarineCombat.IsRocket(attack) && points.Length == 0) return false;
        if (!Wh40kChaosMarineCombat.IsRocket(attack) &&
            points.Any(p => !ValidPoint(uid, state, p, 1.5f) || !ClearLine(uid, Local(uid, uid), p)))
            return false;
        BeginAction(uid, brain, state, attack, points);
        return true;
    }

    private void BeginAction(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state,
        MarineAttack attack, Vector2[]? points = null)
    {
        DestroyMace(uid, brain, state);
        if (Wh40kChaosMarineCombat.IsBarrage(attack) || Wh40kChaosMarineCombat.IsStationarySpecial(attack) && attack != MarineAttack.R2 ||
            attack is MarineAttack.Summon or MarineAttack.Transition or MarineAttack.Punch or MarineAttack.Jump or MarineAttack.Switch)
        {
            _steering.Unregister(uid);
            _physics.SetLinearVelocity(uid, Vector2.Zero);
        }
        var spec = Wh40kChaosMarineCombat.Spec(attack);
        var speed = Wh40kChaosMarineCombat.AttackSpeed(state.PhaseTwo, state.HeatStep);
        state.Attack = attack;
        state.LastAttack = MarineAttack.None;
        state.MaceStop = null;
        brain.ActionTarget = brain.Target;
        state.Sequence++;
        state.Shot = 0;
        state.Origin = Local(uid, uid);
        state.Aim = brain.Target is { } target && CanTarget(uid, target) ? Local(uid, target) : state.Origin + Vector2.UnitX;
        state.Points = points ?? [];
        state.ResolvedRockets = [];
        state.RocketTargets = attack == MarineAttack.S1
            ? LiveTargets(uid, brain).OrderBy(t => Vector2.DistanceSquared(Local(uid, uid), Local(uid, t))).Take(4).ToList()
            : [];
        if (attack == MarineAttack.S1)
            for (var i = 0; i < state.Points.Length; i++)
                state.Points[i] = state.RocketTargets.Count > 0 ? Local(uid, state.RocketTargets[i % state.RocketTargets.Count]) : state.Aim;
        state.StartedAt = Now;
        if (attack == MarineAttack.E)
        {
            // A tactical cooldown stays four minutes even when rage changes the combat tempo.
            brain.NextSmoke = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.SmokeCooldownSeconds);
            if (TryComp<Wh40kChaosMarineTestComponent>(uid, out var smokeTest) && smokeTest.Attack == MarineAttack.E)
                smokeTest.NextCycle = Now + TimeSpan.FromSeconds(35);
        }
        if (attack == MarineAttack.R1)
            brain.NextBasicRanged = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.BasicCooldown(attack, state));
        if (attack == MarineAttack.M1)
            brain.NextBasicMelee = Now + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.BasicCooldown(attack, state));
        var windup = Wh40kChaosMarineCombat.Windup(attack, state);
        state.ExecuteAt = Now + TimeSpan.FromSeconds(windup);
        if (Wh40kChaosMarineCombat.IsRanged(attack) && !Wh40kChaosMarineCombat.IsBasic(attack) &&
            TryComp<GunComponent>(uid, out var gun) && gun.NextFire > state.ExecuteAt)
        {
            // A preceding basic shot must not delay this pattern's announced first shot/dash landing.
            gun.NextFire = state.ExecuteAt;
            DirtyField(uid, gun, nameof(GunComponent.NextFire));
        }
        state.EndsAt = state.ExecuteAt + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Duration(attack, state));
        if (Wh40kChaosMarineCombat.IsRanged(attack))
            state.EndsAt = state.ExecuteAt + TimeSpan.FromSeconds(spec.Duration / speed +
                (attack == MarineAttack.R5 ? 3.2f : attack == MarineAttack.R4 ? 5 : 2));
        if (Wh40kChaosMarineCombat.IsRocket(attack))
            state.EndsAt += TimeSpan.FromSeconds(attack == MarineAttack.S1 ? 6 : 3);
        brain.NextShot = state.ExecuteAt;
        state.NextShotAt = state.ExecuteAt;
        brain.ActionHit = brain.SummonPlaced = brain.SealsPrimed = brain.DashPending = brain.Landed = false;
        brain.ShotRetryUntil = TimeSpan.Zero;
        brain.MeleeStage = 0;
        brain.MeleeStageAt = state.ExecuteAt;
        brain.MeleeNextAt = state.ExecuteAt;
        brain.ThrowReturnReach = .6f;
        brain.DashPrevious = null;
        brain.PreviousProgress = 0;
        brain.Hit.Clear();
        if (attack == MarineAttack.Jump)
        {
            var direction = state.Aim - state.Origin;
            if (direction.Length() > spec.Reach)
                state.Aim = state.Origin + Vector2.Normalize(direction) * spec.Reach;
            state.Aim = ClipLine(uid, state.Origin, state.Aim, .45f);
        }
        if (attack == MarineAttack.R2)
        {
            state.Points = Wh40kChaosMarineCombat.FanPoints(state.Origin, state.Aim);
        }
        if (attack is MarineAttack.Summon or MarineAttack.Transition or MarineAttack.Jump)
            brain.CloseSince.Clear();
        if (!Wh40kChaosMarineCombat.IsBasic(attack))
        {
            if (Wh40kChaosMarineCombat.IsRanged(attack) || Wh40kChaosMarineCombat.IsMelee(attack) || Wh40kChaosMarineCombat.IsRocket(attack))
                brain.PatternUses[attack] = brain.PatternUses.GetValueOrDefault(attack) + 1;
            if (attack != brain.Previous) brain.Repeats = 0;
            brain.Previous = attack;
            brain.Repeats++;
        }
        Face(uid, state);
        if (Wh40kChaosMarineCombat.IsMelee(attack)) CreateMace(uid, brain, state);
        Dirty(uid, state);
        _speed.RefreshMovementSpeedModifiers(uid);
        if (Wh40kChaosMarineCombat.IsMelee(attack)) Sound(uid, "/Audio/Effects/thudswoosh.ogg", -4);
        if (Wh40kChaosMarineCombat.IsRocket(attack)) Sound(uid, "/Audio/Items/smoke_grenade_prime.ogg");
        if (attack == MarineAttack.R4)
            _chat.TrySendInGameICMessage(uid, Loc.GetString("wh40k-marine-ricochet-load"),
                Content.Shared.Chat.InGameICChatType.Emote, false, ignoreActionBlocker: true);
    }

    private void CancelAction(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        brain.ShotRetryUntil = TimeSpan.Zero;
        _steering.Unregister(uid);
        _phantomStep.CancelDash(uid);
        DestroyMace(uid, brain, state);
        foreach (var seal in brain.Seals) QueueDel(seal.Effect);
        brain.Seals.Clear();
        state.Sequence++;
        brain.CancellationSequence++;
        state.Attack = MarineAttack.None;
        state.LastAttack = MarineAttack.None;
        brain.ActionTarget = null;
        state.Points = [];
        state.RocketTargets = [];
        state.ResolvedRockets = [];
        state.TransitionProtected = false;
        Dirty(uid, state);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void Recover(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state, bool interrupted = false)
    {
        brain.ShotRetryUntil = TimeSpan.Zero;
        if (interrupted)
        {
            // In-flight R5 rounds must not turn into immediate ordinary explosions after an aborted salvo.
            state.Sequence++;
            brain.CancellationSequence++;
            _phantomStep.CancelDash(uid);
            foreach (var seal in brain.Seals) QueueDel(seal.Effect);
            brain.Seals.Clear();
        }
        var attack = state.Attack;
        var recovery = Wh40kChaosMarineCombat.Recovery(attack, state.PhaseTwo, state.HeatStep);
        if (!brain.ActionHit && attack is MarineAttack.M2 or MarineAttack.M3 or MarineAttack.M5 or MarineAttack.M6)
            recovery += Wh40kChaosMarineCombat.Delay(.4f, state, .2f);
        if (Wh40kChaosMarineCombat.IsRanged(attack) && !Wh40kChaosMarineCombat.IsBasic(attack) && ++brain.RangedSeries >= 2)
        {
            recovery = MathF.Max(recovery, Wh40kChaosMarineCombat.Delay(1.2f, state, .8f));
            brain.RangedSeries = 0;
        }
        state.RecoverUntil = Now + TimeSpan.FromSeconds(recovery);
        if (!Wh40kChaosMarineCombat.IsBasic(attack) &&
            (Wh40kChaosMarineCombat.IsRanged(attack) || Wh40kChaosMarineCombat.IsMelee(attack)))
            brain.NextPattern = state.RecoverUntil + TimeSpan.FromSeconds(Wh40kChaosMarineCombat.Delay(Wh40kChaosMarineCombat.PatternGapSeconds, state));
        state.WeaponReturnDuration = Wh40kChaosMarineCombat.Delay(
            attack == MarineAttack.M1 ? .3f : Wh40kChaosMarineCombat.WeaponReturnSeconds, state, attack == MarineAttack.M1 ? .15f : .3f);
        state.EndsAt = Now;
        state.LastAttack = attack;
        brain.NextAttack = state.RecoverUntil;
        state.Attack = MarineAttack.None;
        if (Wh40kChaosMarineCombat.IsStationarySpecial(attack) || attack is MarineAttack.Jump or MarineAttack.Transition)
        {
            _steering.Unregister(uid);
            _physics.SetLinearVelocity(uid, Vector2.Zero);
        }
        Dirty(uid, state);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void UpdateAction(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state, float frameTime)
    {
        if (state.Attack == MarineAttack.None)
        {
            if (state.RecoverUntil != TimeSpan.Zero && Now >= state.RecoverUntil)
            {
                state.RecoverUntil = TimeSpan.Zero;
                _speed.RefreshMovementSpeedModifiers(uid);
                Dirty(uid, state);
            }
            return;
        }
        var attack = state.Attack;
        if (attack == MarineAttack.E)
        {
            UpdateSmoke(uid, brain, state);
            return;
        }
        if (attack == MarineAttack.Transition)
        {
            if (!brain.SummonPlaced && Now >= state.StartedAt + TimeSpan.FromSeconds(2) &&
                !(TryComp<Wh40kChaosMarineTestComponent>(uid, out var phaseTest) && phaseTest.Attack == MarineAttack.Transition))
                PlaceSummons(uid, brain, state);
            UpdateTransitionBurst(uid, brain, state);
            if (Now < state.EndsAt) return;
            state.TransitionProtected = false;
            state.PhaseTwo = true;
            if (_bosses.TryGetHealth(uid, out var hp, out var maximum))
                state.HeatStep = Wh40kChaosMarineCombat.Overheat(hp, maximum);
            KeepFire(uid, state);
            Recover(uid, brain, state);
            return;
        }
        if (attack == MarineAttack.Summon && Now >= state.ExecuteAt)
        {
            PlaceSummons(uid, brain, state);
            state.Attack = MarineAttack.None;
            state.RecoverUntil = brain.NextAttack = Now;
            Dirty(uid, state);
            _speed.RefreshMovementSpeedModifiers(uid);
            return;
        }
        if (attack == MarineAttack.Switch)
        {
            if (Now < state.EndsAt) return;
            state.Stance = state.Stance == MarineStance.Ranged ? MarineStance.Melee : MarineStance.Ranged;
            Recover(uid, brain, state);
            return;
        }
        if (Wh40kChaosMarineCombat.IsRanged(attack) || Wh40kChaosMarineCombat.IsRocket(attack))
        {
            UpdateRanged(uid, brain, state);
            return;
        }
        if (Wh40kChaosMarineCombat.IsMelee(attack))
        {
            UpdateMeleeAction(uid, brain, state);
            return;
        }
        if (Now < state.ExecuteAt) return;
        var duration = MathF.Max(.001f, (float) (state.EndsAt - state.ExecuteAt).TotalSeconds);
        var progress = Math.Clamp((float) (Now - state.ExecuteAt).TotalSeconds / duration, 0, 1);
        var spec = Wh40kChaosMarineCombat.Spec(attack);
        if (attack == MarineAttack.Jump && !brain.Landed)
        {
            brain.Landed = true;
            _steering.Unregister(uid);
            _throwing.TryThrow(uid, _arena.World(state.Anchor!.Value, state.Aim) - _transform.GetWorldPosition(uid),
                spec.Reach / duration,
                compensateFriction: true, recoil: false, doSpin: false);
        }
        if (progress >= (attack == MarineAttack.Jump ? 1 : .5f) && state.Shot == 0)
        {
            state.Shot = 1;
            if (attack == MarineAttack.Jump)
            {
                var landing = Local(uid, uid);
                foreach (var target in LiveTargets(uid, brain))
                    if (Vector2.Distance(landing, Local(uid, target)) <= 2 && ClearLine(uid, landing, Local(uid, target), target))
                        _stun.TryParalyze(target, TimeSpan.FromSeconds(1), true);
                Effect(uid, state, landing, MarineEffect.Landing, 2, .6f);
                Sound(uid, "/Audio/Weapons/Guns/Gunshots/bang.ogg", -3);
            }
            else if (attack == MarineAttack.Punch)
            {
                if (brain.ActionTarget is { } target && CanTarget(uid, target) &&
                    brain.CloseSince.TryGetValue(target, out var closeSince) &&
                    Now - closeSince >= TimeSpan.FromSeconds(Wh40kChaosMarineCombat.PunchProximitySeconds) &&
                    Vector2.Distance(Local(uid, uid), Local(uid, target)) <= 2 && Visible(uid, target))
                {
                    ApplyHit(uid, brain, target, spec.Damage, 5);
                    RequestVoice(uid, brain, 20);
                }
                brain.NextPunch = Now + TimeSpan.FromSeconds(5);
                if (brain.ActionTarget is { } close) brain.CloseSince.Remove(close);
            }
            Dirty(uid, state);
        }
        brain.PreviousProgress = progress;
        if (Now >= state.EndsAt) Recover(uid, brain, state);
    }

    private void UpdateTransitionBurst(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        var elapsed = (float) (Now - state.StartedAt).TotalSeconds;
        if (elapsed < Wh40kChaosMarineCombat.TransitionBurstAt) return;
        if (state.Shot == 0)
        {
            state.Shot = 1;
            Sound(uid, "/Audio/Effects/explosion_small2.ogg", -3);
            var epicenter = _arena.World(state.Anchor!.Value, state.Origin);
            // Native explosions kick nearby cameras even when solid cover absorbs the blast.
            foreach (var session in _players.Sessions)
            {
                if (session.AttachedEntity is not { } target || !CanTarget(uid, target)) continue;
                var offset = _transform.GetWorldPosition(target) - epicenter;
                var strength = Wh40kChaosMarineCombat.TransitionBurstDamageAt(offset.Length()) /
                    Wh40kChaosMarineCombat.TransitionBurstDamage;
                if (strength <= 0) continue;
                var direction = offset.LengthSquared() > .001f ? Vector2.Normalize(offset) : Vector2.UnitX;
                _cameraRecoil.KickCamera(target, direction * (Wh40kChaosMarineCombat.TransitionBurstCameraKick * strength));
            }
            Dirty(uid, state);
        }
        var progress = Wh40kChaosMarineCombat.TransitionBurstProgress(elapsed);
        var previousRadius = brain.PreviousProgress * Wh40kChaosMarineCombat.TransitionBurstRadius;
        var radius = progress * Wh40kChaosMarineCombat.TransitionBurstRadius;
        if (progress <= brain.PreviousProgress) return;
        foreach (var target in LiveTargets(uid, brain).ToArray())
        {
            var point = Local(uid, target);
            var distance = Vector2.Distance(state.Origin, point);
            // Include contact with a normal mob's body at both ends of the swept interval.
            if (distance + .35f < previousRadius || distance - .35f > radius || !brain.Hit.Add(target)) continue;
            if (!ClearLine(uid, state.Origin, point, target)) continue;
            var damage = Wh40kChaosMarineCombat.TransitionBurstDamageAt(distance);
            if (damage <= 0) continue;
            brain.ActionHit = true;
            _damage.TryChangeDamage(target, new DamageSpecifier { DamageDict = { ["Heat"] = damage } },
                origin: uid, tool: uid, canSever: false, originFlag: DamageableSystem.DamageOriginFlag.Explosion);
            if (TerminatingOrDeleted(target)) continue;
            var force = damage / Wh40kChaosMarineCombat.TransitionBurstDamage;
            _stun.TryParalyze(target, TimeSpan.FromSeconds(Wh40kChaosMarineCombat.TransitionBurstStunSeconds * force), true);
            var impulse = _transform.GetWorldPosition(target) - _arena.World(state.Anchor!.Value, state.Origin);
            var direction = impulse.LengthSquared() > .001f ? Vector2.Normalize(impulse) : Vector2.UnitX;
            _throwing.TryThrow(target, direction * (Wh40kChaosMarineCombat.TransitionBurstKnockback * force), 8,
                user: uid, compensateFriction: true, recoil: false, doSpin: false);
        }
        brain.PreviousProgress = progress;
    }

    private void ApplyHit(EntityUid uid, Wh40kChaosMarineBrainComponent brain, EntityUid target, float amount, float knockback = 0, EntityUid? tool = null)
    {
        brain.ActionHit = true;
        _damage.TryChangeDamage(target, new DamageSpecifier { DamageDict = { ["Blunt"] = amount } }, origin: uid, tool: tool ?? uid, canSever: false);
        if (TerminatingOrDeleted(target)) return;
        Sound(uid, "/Audio/Weapons/smash.ogg", -3);
        var state = Comp<Wh40kChaosMarineComponent>(uid);
        Effect(uid, state, Local(uid, target), MarineEffect.Impact, .65f, .35f);
        if (knockback > 0)
        {
            var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(uid);
            if (direction.LengthSquared() > .001f)
                _throwing.TryThrow(target, Vector2.Normalize(direction) * knockback, 10, user: uid,
                    compensateFriction: true, recoil: false, doSpin: false);
        }
    }

    private bool ValidPoint(EntityUid uid, Wh40kChaosMarineComponent state, Vector2 point, float margin) =>
        TryComp<Wh40kBossArenaComponent>(uid, out var arena) &&
        Wh40kChaosMarineCombat.InArena(point, arena.Center, arena.HalfSize, margin);

    private bool ClearLine(EntityUid uid, Vector2 from, Vector2 to, EntityUid? target = null)
    {
        var state = Comp<Wh40kChaosMarineComponent>(uid);
        var start = _arena.World(state.Anchor!.Value, from);
        var delta = _arena.World(state.Anchor.Value, to) - start;
        if (delta.LengthSquared() < .001f) return true;
        return !_physics.IntersectRay(Transform(uid).MapID,
            new CollisionRay(start, Vector2.Normalize(delta), (int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable)),
            delta.Length(), uid, false).Any(h => h.HitEntity != target && !HasComp<Content.Shared.Mobs.Components.MobStateComponent>(h.HitEntity));
    }

    private Vector2 ClipLine(EntityUid uid, Vector2 from, Vector2 to, float padding)
    {
        var state = Comp<Wh40kChaosMarineComponent>(uid);
        var start = _arena.World(state.Anchor!.Value, from);
        var delta = _arena.World(state.Anchor.Value, to) - start;
        if (delta.LengthSquared() < .01f) return from;
        var length = delta.Length();
        var hit = _physics.IntersectRay(Transform(uid).MapID,
            new CollisionRay(start, delta / length, (int) CollisionGroup.Impassable), length, uid, true).FirstOrDefault();
        return hit.HitEntity.IsValid() ? from + Vector2.Normalize(to - from) * MathF.Max(0, hit.Distance - padding) : to;
    }

}
