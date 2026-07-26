using System.Collections.ObjectModel;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Versioned server-owned balance data for the first WH40K RPG release.
/// Values are explicit production data; the design formula is not evaluated at runtime.
/// </summary>
public static class Wh40kExperienceCurve
{
    public const int BalanceVersion = 1;
    public const int ProgressSchemaVersion = 1;
    public const int MinimumLevel = 1;
    public const int MaximumLevel = 100;
    public const int DevelopmentPointsPerLevel = 3;
    public const int ExperienceTenthsPerExperience = 10;
    public const int TotalExperience = 500_000;
    public const long TotalExperienceTenths = TotalExperience * ExperienceTenthsPerExperience;

    public const int DifficultyMultiplierScale = 1000;
    public const int StandardDifficultyMultiplier = DifficultyMultiplierScale;

    private static readonly int[] CumulativeExperienceValues =
    {
        0, 750, 1500, 2260, 3020, 3790, 4570, 5370, 6190, 7030,
        7890, 8770, 9680, 10620, 11600, 12610, 13660, 14750, 15890, 17070,
        18300, 19580, 20920, 22320, 23780, 25300, 26890, 28540, 30260, 32060,
        33930, 35880, 37910, 40030, 42240, 44540, 46930, 49410, 51990, 54670,
        57450, 60340, 63340, 66450, 69670, 73010, 76470, 80050, 83750, 87580,
        91540, 95630, 99860, 104220, 108720, 113370, 118160, 123100, 128190, 133440,
        138840, 144400, 150120, 156010, 162060, 168280, 174680, 181250, 188000, 194930,
        202040, 209340, 216830, 224510, 232380, 240450, 248720, 257190, 265860, 274740,
        283830, 293130, 302650, 312390, 322350, 332530, 342940, 353570, 364440, 375540,
        386880, 398460, 410280, 422340, 434650, 447210, 460020, 473090, 486420, 500000,
    };

    private static readonly int[] SupportedDifficultyMultiplierValues =
    {
        1000,
        1250,
        1500,
        1750,
    };

    public static ReadOnlyCollection<int> CumulativeExperience { get; } =
        Array.AsReadOnly(CumulativeExperienceValues);

    public static ReadOnlyCollection<int> SupportedDifficultyMultipliers { get; } =
        Array.AsReadOnly(SupportedDifficultyMultiplierValues);

    public static long GetCumulativeExperienceTenths(int level)
    {
        ValidateLevel(level);
        return (long) CumulativeExperienceValues[level - 1] * ExperienceTenthsPerExperience;
    }

    public static long GetExperienceToNextLevelTenths(int level)
    {
        ValidateLevel(level);
        if (level == MaximumLevel)
            return 0;

        return (long) (CumulativeExperienceValues[level] - CumulativeExperienceValues[level - 1])
               * ExperienceTenthsPerExperience;
    }

    public static int GetLevel(long experienceTenths)
    {
        if (experienceTenths < 0)
            throw new ArgumentOutOfRangeException(nameof(experienceTenths));

        if (experienceTenths >= TotalExperienceTenths)
            return MaximumLevel;

        var experience = experienceTenths / ExperienceTenthsPerExperience;
        var index = Array.BinarySearch(CumulativeExperienceValues, (int) experience);
        if (index >= 0)
            return index + 1;

        var insertionIndex = ~index;
        return insertionIndex;
    }

    public static bool IsSupportedDifficultyMultiplier(int multiplier)
    {
        return Array.BinarySearch(SupportedDifficultyMultiplierValues, multiplier) >= 0;
    }

    private static void ValidateLevel(int level)
    {
        if (level is < MinimumLevel or > MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
    }
}
