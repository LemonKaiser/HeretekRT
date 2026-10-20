using System.Numerics;

namespace Content.Shared._WH40K.Bosses;

/// <summary>Body recoil and wrist anchors. The mace itself is moved and rotated by physics.</summary>
public static class Wh40kChaosMarineWeaponAnimation
{
    private static float Fraction(TimeSpan now, TimeSpan start, TimeSpan duration) =>
        Math.Clamp((float) ((now - start).TotalSeconds / Math.Max(.001, duration.TotalSeconds)), 0, 1);

    public static Vector2 BodyOffset(Wh40kChaosMarineComponent state, TimeSpan now, Vector2 facing)
    {
        if (!Wh40kChaosMarineCombat.IsMelee(state.Attack) || state.Attack is MarineAttack.M3 or MarineAttack.M4) return Vector2.Zero;
        var preparing = now < state.ExecuteAt;
        var p = Fraction(now, preparing ? state.StartedAt : state.ExecuteAt,
            preparing ? state.ExecuteAt - state.StartedAt : state.EndsAt - state.ExecuteAt);
        return facing * ((preparing ? -.045f : .035f) * MathF.Sin(p * MathF.PI));
    }

    // Wrist and resting head anchors match the four supplied 64 px poses (32 px per tile).
    public static Vector2 Grip(Vector2 facing) => MathF.Abs(facing.X) > MathF.Abs(facing.Y)
        ? facing.X > 0 ? new(.1f, .2f) : new(-.28f, .18f)
        : facing.Y > 0 ? new(-.24f, .22f) : new(.24f, .22f);

    public static Vector2 Rest(Vector2 facing) => MathF.Abs(facing.X) > MathF.Abs(facing.Y)
        ? facing.X > 0 ? new(.13f, -.06f) : new(-.41f, -.12f)
        : facing.Y > 0 ? new(-.56f, -.08f) : new(.54f, -.08f);
}
