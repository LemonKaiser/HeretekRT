using System.Linq;
using Content.Server._WH40K.Progression;
using Content.Shared._WH40K.ClassProgression;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Deterministic released-tree migrations. Stable ids survive topology-only revisions and only ids that no longer
/// exist are refunded by removing them from the purchased set before the audited database rewrite.
/// </summary>
public static class Wh40kClassTreeMigrationPolicy
{
    /// <summary>
    /// Compatibility overload for released callers that only need to retain or refund an id.
    /// </summary>
    public static Wh40kClassTreeMigrationResult Migrate(
        Wh40kAccountClassProgressRecord progress,
        Func<string, bool> isPersistentSkillIdValid)
    {
        ArgumentNullException.ThrowIfNull(isPersistentSkillIdValid);
        return Migrate(progress, id => isPersistentSkillIdValid(id) ? id : null);
    }

    public static Wh40kClassTreeMigrationResult Migrate(
        Wh40kAccountClassProgressRecord progress,
        Func<string, string?> resolvePersistentSkillId)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(resolvePersistentSkillId);
        if (progress.TreeVersion <= 0 || progress.TreeVersion > Wh40kClassProgressionConstants.TreeVersion)
            return new Wh40kClassTreeMigrationResult(progress, Array.Empty<string>(), false);
        if (progress.TreeVersion == Wh40kClassProgressionConstants.TreeVersion)
            return new Wh40kClassTreeMigrationResult(progress, Array.Empty<string>(), false);

        var resolved = progress.Skills
            .Select(skill => (Skill: skill, PersistentId: resolvePersistentSkillId(skill.SkillId)))
            .ToArray();
        var removed = resolved
            .Where(entry => entry.PersistentId == null)
            .Select(entry => entry.Skill.SkillId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var retained = resolved
            .Where(entry => entry.PersistentId != null)
            .GroupBy(entry => entry.PersistentId!, StringComparer.Ordinal)
            .Select(group => new Wh40kAccountClassSkillRecord(
                group.Key,
                group.Min(entry => entry.Skill.PurchasedAt)))
            .OrderBy(skill => skill.SkillId, StringComparer.Ordinal)
            .ToArray();
        var migrated = progress with
        {
            TreeVersion = Wh40kClassProgressionConstants.TreeVersion,
            Skills = retained,
        };
        return new Wh40kClassTreeMigrationResult(migrated, removed, true);
    }
}

public sealed record Wh40kClassTreeMigrationResult(
    Wh40kAccountClassProgressRecord Progress,
    IReadOnlyList<string> RemovedSkillIds,
    bool RequiresPersistence);
