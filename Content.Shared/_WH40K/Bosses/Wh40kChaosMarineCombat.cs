using System.Numerics;

namespace Content.Shared._WH40K.Bosses;

public readonly record struct MarineAttackSpec(
    float Windup, float Duration, float Recovery, float Reach = 0,
    float Width = 0, float Damage = 0, int Shots = 0);

public static class Wh40kChaosMarineCombat
{
    public const float FireDamagePerStack = 1.5f;
    public const float FireHitWindowSeconds = 10;
    public const float PunchProximitySeconds = 3;
    public const float WeaponReturnSeconds = .65f;
    public const float PatternGapSeconds = 1.5f;
    public const float DeathDissolveSeconds = 1;
    public const float JumpInitialDelaySeconds = 6;
    public const float JumpCooldownSeconds = 20;
    public const float SmokeCooldownSeconds = 240;
    public const float RicochetReadSeconds = 1;
    public const float BarrageWindup = 4;
    public const float BarrageInterval = .1f;
    public const int BarrageLimit = 40;
    public const float HomingRocketSpeed = 7;
    public const float HomingRocketTurnRate = 2.6f;
    public const float TransitionBurstAt = 4.25f;
    public const float TransitionBurstSeconds = .16f;
    public const float TransitionBurstRadius = 6;
    public const float TransitionBurstDamage = 30;
    public const float TransitionBurstKnockback = 3;
    public const float TransitionBurstStunSeconds = 1.2f;
    public const float TransitionBurstCameraKick = .8f;

    public static float TransitionBurstProgress(float elapsed) =>
        Math.Clamp((elapsed - TransitionBurstAt) / TransitionBurstSeconds, 0, 1);

    public static float TransitionBurstDamageAt(float distance) =>
        TransitionBurstDamage * Math.Clamp(1 - distance / TransitionBurstRadius, 0, 1);

    public static float Smooth(float progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        return p * p * (3 - 2 * p);
    }

    public static bool WeaponVisible(Wh40kChaosMarineComponent state, TimeSpan now) =>
        IsMelee(state.Attack) || state.Attack == MarineAttack.None && IsMelee(state.LastAttack) &&
        now < state.RecoverUntil && now < state.EndsAt + TimeSpan.FromSeconds(state.WeaponReturnDuration);

    public static MarineAttackSpec Spec(MarineAttack attack) => attack switch
    {
        MarineAttack.R1 => new(.12f, 0, .05f, Shots: 1),
        MarineAttack.M1 => new(.28f, .32f, .3f, 2, 90, 25),
        MarineAttack.R2 => new(.45f, 3.5f, 1.8f, Shots: 8),
        MarineAttack.R3 => new(.65f, 6.4f, 1.5f, Shots: 20),
        MarineAttack.R4 => new(.6f, 8.4f, 1.2f, Shots: 15),
        MarineAttack.R5 => new(.7f, 1.8f, 2, Shots: 7),
        MarineAttack.R6 => new(.4f, 3.4f, 1.2f, Shots: 6),
        MarineAttack.M2 => new(.65f, 3.1f, 1.65f, 4.6f, 180, 32),
        MarineAttack.M3 => new(.55f, 4.2f, 1.3f, 5, 100, 32),
        MarineAttack.M4 => new(.8f, 12, 1.8f, 2.4f, Damage: 12),
        MarineAttack.M5 => new(.65f, 9, 1.6f, 10, Damage: 38, Shots: 6),
        MarineAttack.M6 => new(.7f, 2.65f, 1.5f, 4.2f, Damage: 30),
        MarineAttack.S1 => new(1.2f, 1.8f, 3, 1.5f, Damage: 38, Shots: 4),
        MarineAttack.S2 or MarineAttack.S3 or MarineAttack.S4 => new(BarrageWindup, 1.9f, 3, 1, Damage: 50, Shots: BarrageLimit),
        MarineAttack.Punch => new(.35f, .1f, .8f, 2, Damage: 25),
        MarineAttack.Jump => new(.8f, .4f, 1.5f, 10, 2),
        MarineAttack.Summon => new(2, 2, .5f),
        MarineAttack.Transition => new(0, 5, .65f),
        MarineAttack.Switch => new(0, .35f, .15f),
        MarineAttack.E => new(.45f, .45f, .35f, 3, Shots: 4),
        _ => new(0, 0, 0),
    };

