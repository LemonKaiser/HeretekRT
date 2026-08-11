using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.Progression;
using Content.Server._WH40K.ClassProgression;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared._WH40K.Progression;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared.Database;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Administration;

/// <summary>
/// Approved additive account-level RPG administration with persistent and round admin audit trails.
/// </summary>
public sealed class Wh40kRpgAdminService
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private Wh40kExperienceService _experience = default!;
    [Dependency] private Wh40kProgressManager _progress = default!;
    [Dependency] private Wh40kAccountRpgManager _accountRpg = default!;
    [Dependency] private Wh40kClassProgressManager _classProgress = default!;

    public async Task<Wh40kExperienceAwardResult> GrantExperienceAsync(
        NetUserId target,
        string targetName,
        long experience,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        await RequireAccountAsync(target, cancel);
        var amountTenths = checked(experience * Wh40kExperienceCurve.ExperienceTenthsPerExperience);
        var request = CreateAuditRequest("experience", target, audit, amountTenths: amountTenths);
        var result = await _experience.AwardAsync(target, request, cancel);
        _adminLog.Add(
            LogType.Action,
            LogImpact.Medium,
            $"WH40K RPG admin {audit.AdminName} ({audit.AdminId}) granted {experience} XP to " +
            $"{targetName} ({target}). Reason: {audit.Reason}");
        return result;
    }

    public async Task<Wh40kExperienceAwardResult> GrantTargetLevelAsync(
        NetUserId target,
        string targetName,
        int targetLevel,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        if (targetLevel is <= Wh40kExperienceCurve.MinimumLevel or > Wh40kExperienceCurve.MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(targetLevel), "Целевой уровень должен быть от 2 до 100.");

        var account = await RequireAccountAsync(target, cancel);
        if (targetLevel <= account.Progress.Level)
        {
            throw new InvalidOperationException(
                $"Текущий уровень аккаунта {account.Progress.Level}; команда допускает только повышение.");
        }

        var targetExperience = Wh40kExperienceCurve.GetCumulativeExperienceTenths(targetLevel);
        var amountTenths = checked(targetExperience - account.Progress.ExperienceTenths);
        var request = CreateAuditRequest(
            "target-level",
            target,
            audit,
            amountTenths: amountTenths,
            targetLevel: targetLevel);
        var result = await _experience.AwardAsync(target, request, cancel);
        _adminLog.Add(
            LogType.Action,
            LogImpact.Medium,
            $"WH40K RPG admin {audit.AdminName} ({audit.AdminId}) raised {targetName} ({target}) " +
            $"to level {targetLevel}. Reason: {audit.Reason}");
        return result;
    }

    public async Task<Wh40kDevelopmentPointGrantResult> GrantDevelopmentPointsAsync(
        NetUserId target,
        string targetName,
        int amount,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        await RequireAccountAsync(target, cancel);
        var request = CreateAuditRequest("development-points", target, audit, points: amount);
        var result = await _db.GrantWh40kDevelopmentPointsAsync(target, amount, request, cancel);
        _progress.Cache(result.Account, result.IsAwarded);
        _adminLog.Add(
            LogType.Action,
            LogImpact.Medium,
            $"WH40K RPG admin {audit.AdminName} ({audit.AdminId}) granted {amount} development points " +
            $"to {targetName} ({target}). Reason: {audit.Reason}");
        return result;
    }

    public async Task<IReadOnlyList<Wh40kRewardDeliveryRecord>> CompensateCurrencyAsync(
        NetUserId target,
        string targetName,
        long amount,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        if (amount is <= 0 or > Wh40kLevelRewardCatalog.MaximumCurrencyDeliveryAmount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Сумма компенсации должна быть от 1 до 2147483647.");

        await RequireAccountAsync(target, cancel);
        var rewardId = CreateRewardId("currency");
        var context = CreateAuditContext("currency", target, audit, amount: amount);
        var result = await _db.EnqueueWh40kRewardDeliveriesAsync(
            target,
            [
                new Wh40kRewardDeliveryDraft(
                    rewardId,
                    "currency",
                    Wh40kLevelRewardCatalog.CurrencyRewardType,
                    null,
                    amount,
                    context),
            ],
            cancel);
        _adminLog.Add(
            LogType.AdminRefund,
            LogImpact.Medium,
            $"WH40K RPG admin {audit.AdminName} ({audit.AdminId}) queued {amount} throne gelt " +
            $"for {targetName} ({target}). Reason: {audit.Reason}");
        await _entities.System<Wh40kRewardDeliverySystem>().TryDeliverForUserAsync(target, cancel);
        return result;
    }

    public async Task<IReadOnlyList<Wh40kRewardDeliveryRecord>> CompensateItemAsync(
        NetUserId target,
        string targetName,
        EntProtoId prototype,
        int count,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        if (count is <= 0 or > Wh40kLevelRewardCatalog.MaximumItemDeliveryCount)
            throw new ArgumentOutOfRangeException(nameof(count), "Количество предметов должно быть от 1 до 100.");
        if (!_prototypes.TryIndex<EntityPrototype>(prototype, out _))
            throw new ArgumentException($"Прототип предмета '{prototype}' не найден.", nameof(prototype));

        await RequireAccountAsync(target, cancel);
        var rewardId = CreateRewardId("item");
        var context = CreateAuditContext("item", target, audit, prototype.Id, count);
        var result = await _db.EnqueueWh40kRewardDeliveriesAsync(
            target,
            [
                new Wh40kRewardDeliveryDraft(
                    rewardId,
                    "item:0",
                    Wh40kLevelRewardCatalog.ItemRewardType,
                    prototype.Id,
                    count,
                    context),
            ],
            cancel);
        _adminLog.Add(
            LogType.AdminRefund,
            LogImpact.Medium,
            $"WH40K RPG admin {audit.AdminName} ({audit.AdminId}) queued {count} x {prototype} " +
            $"for {targetName} ({target}). Reason: {audit.Reason}");
        await _entities.System<Wh40kRewardDeliverySystem>().TryDeliverForUserAsync(target, cancel);
        return result;
    }

    public async Task<Wh40kClassAdminMutationResult> SetClassAsync(
        NetUserId target,
        string targetName,
        string classId,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        var progress = await RequireClassProgressAsync(target, cancel);
        return await MutateClassAsync(
            target,
            targetName,
            Wh40kClassAdminOperation.SetClass,
            progress.Revision,
            classId,
            [],
            audit,
            cancel);
    }

    public async Task<Wh40kClassAdminMutationResult> ReplaceSkillsAsync(
        NetUserId target,
        string targetName,
        IReadOnlyList<string> skillIds,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        var account = await RequireAccountAsync(target, cancel);
        var progress = await RequireClassProgressAsync(target, cancel);
        return await MutateClassAsync(
            target,
            targetName,
            Wh40kClassAdminOperation.ReplaceSkills,
            progress.Revision,
            account.Foundation.ClassId,
            skillIds,
            audit,
            cancel);
    }

    public async Task<Wh40kClassAdminMutationResult> GrantSkillAsync(
        NetUserId target,
        string targetName,
        string skillId,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        var account = await RequireAccountAsync(target, cancel);
        var progress = await RequireClassProgressAsync(target, cancel);
        if (progress.PurchasedSkillIds.Contains(skillId))
            throw new InvalidOperationException($"Навык '{skillId}' уже выдан аккаунту.");

        var skills = progress.Skills.Select(skill => skill.SkillId).Append(skillId).ToArray();
        return await MutateClassAsync(
            target,
            targetName,
            Wh40kClassAdminOperation.GrantSkill,
            progress.Revision,
            account.Foundation.ClassId,
            skills,
            audit,
            cancel);
    }

    public async Task<Wh40kClassAdminMutationResult> RevokeSkillAsync(
        NetUserId target,
        string targetName,
        string skillId,
        Wh40kAdminAudit audit,
        CancellationToken cancel = default)
    {
        var account = await RequireAccountAsync(target, cancel);
        var progress = await RequireClassProgressAsync(target, cancel);
        if (!progress.PurchasedSkillIds.Contains(skillId))
            throw new InvalidOperationException($"Навык '{skillId}' не выдан аккаунту.");

        var revoked = new HashSet<string>(StringComparer.Ordinal) { skillId };
        bool changed;
        do
        {
            changed = false;
            foreach (var skill in _prototypes.EnumeratePrototypes<Wh40kClassSkillPrototype>())
            {
                if (skill.Prerequisite is not { } prerequisite ||
                    !revoked.Contains(prerequisite.Id) ||
                    !progress.PurchasedSkillIds.Contains(skill.ID))
                {
                    continue;
                }

                changed |= revoked.Add(skill.ID);
            }
        } while (changed);

        var skills = progress.Skills
            .Select(skill => skill.SkillId)
            .Where(id => !revoked.Contains(id))
            .ToArray();
        return await MutateClassAsync(
            target,
            targetName,
            Wh40kClassAdminOperation.RevokeSkill,
            progress.Revision,
            account.Foundation.ClassId,
            skills,
            audit,
            cancel);
    }

    private async Task<Wh40kClassAdminMutationResult> MutateClassAsync(
        NetUserId target,
        string targetName,
        Wh40kClassAdminOperation operation,
        long expectedRevision,
        string newClassId,
        IReadOnlyList<string> skillIds,
        Wh40kAdminAudit audit,
        CancellationToken cancel)
    {
        var request = new Wh40kClassAdminMutationRequest(
            Guid.NewGuid(),
            operation,
            expectedRevision,
            newClassId,
            skillIds,
            Wh40kClassProgressionConstants.TreeVersion,
            audit.AdminId,
            audit.AdminName,
            audit.Reason);
        var result = await _classProgress.MutateAsync(target, request, cancel);
        if (!result.IsSuccess || result.Account == null)
        {
            throw new InvalidOperationException(
                $"Изменение класса отклонено: {result.Status}; повторите команду с актуальным состоянием.");
        }

        _progress.Cache(result.Account, true);
        _accountRpg.CacheFoundation(result.Account.Foundation);
        _adminLog.Add(
            LogType.Action,
            LogImpact.High,
            $"WH40K class admin {audit.AdminName} ({audit.AdminId}) performed {operation} for " +
            $"{targetName} ({target}); class={newClassId}; skills={skillIds.Count}. Reason: {audit.Reason}");
        return result;
    }

    private async Task<Wh40kAccountRpgRecord> RequireAccountAsync(NetUserId target, CancellationToken cancel)
    {
        return await _db.GetWh40kAccountRpgAsync(target, cancel)
               ?? throw new InvalidOperationException($"У аккаунта {target} нет фундамента WH40K RPG.");
    }

    private async Task<Wh40kAccountClassProgressRecord> RequireClassProgressAsync(
        NetUserId target,
        CancellationToken cancel)
    {
        return await _db.GetWh40kAccountClassProgressAsync(target, cancel)
               ?? throw new InvalidOperationException($"У аккаунта {target} нет постоянного прогресса класса.");
    }

    private static Wh40kXpAwardRequest CreateAuditRequest(
        string operation,
        NetUserId target,
        Wh40kAdminAudit audit,
        long amountTenths = 0,
        int? targetLevel = null,
        int? points = null)
    {
        return new Wh40kXpAwardRequest(
            CreateRewardId(operation),
            Wh40kExperienceSourceType.Admin,
            amountTenths,
            IssuerEntity: audit.AdminId,
            ContextJson: CreateAuditContext(operation, target, audit, targetLevel: targetLevel, points: points));
    }

    private static string CreateAuditContext(
        string operation,
        NetUserId target,
        Wh40kAdminAudit audit,
        string? prototype = null,
        long? amount = null,
        int? targetLevel = null,
        int? points = null)
    {
        return JsonSerializer.Serialize(new
        {
            operation,
            adminId = audit.AdminId,
            adminName = audit.AdminName,
            reason = audit.Reason,
            target = target.UserId,
            prototype,
            amount,
            targetLevel,
            points,
        });
    }

    private static string CreateRewardId(string operation)
    {
        return $"admin:{operation}:{Guid.NewGuid():N}";
    }
}

public sealed record Wh40kAdminAudit(string AdminId, string AdminName, string Reason);
