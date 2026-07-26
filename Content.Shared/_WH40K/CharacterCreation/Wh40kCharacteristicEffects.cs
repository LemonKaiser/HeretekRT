namespace Content.Shared._WH40K.CharacterCreation;

/// <summary>
/// Shared characteristic formulas used by both runtime systems and player-facing previews.
/// Characteristic values remain uncapped; only their individual gameplay effects are bounded.
/// </summary>
public static class Wh40kCharacteristicEffects
{
    public const int MaximumPhantomStepCharges = 5;

    public static float GetDamageMultiplier(int points)
    {
        return Math.Clamp(1f + points * 0.01f, 0.25f, 2f);
    }

    public static float GetMeleeCooldownMultiplier(int melee)
    {
        return Math.Clamp(1f - melee * 0.005f, 0.5f, 1.5f);
    }

    public static float GetRangedPrecisionMultiplier(int ranged)
    {
        return Math.Clamp(1f - ranged * 0.005f, 0.5f, 1.5f);
    }

    public static int GetEnduranceEffect(int endurance)
    {
        return Math.Clamp(endurance, -100, 100);
    }

    public static float GetDoAfterSpeedMultiplier(int intelligence)
    {
        return Math.Clamp(1f + intelligence * 0.01f, 0.5f, 2f);
    }

    public static float GetMovementSpeedMultiplier(int agility)
    {
        return 1f + Math.Clamp(agility, -25, 25) * 0.01f;
    }

    public static int GetPhantomStepCharges(int agility)
    {
        return Math.Clamp(
            Math.Max(0, agility) / 25,
            0,
            MaximumPhantomStepCharges);
    }
}
