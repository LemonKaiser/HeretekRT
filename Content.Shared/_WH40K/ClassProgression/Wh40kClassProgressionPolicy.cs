namespace Content.Shared._WH40K.ClassProgression;

/// <summary>
/// Pure progression rules shared by server validation and focused unit tests.
/// </summary>
public static class Wh40kClassProgressionPolicy
{
    public const int MinimumLevel = 1;
    public const int MaximumLevel = 100;
    public const int LevelsPerBaseSkillPoint = 5;

    public static int GetBaseSkillPoints(int level)
    {
        ValidateLevel(level);
        return level / LevelsPerBaseSkillPoint;
    }

    public static int GetMinimumLevelForOrder(int order)
    {
        return GetMinimumLevelForOrder(order, Wh40kClassProgressionConstants.LegacySkillsPerSpecialization);
    }

    public static int GetMinimumLevelForOrder(int order, int skillCount)
    {
        if (skillCount != Wh40kClassProgressionConstants.LegacySkillsPerSpecialization &&
            skillCount != Wh40kClassProgressionConstants.SkillsPerSpecialization)
        {
            throw new ArgumentOutOfRangeException(nameof(skillCount));
        }
        if (order < 1 || order > skillCount)
            throw new ArgumentOutOfRangeException(nameof(order));

        if (skillCount == Wh40kClassProgressionConstants.LegacySkillsPerSpecialization)
        {
            return order switch
            {
                <= 4 => 5,
                <= 8 => 25,
                <= 12 => 45,
                <= 16 => 65,
                <= 19 => 85,
                _ => 100,
            };
        }

        return order switch
        {
            <= 5 => 5,
            <= 10 => 25,
            <= 15 => 45,
            <= 20 => 65,
            <= 24 => 85,
            _ => 100,
        };
    }

    public static int GetAvailableSkillPoints(int level, int additionalPoints, int spentSkillPoints)
    {
        if (additionalPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(additionalPoints));
        if (spentSkillPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(spentSkillPoints));

        return checked(GetBaseSkillPoints(level) + additionalPoints - spentSkillPoints);
    }

    public static int GetSpentSkillPoints(
        IReadOnlySet<string> purchasedSkillIds,
        IReadOnlyDictionary<string, int> persistentSkillCosts)
    {
        if (persistentSkillCosts.Count == 0)
            return purchasedSkillIds.Count;

        var spent = 0;
        foreach (var skillId in purchasedSkillIds)
        {
            if (!persistentSkillCosts.TryGetValue(skillId, out var cost) || cost < 0)
                throw new ArgumentException($"Persistent class skill '{skillId}' has no valid cost.", nameof(persistentSkillCosts));
            spent = checked(spent + cost);
        }

        return spent;
    }

    private static void ValidateLevel(int level)
    {
        if (level is < MinimumLevel or > MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
    }
}

/// <summary>
/// Side-effect-free purchase decision used by both persistence and tests.
/// </summary>
public static class Wh40kClassPurchasePolicy
{
    public static Wh40kClassSkillPurchaseStatus Evaluate(
        string accountClassId,
        int level,
        long currentRevision,
        long expectedRevision,
        IReadOnlySet<string> purchasedSkillIds,
        Wh40kClassSkillPurchaseSpecData skill,
        int additionalSkillPoints,
        int? spentSkillPoints = null)
    {
        if (currentRevision != expectedRevision)
            return Wh40kClassSkillPurchaseStatus.RevisionMismatch;
        if (!string.Equals(accountClassId, skill.ClassId, StringComparison.Ordinal))
            return Wh40kClassSkillPurchaseStatus.ClassMismatch;
        if (skill.Availability != Wh40kClassContentAvailability.Enabled)
            return Wh40kClassSkillPurchaseStatus.ContentUnavailable;
        if (purchasedSkillIds.Contains(skill.SkillId))
            return Wh40kClassSkillPurchaseStatus.AlreadyPurchased;
        if (level < skill.MinimumLevel)
            return Wh40kClassSkillPurchaseStatus.InsufficientLevel;
        if (skill.PrerequisiteSkillId != null && !purchasedSkillIds.Contains(skill.PrerequisiteSkillId))
            return Wh40kClassSkillPurchaseStatus.MissingPrerequisite;
        if (Wh40kClassProgressionPolicy.GetAvailableSkillPoints(
                level,
                additionalSkillPoints,
                spentSkillPoints ?? purchasedSkillIds.Count) < skill.Cost)
        {
            return Wh40kClassSkillPurchaseStatus.InsufficientPoints;
        }

        return Wh40kClassSkillPurchaseStatus.Success;
    }
}

public readonly record struct Wh40kClassSkillPurchaseSpecData(
    string SkillId,
    string ClassId,
    string? PrerequisiteSkillId,
    int MinimumLevel,
    int Cost,
    Wh40kClassContentAvailability Availability);
