using System.Numerics;

namespace Content.Client._WH40K.FtlEffects;

/// <summary>Fits a rounded conical field from midship to just ahead of the bow.</summary>
public static class Wh40kFtlConeGeometry
{
    public static Box2 Bounds(Box2 hull)
    {
        var width = hull.Width * 0.8f + 1f;
        var nose = hull.Top + Math.Clamp(hull.Width * 0.2f, 0.75f, 6f);
        return new Box2(hull.Center.X - width, hull.Center.Y, hull.Center.X + width, nose);
    }

    public static Matrix3x2 MotionTransform(Box2 hull, float motion)
    {
        var distance = Math.Clamp(hull.Height * 3f, 30f, 120f);
        var stretch = 1f + MathF.Abs(motion) * 2.5f;
        return Matrix3x2.CreateTranslation(-hull.Center)
            * Matrix3x2.CreateScale(1f, stretch)
            * Matrix3x2.CreateTranslation(hull.Center + new Vector2(0f, motion * distance));
    }

    public static Matrix3x2 FollowerTransform(Box2 leadHull, float motion, Matrix3x2 follower, Matrix3x2 lead)
    {
        if (!Matrix3x2.Invert(lead, out var inverse))
            return follower;
        return follower * inverse * MotionTransform(leadHull, motion) * lead;
    }
}