    public static int Overheat(float health, float maximum)
    {
        if (!float.IsFinite(health) || !float.IsFinite(maximum) || maximum <= 0)
            return 0;
        return Math.Clamp((int) MathF.Floor((.5f - health / maximum) * 20 + .00001f), 0, 9);
    }

    public static float AttackSpeed(bool phaseTwo, int heatStep) =>
        phaseTwo ? 1.1f + .4f * Math.Clamp(heatStep, 0, 9) / 9 : 1;

    public static float Delay(float seconds, Wh40kChaosMarineComponent state, float minimum = 0) =>
        MathF.Max(minimum, seconds / AttackSpeed(state.PhaseTwo, state.HeatStep));

    public static float Windup(MarineAttack attack, Wh40kChaosMarineComponent state) => attack switch
    {
        MarineAttack.Transition or MarineAttack.Switch => 0,
        MarineAttack.Summon => 2,
        MarineAttack.Punch => .35f,
        MarineAttack.R1 => Delay(.12f, state, .08f),
        MarineAttack.R4 => RicochetReadSeconds + Delay(Spec(attack).Windup, state, .3f),
        MarineAttack.S2 or MarineAttack.S3 or MarineAttack.S4 => BarrageWindup,
        MarineAttack.M1 => Delay(Spec(attack).Windup, state, .17f),
        MarineAttack.Jump => Delay(Spec(attack).Windup, state, .45f),
        _ => Delay(Spec(attack).Windup, state, IsMelee(attack) ? .4f : IsRocket(attack) ? .65f : .3f),
    };

    public static float Duration(MarineAttack attack, Wh40kChaosMarineComponent state) =>
        IsBarrage(attack) || attack is MarineAttack.M4 or MarineAttack.Summon or MarineAttack.Punch or MarineAttack.Transition
            ? Spec(attack).Duration : Delay(Spec(attack).Duration, state);

    public static float ShotInterval(MarineAttack attack, int shotsFired, Wh40kChaosMarineComponent state) =>
        IsBarrage(attack) ? BarrageInterval : Delay(attack switch
        {
            MarineAttack.R2 => .5f,
            MarineAttack.R3 => MathF.Max(.1f, 1 - .1f * (shotsFired - 1)),
            MarineAttack.R4 => .6f,
            MarineAttack.R5 => .3f,
            MarineAttack.R6 => .4f,
            MarineAttack.S2 or MarineAttack.S3 or MarineAttack.S4 => BarrageInterval,
            _ => IsRocket(attack) ? .6f : 1,
        }, state, IsBarrage(attack) ? .06f : IsRocket(attack) ? .18f : .06f);

    public static float DashDuration(Wh40kChaosMarineComponent state) => Delay(.16f, state);

    public static float AimLock(Wh40kChaosMarineComponent state) => Delay(.35f, state, .2f);

    public static bool IsBasic(MarineAttack attack) => attack is MarineAttack.R1 or MarineAttack.M1;

    public static float BasicCooldown(MarineAttack attack, Wh40kChaosMarineComponent state) =>
        Delay(attack == MarineAttack.M1 ? 1.5f : 1, state);

    public static float HeatMultiplier(bool phaseTwo, int heatStep) =>
        phaseTwo ? .25f + .25f * Math.Clamp(heatStep, 0, 9) / 9 : .1f;

    public static int FireStackLimit(bool phaseTwo, int heatStep) =>
        phaseTwo ? 2 + (int) MathF.Round(10f * Math.Clamp(heatStep, 0, 9) / 9) : 2;

    public static float Recovery(MarineAttack attack, bool phaseTwo, int heatStep)
    {
        var spec = Spec(attack);
        if (IsBasic(attack))
            return spec.Recovery / AttackSpeed(phaseTwo, heatStep);
        if (!phaseTwo || attack is MarineAttack.Summon or MarineAttack.Punch)
            return spec.Recovery;
        var minimum = attack switch
        {
            MarineAttack.Switch => .08f,
            MarineAttack.E => .2f,
            MarineAttack.Transition => .4f,
            MarineAttack.Jump => 1,
            _ => IsRocket(attack) ? 1.5f : IsStationarySpecial(attack) ? 1.2f : .6f,
        };
        return MathF.Max(minimum, spec.Recovery / AttackSpeed(true, heatStep));
    }

    public static bool IsStationarySpecial(MarineAttack attack) =>
        attack is MarineAttack.R2 or MarineAttack.R5 or MarineAttack.M2 or MarineAttack.M5;

