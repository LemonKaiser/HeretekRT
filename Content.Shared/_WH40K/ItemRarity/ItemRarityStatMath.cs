using Content.Shared.Damage;

namespace Content.Shared._WH40K.ItemRarity;

public static class ItemRarityStatMath
{
    public static DamageModifierSet CopyArmorModifiers(DamageModifierSet modifiers)
    {
        return new DamageModifierSet
        {
            Coefficients = new(modifiers.Coefficients),
            FlatReduction = new(modifiers.FlatReduction),
            IgnoreArmorPierceFlags = modifiers.IgnoreArmorPierceFlags,
        };
    }

    public static DamageModifierSet ScaleArmorModifiers(DamageModifierSet modifiers, float bonusPercent)
    {
        var result = CopyArmorModifiers(modifiers);
        var bonus = float.IsFinite(bonusPercent) ? Math.Clamp(bonusPercent, 0f, 50f) / 200f : 0f;
        foreach (var (type, coefficient) in modifiers.Coefficients)
        {
            if (coefficient is > 0f and < 1f)
                result.Coefficients[type] = MathF.Pow(coefficient, 1f + bonus);
        }

        foreach (var (type, reduction) in modifiers.FlatReduction)
        {
            if (reduction > 0f)
                result.FlatReduction[type] = reduction * (1f + bonus);
        }

        return result;
    }

    public static float BoostArmorPenetration(float penetration, float multiplier)
    {
        if (!float.IsFinite(penetration))
            return 0f;

        // Preserve ammunition penalties and explicitly configured full penetration.
        if (penetration <= 0f || penetration >= 1f)
            return penetration;

        var bonus = float.IsFinite(multiplier) ? Math.Clamp(multiplier, 1f, 1.25f) : 1f;
        return MathF.Min(MathF.Max(0.95f, penetration), penetration * bonus);
    }

    public static float CombineArmorPenetration(float ammunition, float weapon)
    {
        if (!float.IsFinite(ammunition))
            ammunition = 0f;
        if (!float.IsFinite(weapon))
            weapon = 0f;

        // Cap added bonuses without lowering a stronger value explicitly configured by the prototype.
        var limit = MathF.Max(0.95f, MathF.Max(ammunition, weapon));
        return ammunition >= 1f ? ammunition : MathF.Min(limit, ammunition + weapon);
    }
}
