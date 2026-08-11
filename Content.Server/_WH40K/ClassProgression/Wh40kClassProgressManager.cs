using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.Progression;
using Content.Server.Database;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ClassProgression;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Server-authoritative account class tree facade. The client only receives its own class snapshot.
/// </summary>
public sealed class Wh40kClassProgressManager
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IWh40kAdditionalSkillPointSource _additionalPoints = default!;

    private readonly Dictionary<NetUserId, Wh40kAccountClassProgressRecord> _progress = new();

    public event Action<NetUserId, Wh40kAccountRpgRecord, Wh40kAccountClassProgressRecord>? ProgressChanged;

    public bool TryGetProgress(NetUserId userId, out Wh40kAccountClassProgressRecord progress)
    {
        return _progress.TryGetValue(userId, out progress!);
    }

    public void Remove(NetUserId userId)
    {
        _progress.Remove(userId);
    }

    public async Task<Wh40kClassTreeOperationResult> GetSnapshotAsync(
        NetUserId userId,
        CancellationToken cancel = default)
    {
        var account = await _db.GetWh40kAccountRpgAsync(userId, cancel);
        var progress = await _db.GetWh40kAccountClassProgressAsync(userId, cancel);
        if (account == null || progress == null)
        {
            return new Wh40kClassTreeOperationResult(
                Wh40kClassSkillPurchaseStatus.AccountNotFound,
                null,
                account,
                progress);
        }

        var migration = Wh40kClassTreeMigrationPolicy.Migrate(
            progress,
            ResolvePersistentSkillId);
        if (migration.RequiresPersistence)
        {
            var migrated = await _db.MutateWh40kClassProgressAsync(
                userId,
                new Wh40kClassAdminMutationRequest(
                    Guid.NewGuid(),
                    Wh40kClassAdminOperation.TreeMigration,
                    progress.Revision,
                    account.Foundation.ClassId,
                    migration.Progress.PurchasedSkillIds.Order(StringComparer.Ordinal).ToArray(),
                    Wh40kClassProgressionConstants.TreeVersion,
                    "system:class-tree-migration",
                    "class-tree-migration",
                    migration.RemovedSkillIds.Count == 0
                        ? $"tree-v{progress.TreeVersion}-v{Wh40kClassProgressionConstants.TreeVersion}"
                        : $"tree-v{progress.TreeVersion}-v{Wh40kClassProgressionConstants.TreeVersion};refunded={string.Join(',', migration.RemovedSkillIds)}"),
                cancel);
            account = migrated.Account;
            progress = migrated.ClassProgress;
            if (account == null || progress == null)
            {
                return new Wh40kClassTreeOperationResult(
                    migrated.Status,
                    null,
                    account,
                    progress);
            }
        }

        Cache(account, progress, false);

        return new Wh40kClassTreeOperationResult(
            Wh40kClassSkillPurchaseStatus.Success,
            BuildSnapshot(userId, account, progress),
            account,
            progress);
    }

    public async Task<Wh40kClassTreeOperationResult> PurchaseAsync(
        NetUserId userId,
        string skillId,
        long expectedRevision,
        CancellationToken cancel = default)
    {
        if (!_prototypes.TryIndex<Wh40kClassSkillPrototype>(skillId, out var skill) ||
            !_prototypes.TryIndex(skill.Specialization, out Wh40kClassSpecializationPrototype? specialization))
        {
            var missing = await GetSnapshotAsync(userId, cancel);
            return missing with { Status = Wh40kClassSkillPurchaseStatus.SkillNotFound };
        }

        var persistentSkill = ResolvePersistentSkill(skill);
        if (!_prototypes.TryIndex(persistentSkill.Specialization, out Wh40kClassSpecializationPrototype? persistentSpecialization) ||
            persistentSpecialization.Class != specialization.Class)
        {
            throw new InvalidOperationException($"Shared class skill '{skill.ID}' does not resolve inside its class.");
        }

        var result = await _db.PurchaseWh40kClassSkillAsync(
            userId,
            expectedRevision,
            new Wh40kClassSkillPurchaseSpec(
                persistentSkill.ID,
                specialization.Class.Id,
                ResolvePersistentSkillId(skill.Prerequisite?.Id ?? string.Empty),
                skill.MinimumLevel,
                persistentSkill.Cost,
                Wh40kClassProgressionConstants.TreeVersion,
                skill.Availability)
            {
                PersistentSkillCosts = BuildPersistentSkillCosts(specialization.Class),
            },
            _additionalPoints.GetAdditionalSkillPoints(userId),
            cancel);
        var snapshot = result.Account != null && result.ClassProgress != null
            ? BuildSnapshot(userId, result.Account, result.ClassProgress)
            : null;
        if (result.Account != null && result.ClassProgress != null)
            Cache(result.Account, result.ClassProgress, result.IsSuccess);
        return new Wh40kClassTreeOperationResult(
            result.Status,
            snapshot,
            result.Account,
            result.ClassProgress);
    }

    public async Task<Wh40kClassAdminMutationResult> MutateAsync(
        NetUserId userId,
        Wh40kClassAdminMutationRequest request,
        CancellationToken cancel = default)
    {
        var persistentSkillIds = new List<string>(request.NewSkillIds.Count);
        foreach (var skillId in request.NewSkillIds)
        {
            var persistentSkillId = ResolvePersistentSkillId(skillId);
            if (persistentSkillId == null)
                throw new ArgumentException($"Навык '{skillId}' не найден.", nameof(request));
            persistentSkillIds.Add(persistentSkillId);
        }
        request = request with { NewSkillIds = persistentSkillIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
        ValidateAdminMutation(request);
        var result = await _db.MutateWh40kClassProgressAsync(userId, request, cancel);
        if (result.Account != null && result.ClassProgress != null)
            Cache(result.Account, result.ClassProgress, result.IsSuccess);
        return result;
    }

    private void Cache(
        Wh40kAccountRpgRecord account,
        Wh40kAccountClassProgressRecord progress,
        bool notify)
    {
        var userId = account.Foundation.UserId;
        if (_progress.TryGetValue(userId, out var current) && current.Revision > progress.Revision)
            return;

        _progress[userId] = progress;
        if (notify)
            ProgressChanged?.Invoke(userId, account, progress);
    }

    public void ValidateAdminMutation(Wh40kClassAdminMutationRequest request)
    {
        if (!_prototypes.TryIndex<Wh40kCharacterClassPrototype>(request.NewClassId, out var accountClass))
            throw new ArgumentException($"Класс '{request.NewClassId}' не найден.", nameof(request));

        var allowedSpecializations = accountClass.Specializations
            .Select(id => id.Id)
            .ToHashSet(StringComparer.Ordinal);
        var requested = request.NewSkillIds.ToHashSet(StringComparer.Ordinal);
        foreach (var skillId in requested)
        {
            if (!_prototypes.TryIndex<Wh40kClassSkillPrototype>(skillId, out var skill))
                throw new ArgumentException($"Навык '{skillId}' не найден.", nameof(request));
            if (!allowedSpecializations.Contains(skill.Specialization.Id))
            {
                throw new ArgumentException(
                    $"Навык '{skillId}' не принадлежит классу '{request.NewClassId}'.",
                    nameof(request));
            }
            if (skill.Prerequisite is { } prerequisite && !requested.Contains(ResolvePersistentSkillId(prerequisite.Id)!))
            {
                throw new ArgumentException(
                    $"Для навыка '{skillId}' отсутствует prerequisite '{prerequisite.Id}'.",
                    nameof(request));
            }
        }
    }

    private Wh40kClassTreeSnapshot BuildSnapshot(
        NetUserId userId,
        Wh40kAccountRpgRecord account,
        Wh40kAccountClassProgressRecord progress)
    {
        if (!_prototypes.TryIndex<Wh40kCharacterClassPrototype>(account.Foundation.ClassId, out var accountClass))
            throw new InvalidOperationException($"WH40K account class '{account.Foundation.ClassId}' does not exist.");

        var purchased = progress.PurchasedSkillIds;
        var levelStart = Wh40kExperienceCurve.GetCumulativeExperienceTenths(account.Progress.Level);
        var levelSpan = Wh40kExperienceCurve.GetExperienceToNextLevelTenths(account.Progress.Level);
        var currentLevelExperience = Math.Max(0, account.Progress.ExperienceTenths - levelStart);
        var experienceToNext = levelSpan == 0
            ? 0
            : Math.Max(0, levelSpan - currentLevelExperience);
        var additionalPoints = _additionalPoints.GetAdditionalSkillPoints(userId);
        var earned = checked(Wh40kClassProgressionPolicy.GetBaseSkillPoints(account.Progress.Level) + additionalPoints);
        var available = earned - Wh40kClassProgressionPolicy.GetSpentSkillPoints(
            purchased,
            BuildPersistentSkillCosts(accountClass));
        var specializations = new List<Wh40kClassSpecializationSnapshot>();
        foreach (var specializationId in accountClass.Specializations)
        {
            var specialization = _prototypes.Index(specializationId);
            var nodes = _prototypes.EnumeratePrototypes<Wh40kClassSkillPrototype>()
                .Where(skill => skill.Specialization == specializationId)
                .OrderBy(skill => skill.Order)
                .Select(skill => new Wh40kClassSkillNodeSnapshot(
                    skill.ID,
                    GetNodeState(skill, account.Progress.Level, available, purchased, progress.TreeVersion)))
                .ToList();
            specializations.Add(new Wh40kClassSpecializationSnapshot(specialization.ID, nodes));
        }

        return new Wh40kClassTreeSnapshot(
            progress.Revision,
            progress.TreeVersion,
            account.Foundation.ClassId,
            account.Progress.Level,
            currentLevelExperience,
            experienceToNext,
            levelSpan,
            earned,
            available,
            purchased.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            specializations);
    }

    private Wh40kClassSkillNodeState GetNodeState(
        Wh40kClassSkillPrototype skill,
        int level,
        int availablePoints,
        IReadOnlySet<string> purchased,
        int treeVersion)
    {
        if (purchased.Contains(ResolvePersistentSkill(skill).ID))
            return Wh40kClassSkillNodeState.Purchased;
        if (treeVersion != Wh40kClassProgressionConstants.TreeVersion ||
            skill.Availability != Wh40kClassContentAvailability.Enabled)
        {
            return Wh40kClassSkillNodeState.ContentUnavailable;
        }
        if (level < skill.MinimumLevel)
            return Wh40kClassSkillNodeState.InsufficientLevel;
        if (skill.Prerequisite is { } prerequisite && !purchased.Contains(ResolvePersistentSkillId(prerequisite.Id)!))
            return Wh40kClassSkillNodeState.MissingPrerequisite;
        if (availablePoints < skill.Cost)
            return Wh40kClassSkillNodeState.InsufficientPoints;

        return Wh40kClassSkillNodeState.Available;
    }

    private string? ResolvePersistentSkillId(string skillId)
    {
        return !_prototypes.TryIndex<Wh40kClassSkillPrototype>(skillId, out var skill)
            ? null
            : ResolvePersistentSkill(skill).ID;
    }

    private Wh40kClassSkillPrototype ResolvePersistentSkill(Wh40kClassSkillPrototype skill)
    {
        if (skill.SharedPurchase is not { } sharedPurchase)
            return skill;

        return _prototypes.Index(sharedPurchase);
    }

    private IReadOnlyDictionary<string, int> BuildPersistentSkillCosts(
        ProtoId<Wh40kCharacterClassPrototype> classId)
    {
        var costs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var skill in _prototypes.EnumeratePrototypes<Wh40kClassSkillPrototype>())
        {
            if (!_prototypes.TryIndex(skill.Specialization, out Wh40kClassSpecializationPrototype? specialization) ||
                specialization.Class != classId)
            {
                continue;
            }

            var persistent = ResolvePersistentSkill(skill);
            if (!_prototypes.TryIndex(persistent.Specialization, out Wh40kClassSpecializationPrototype? persistentSpecialization) ||
                persistentSpecialization.Class != classId)
            {
                throw new InvalidOperationException($"Shared class skill '{skill.ID}' crosses class boundaries.");
            }

            if (persistent.Cost < 0)
                throw new InvalidOperationException($"Class skill '{persistent.ID}' has a negative cost.");

            if (!costs.TryAdd(persistent.ID, persistent.Cost) && costs[persistent.ID] != persistent.Cost)
                throw new InvalidOperationException($"Shared class skill '{persistent.ID}' has conflicting costs.");
        }

        return costs;
    }
}