    public static bool MovementLocked(Wh40kChaosMarineComponent state, TimeSpan now) =>
        state.Dying || IsBarrage(state.Attack) ||
        IsStationarySpecial(state.Attack) && state.Attack != MarineAttack.R2 ||
        state.Attack is MarineAttack.Transition or MarineAttack.Summon or
            MarineAttack.Punch or MarineAttack.Jump or MarineAttack.Switch ||
        state.Attack == MarineAttack.M3 && now >= state.ExecuteAt ||
        (IsRocket(state.Attack) || state.Attack is MarineAttack.R2 or MarineAttack.R3 or MarineAttack.R6) &&
            now >= state.ExecuteAt && state.Shot < (IsRocket(state.Attack) ? state.Points.Length : Spec(state.Attack).Shots) ||
        state.Attack == MarineAttack.None && now < state.RecoverUntil && IsStationarySpecial(state.LastAttack);

    public static bool IsRanged(MarineAttack attack) => attack >= MarineAttack.R1 && attack <= MarineAttack.R6;
    public static bool IsMelee(MarineAttack attack) => attack >= MarineAttack.M1 && attack <= MarineAttack.M6;
    public static bool IsRocket(MarineAttack attack) => attack is MarineAttack.S1 or MarineAttack.S2 or MarineAttack.S3 or MarineAttack.S4;

    public static bool IsBarrage(MarineAttack attack) => attack is MarineAttack.S2 or MarineAttack.S3 or MarineAttack.S4;

    public static bool InArena(Vector2 point, Vector2 center, Vector2 halfSize, float margin = 0) =>
        MathF.Abs(point.X - center.X) <= halfSize.X - margin &&
        MathF.Abs(point.Y - center.Y) <= halfSize.Y - margin;

    public static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
    {
        var delta = end - start;
        var length = delta.LengthSquared();
        return Vector2.Distance(point, length < .00001f ? start :
            start + delta * Math.Clamp(Vector2.Dot(point - start, delta) / length, 0, 1));
    }

    public static bool InSector(Vector2 offset, Vector2 direction, float reach, float degrees)
    {
        if (offset.LengthSquared() > reach * reach)
            return false;
        if (offset.LengthSquared() < .00001f)
            return true;
        return Vector2.Dot(Vector2.Normalize(offset), Vector2.Normalize(direction)) >=
            MathF.Cos(degrees * MathF.PI / 360);
    }

    public static Vector2[] SealPoints(Vector2 center)
    {
        var points = new Vector2[7];
        for (var i = 0; i < 6; i++)
        {
            var angle = i * MathF.Tau / 6;
            points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 2.7f;
        }
        points[6] = center;
        return points;
    }

    public static Vector2[] FanPoints(Vector2 origin, Vector2 aim)
    {
        var delta = aim - origin;
        var heading = delta.LengthSquared() > .001f ? MathF.Atan2(delta.Y, delta.X) : 0;
        var points = new Vector2[8];
        for (var i = 0; i < points.Length; i++)
        {
            // Looking along the firing direction, the left-hand lane fires first.
            var angle = heading + (35 - i * 10) * MathF.PI / 180;
            points[i] = origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 30;
        }
        return points;
    }

    public static Vector2 OrbitDash(Vector2 origin, Vector2 target, int side)
    {
        var radial = origin - target;
        var radius = radial.Length();
        if (radius < 1.51f)
        {
            var forward = radius > .01f ? -radial / radius : Vector2.UnitX;
            return origin + new Vector2(-forward.Y, forward.X) * side * 3;
        }
        var angle = 2 * MathF.Asin(3 / (2 * radius)) * side;
        var rotated = new Vector2(radial.X * MathF.Cos(angle) - radial.Y * MathF.Sin(angle),
            radial.X * MathF.Sin(angle) + radial.Y * MathF.Cos(angle));
        return target + rotated;
    }

    public static Vector2 Head(MarineAttack attack, Vector2 origin, Vector2 aim, float progress)
    {
        var delta = aim - origin;
        var direction = delta.LengthSquared() > .001f ? Vector2.Normalize(delta) : Vector2.UnitX;
        var pose = Wh40kChaosMarineMacePhysics.Pose(attack, progress);
        return origin + direction * pose.Offset.X + new Vector2(-direction.Y, direction.X) * pose.Offset.Y;
    }
}
