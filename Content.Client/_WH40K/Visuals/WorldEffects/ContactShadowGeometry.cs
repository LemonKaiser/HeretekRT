using System.Numerics;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>
/// Projects opaque sprite pixels onto two world-space axes before placing a shadow.
/// </summary>
public struct ContactShadowProjectedBounds
{
    public bool Valid { get; private set; }
    public float MinX { get; private set; }
    public float MaxX { get; private set; }
    public float MinY { get; private set; }
    public float MaxY { get; private set; }
    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;
    public float CenterX => (MinX + MaxX) * 0.5f;
    public float CenterY => (MinY + MaxY) * 0.5f;

    public void Include(Box2 bounds, Matrix3x2 transform, Vector2 axisX, Vector2 axisY)
    {
        IncludePoint(new Vector2(bounds.Left, bounds.Bottom), transform, axisX, axisY);
        IncludePoint(new Vector2(bounds.Left, bounds.Top), transform, axisX, axisY);
        IncludePoint(new Vector2(bounds.Right, bounds.Bottom), transform, axisX, axisY);
        IncludePoint(new Vector2(bounds.Right, bounds.Top), transform, axisX, axisY);
    }

    private void IncludePoint(Vector2 local, Matrix3x2 transform, Vector2 axisX, Vector2 axisY)
    {
        var world = Vector2.Transform(local, transform);
        var x = Vector2.Dot(world, axisX);
        var y = Vector2.Dot(world, axisY);
        if (!Valid)
        {
            MinX = MaxX = x;
            MinY = MaxY = y;
            Valid = true;
            return;
        }

        MinX = MathF.Min(MinX, x);
        MaxX = MathF.Max(MaxX, x);
        MinY = MathF.Min(MinY, y);
        MaxY = MathF.Max(MaxY, y);
    }
}

public readonly record struct ContactShadowShape(Vector2 Center, Vector2 Size, Angle Rotation);

public static class ContactShadowGeometry
{
    public static ContactShadowShape Standing(
        ContactShadowProjectedBounds feet,
        ContactShadowProjectedBounds body,
        Vector2 screenRight,
        Vector2 screenUp,
        Angle rotation)
    {
        var contact = feet.Valid ? feet : body;
        var width = MathF.Max(0.24f, MathF.Max(contact.Width, body.Width * 0.75f) + 0.16f);
        var height = Math.Clamp(width * 0.3f, 0.15f, 0.32f);
        // Keep the dark center at the contact line while the soft half extends below the feet.
        var center = screenRight * contact.CenterX + screenUp * (contact.MinY + height * 0.08f);
        return new ContactShadowShape(center, new Vector2(width, height), rotation);
    }

    public static ContactShadowShape Lying(
        ContactShadowProjectedBounds body,
        Vector2 bodyAxis,
        Vector2 crossAxis,
        Vector2 screenUp,
        Angle rotation)
    {
        var center = bodyAxis * body.CenterX + crossAxis * body.CenterY;
        var length = MathF.Max(0.34f, body.Width * 0.9f);
        var thickness = Math.Clamp(body.Height * 0.44f, 0.18f, 0.52f);
        // Follow the lower edge of the rotated body, including the brief rotation animation.
        var lowerExtent = MathF.Abs(Vector2.Dot(bodyAxis, screenUp)) * body.Width * 0.5f +
                          MathF.Abs(Vector2.Dot(crossAxis, screenUp)) * body.Height * 0.5f;
        center -= screenUp * (lowerExtent - thickness * 0.08f);
        return new ContactShadowShape(center, new Vector2(length, thickness), rotation);
    }
}
