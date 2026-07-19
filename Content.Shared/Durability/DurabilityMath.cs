using System.Globalization;

namespace Content.Shared.Durability;

/// <summary>
/// Common precision rules for durability values. Durability is intentionally limited to one decimal place so repeated
/// fractional drains such as 0.1 cannot leak binary floating-point noise into the gameplay state or examine text.
/// </summary>
public static class DurabilityMath
{
    public const int DecimalPlaces = 1;
    public const float MinimumArmorDamageDrain = 0.1f;

    public static float Round(float value)
    {
        return !float.IsFinite(value)
            ? value
            : MathF.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero);
    }

    public static float Clamp(float value, float maximum)
    {
        var roundedMaximum = Round(maximum);
        if (!float.IsFinite(roundedMaximum) || roundedMaximum <= 0f)
            return 0f;

        var roundedValue = Round(value);
        if (!float.IsFinite(roundedValue))
            return roundedMaximum;

        return Math.Clamp(roundedValue, 0f, roundedMaximum);
    }

    public static string Format(float value)
    {
        var rounded = Round(value);
        return float.IsFinite(rounded)
            ? rounded.ToString("0.0", CultureInfo.InvariantCulture)
            : "0.0";
    }

    /// <summary>
    /// Converts damage prevented by armor into durability wear. Any real absorption costs at least one durability
    /// precision step, while invalid or disabled values do not spend durability.
    /// </summary>
    public static float CalculateArmorDamageDrain(float absorbedDamage, float drainMultiplier)
    {
        if (!float.IsFinite(absorbedDamage) || absorbedDamage <= 0f ||
            !float.IsFinite(drainMultiplier) || drainMultiplier <= 0f)
        {
            return 0f;
        }

        var drain = absorbedDamage * drainMultiplier;
        if (!float.IsFinite(drain) || drain <= 0f)
            return 0f;

        return MathF.Max(MinimumArmorDamageDrain, Round(drain));
    }
}
