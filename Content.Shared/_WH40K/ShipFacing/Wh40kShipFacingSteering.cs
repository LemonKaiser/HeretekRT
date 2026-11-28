using System.Numerics;

namespace Content.Shared._WH40K.ShipFacing;

/// <summary>
/// Computes shuttle rotation input that points the vessel's nose (local north) toward a world point and stops cleanly.
/// </summary>
public static class Wh40kShipFacingSteering
{
    public const float MinDistance = 1f;
    public const float Deadband = 0.004f;
    public const float DeadbandSpeed = 0.02f;
    public const float BrakeMargin = 0.8f;
    public const float FinalGain = 4f;

    public static Angle Heading(Vector2 from, Vector2 to)
    {
        var dir = to - from;
        return new Angle(MathF.Atan2(dir.Y, dir.X) - MathF.PI / 2f);
    }

    public static float TurnInput(
        Angle rotation,
        float angularVelocity,
        Vector2 center,
        Vector2 target,
        float angularAccel,
        float maxAngularVelocity,
        float frameTime)
    {
        if (angularAccel <= 0f || frameTime <= 0f || (target - center).LengthSquared() < MinDistance * MinDistance)
            return 0f;

        var error = (float) Angle.ShortestDistance(rotation, Heading(center, target)).Theta;
        if (MathF.Abs(error) < Deadband && MathF.Abs(angularVelocity) < DeadbandSpeed)
            return 0f;

        var speed = MathF.Min(MathF.Sqrt(2f * angularAccel * BrakeMargin * MathF.Abs(error)), FinalGain * MathF.Abs(error));
        var wanted = MathF.Sign(error) * MathF.Min(speed, maxAngularVelocity);

        return Math.Clamp((angularVelocity - wanted) / (angularAccel * frameTime), -1f, 1f);
    }
}
