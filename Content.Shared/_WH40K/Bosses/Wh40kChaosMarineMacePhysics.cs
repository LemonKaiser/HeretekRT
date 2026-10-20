using System.Numerics;

namespace Content.Shared._WH40K.Bosses;

/// <summary>Motor targets are intentions; only the physics solver moves the mace and determines contacts.</summary>
public static class Wh40kChaosMarineMacePhysics
{
    public const int LinkCount = 10;
    public const float HeadRadius = .28f;
    public const float HeadScale = .8533333f; // 21 px * scale / 32 = 0.56 tiles.

    public static float ChainLength(MarineAttack attack, Vector2 origin, Vector2 aim) =>
        MathF.Min(Wh40kChaosMarineCombat.Spec(attack).Reach + .55f,
            attack == MarineAttack.M5 ? Vector2.Distance(origin, aim) + .55f : Pose(attack, 0).Offset.Length() + .55f);

    public static MarineMacePose Pose(MarineAttack attack, float progress, float speed = 1)
    {
        var p = Math.Clamp(progress, 0, 1);
        var t = p * Wh40kChaosMarineCombat.Spec(attack).Duration;
        switch (attack)
        {
            case MarineAttack.M1:
                // Like WeaponArcSlash: the left endpoint precedes the right endpoint.
                return Arc(1.7f, MathF.PI / 2, p, 0);
            case MarineAttack.M2:
                if (t < .48f) return Arc(2.65f, MathF.PI, t / .48f, 0);
                if (t < .72f) return Arc(2.65f, MathF.PI, 1, -1);
                if (t < 1.2f) return Arc(2.65f, MathF.PI, 1 - (t - .72f) / .48f, 1);
                if (t < 1.6f) return Arc(2.65f + 1.7f * Wh40kChaosMarineCombat.Smooth((t - 1.2f) / .4f), MathF.PI, 0, -1);
                if (t < 2.18f) return Arc(4.35f, MathF.PI, (t - 1.6f) / .58f, 2);
                if (t < 2.46f) return Arc(4.35f, MathF.PI, 1, -1);
                if (t < 3.04f) return Arc(4.35f, MathF.PI, 1 - (t - 2.46f) / .58f, 3);
                return Arc(4.35f, MathF.PI, 0, -1);
            case MarineAttack.M3:
                return Arc(1.8f, 100 * MathF.PI / 180, p, 0);
            case MarineAttack.M4:
                var turns = t * 2 * speed;
                var radius = 2.4f * (.35f + .65f * Wh40kChaosMarineCombat.Smooth(t / .3f));
                return new(new Vector2(MathF.Cos(turns * MathF.Tau), MathF.Sin(turns * MathF.Tau)) * radius,
                    t < .3f ? -1 : (int) turns);
            case MarineAttack.M5:
                return Throw(10, p, false);
            case MarineAttack.M6:
                if (t < .65f) return Loop(t / .65f * MathF.PI, 0);
                if (t < .9f) return new(new Vector2(1.8f, 0), -1);
                if (t < 1.55f) return Loop(MathF.PI + (t - .9f) / .65f * MathF.PI, 1);
                if (t < 1.95f) return new(new Vector2(1.8f - .9f * Wh40kChaosMarineCombat.Smooth((t - 1.55f) / .4f), 0), -1);
                if (t < 2.35f) return new(new Vector2(.9f + 3.3f * Wh40kChaosMarineCombat.Smooth((t - 1.95f) / .4f), 0), 2);
                return new(new Vector2(4.2f - 3.4f * Wh40kChaosMarineCombat.Smooth((t - 2.35f) / .3f), 0), -1);
            default:
                return new(new Vector2(.6f, 0), -1);
        }
    }

    public static MarineMacePose Throw(float distance, float progress, bool returning)
    {
        var p = Wh40kChaosMarineCombat.Smooth(progress);
        var reach = Math.Clamp(distance, 1, 10);
        return new(new Vector2(returning ? reach + (.6f - reach) * p : .6f + (reach - .6f) * p, 0), returning ? -1 : 0);
    }

    private static MarineMacePose Arc(float radius, float width, float progress, int strike)
    {
        var angle = (.5f - Wh40kChaosMarineCombat.Smooth(progress)) * width;
        return new(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius, strike);
    }

    private static MarineMacePose Loop(float angle, int strike) =>
        new(new Vector2(1.8f + .9f * MathF.Sin(2 * angle), 1.8f * MathF.Sin(angle)), strike);

    public static Vector2 Acceleration(Vector2 position, Vector2 velocity, Vector2 goal, Vector2 goalVelocity,
        float speed, bool returning, float frameTime)
    {
        var frequency = (returning ? 14 : 42) * speed;
        var acceleration = (goal - position) * (frequency * frequency) +
                           (goalVelocity - velocity) * (2 * frequency);
        var step = Math.Clamp(frameTime, .001f, .1f) * frequency;
        acceleration /= 1 + 2 * step + step * step;
        var limit = (returning ? 90 : 650) * speed * speed;
        var length = acceleration.Length();
        return length > limit ? acceleration * (limit / length) : acceleration;
    }

    public static Vector2 SpinAcceleration(Vector2 offset, Vector2 relativeVelocity, float radius, float speed, float inertia, float frameTime)
    {
        var distance = MathF.Max(.2f, offset.Length());
        var radial = offset.LengthSquared() > .001f ? Vector2.Normalize(offset) : Vector2.UnitX;
        var tangent = new Vector2(-radial.Y, radial.X);
        var step = Math.Clamp(frameTime, .001f, .1f);
        // Brake the tangential motion while reeling an overextended chain back to its intended orbit.
        var angle = MathF.Tau * 2 * speed * step * MathF.Min(1, radius / distance);
        var nextRadius = distance + (radius - distance) * (1 - MathF.Exp(-12 * speed * step));
        // Turn from the actual head position, rather than chasing a point that runs ahead of the solver.
        // The velocity motor applies bounded force; collisions can still stop or deflect the real body.
        var nextOffset = (radial * MathF.Cos(angle) + tangent * MathF.Sin(angle)) * nextRadius;
        var wantedVelocity = (nextOffset - offset) / step;
        var acceleration = (wantedVelocity - relativeVelocity) / step * inertia;
        var limit = 1200 * speed * speed;
        var length = acceleration.Length();
        return length > limit ? acceleration * (limit / length) : acceleration;
    }

    public static int Strike(MarineAttack attack, float progress) => Pose(attack, progress).Strike;
}

public readonly record struct MarineMacePose(Vector2 Offset, int Strike);
