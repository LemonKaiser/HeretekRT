using System.Globalization;
using System.Numerics;
using System.Text;

namespace Content.Shared._WH40K.Waypoints;

/// <summary>
/// Limits and coordinate/name sanitization shared between the radar waypoint panel and server validation.
/// </summary>
public static class Wh40kWaypointRules
{
    public const int MaxWaypoints = 16;
    public const int MaxNameLength = 24;
    public const float MaxCoordinate = 1_000_000f;

    public static bool IsValidPosition(Vector2 position)
    {
        return float.IsFinite(position.X) && float.IsFinite(position.Y) &&
               MathF.Abs(position.X) <= MaxCoordinate && MathF.Abs(position.Y) <= MaxCoordinate;
    }

    public static bool TryParsePosition(string x, string y, out Vector2 position)
    {
        position = default;
        if (!TryParseCoordinate(x, out var parsedX) || !TryParseCoordinate(y, out var parsedY))
            return false;

        position = new Vector2(parsedX, parsedY);
        return IsValidPosition(position);
    }

    private static bool TryParseCoordinate(string value, out float coordinate)
    {
        // The radar formats picked coordinates using the player's decimal separator.
        return float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out coordinate);
    }

    public static string CleanName(string name)
    {
        var clean = new StringBuilder(MaxNameLength);
        foreach (var rune in name.Trim().EnumerateRunes())
        {
            if (Rune.IsControl(rune))
                continue;
            if (clean.Length + rune.Utf16SequenceLength > MaxNameLength)
                break;
            clean.Append(rune.ToString());
        }

        return clean.ToString().Trim();
    }
}
