using System.Numerics;

namespace Content.Client.Wall;

internal static class WallMountVisibilityGeometry
{
    // Keep the sight target outside the backing wall's occluder.
    private const float FaceMargin = 0.03f;

    public static Vector2 FacePoint(Vector2 wallCenter, Vector2 facing, float tileSize)
    {
        return wallCenter + facing * (tileSize / 2f + FaceMargin);
    }

    public static bool FacesEye(Vector2 facePoint, Vector2 facing, Angle arc, Vector2 eyePosition)
    {
        var toEye = eyePosition - facePoint;
        var distanceSquared = toEye.LengthSquared();
        if (distanceSquared < 0.0001f)
            return true;

        return Vector2.Dot(toEye, facing) >
            MathF.Cos((float) (arc.Theta / 2)) * MathF.Sqrt(distanceSquared);
    }
}
