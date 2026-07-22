using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared.Armor;

/// <summary>
/// Deterministic numerical armor calculations shared by the damage pipeline and tests.
/// </summary>
public static class ArmorRatingMath
{
    /// <summary>
    /// Armor rating that provides 50% numerical physical damage reduction before penetration.
    /// </summary>
    public const float ArmorScale = 200f;

    public static bool IsPhysicalDamageType(string damageType)
    {
        return damageType is "Blunt" or "Slash" or "Piercing";
    }

    public static ArmorRatingResult Apply(
        DamageSpecifier currentDamage,
        float armorRating,
        float armorPenetration)
    {
        var effectiveArmor = GetEffectiveArmor(armorRating, armorPenetration);
        if (effectiveArmor <= 0f)
        {
            return new ArmorRatingResult(
                currentDamage,
                effectiveArmor,
                1f,
                0f);
        }

        var damageMultiplier = ArmorScale / (ArmorScale + effectiveArmor);
        var currentPhysical = GetPositivePhysicalDamage(currentDamage);
        var result = new DamageSpecifier();

        foreach (var (type, amount) in currentDamage.DamageDict)
        {
            if (amount <= FixedPoint2.Zero || !IsPhysical(type))
            {
                result.DamageDict[type] = amount;
                continue;
            }

            var remaining = amount.Float() * damageMultiplier;
            if (remaining > 0f)
                result.DamageDict[type] = FixedPoint2.New(remaining);
        }

        var finalPhysical = GetPositivePhysicalDamage(result);
        var absorbedDamage = Math.Max(0f, currentPhysical - finalPhysical);

        return new ArmorRatingResult(
            result,
            effectiveArmor,
            damageMultiplier,
            absorbedDamage);
    }

    /// <summary>
    ///     Applies flat armor penetration in points. Armor penetration is not a percentage and does not
    ///     alter the target's damage-type coefficients.
    /// </summary>
    public static float GetEffectiveArmor(float armorRating, float armorPenetration)
    {
        if (!float.IsFinite(armorRating) || armorRating <= 0f)
            return 0f;

        if (!float.IsFinite(armorPenetration))
            armorPenetration = 0f;

        return Math.Max(0f, armorRating - armorPenetration);
    }

    /// <summary>
    /// Returns the percentage of positive physical damage absorbed by the numerical armor layer.
    /// This is primarily used to explain an armor rating to players and follows the same formula
    /// as <see cref="Apply"/>.
    /// </summary>
    public static float GetDamageReductionPercent(float armorRating, float armorPenetration = 0f)
    {
        var effectiveArmor = GetEffectiveArmor(armorRating, armorPenetration);
        if (effectiveArmor <= 0f)
            return 0f;

        return effectiveArmor / (ArmorScale + effectiveArmor) * 100f;
    }

    public static float GetPositivePhysicalDamage(DamageSpecifier damage)
    {
        var total = 0f;
        foreach (var (type, amount) in damage.DamageDict)
        {
            if (amount > FixedPoint2.Zero && IsPhysical(type))
                total += amount.Float();
        }

        return total;
    }

    private static bool IsPhysical(string damageType) => IsPhysicalDamageType(damageType);
}

public readonly record struct ArmorRatingResult(
    DamageSpecifier Damage,
    float EffectiveArmor,
    float DamageMultiplier,
    float AbsorbedDamage);