public sealed record Wh40kClassTreeOperationResult(
    Wh40kClassSkillPurchaseStatus Status,
    Wh40kClassTreeSnapshot? Snapshot,
    Wh40kAccountRpgRecord? Account,
    Wh40kAccountClassProgressRecord? ClassProgress);

public sealed class Wh40kClassProgressNetworkSystem : EntitySystem
{
    [Dependency] private Wh40kClassProgressManager _progress = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<Wh40kClassTreeRequestEvent>(OnTreeRequest);
        SubscribeNetworkEvent<Wh40kClassSkillPurchaseRequestEvent>(OnPurchaseRequest);
    }

    private async void OnTreeRequest(Wh40kClassTreeRequestEvent message, EntitySessionEventArgs args)
    {
        var result = await _progress.GetSnapshotAsync(args.SenderSession.UserId);
        RaiseNetworkEvent(new Wh40kClassTreeSnapshotEvent(result.Status, result.Snapshot), args.SenderSession);
    }

    private async void OnPurchaseRequest(
        Wh40kClassSkillPurchaseRequestEvent message,
        EntitySessionEventArgs args)
    {
        var result = await _progress.PurchaseAsync(
            args.SenderSession.UserId,
            message.SkillId,
            message.ExpectedRevision);
        RaiseNetworkEvent(new Wh40kClassTreeSnapshotEvent(result.Status, result.Snapshot), args.SenderSession);
    }
}
