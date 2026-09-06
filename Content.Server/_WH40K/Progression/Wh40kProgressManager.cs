using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.CharacterCreation;
using Content.Server.Database;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.Progression;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Owns the connected-account progression cache and the server-authoritative spend entry point.
/// </summary>
public sealed partial class Wh40kProgressManager
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;

    private readonly Dictionary<NetUserId, Wh40kAccountRpgRecord> _accounts = new();
    private readonly Dictionary<(NetUserId UserId, string RewardId), Wh40kExperienceLedgerRecord>
        _transientLedger = new();

    public event Action<NetUserId, Wh40kAccountRpgRecord>? ProgressChanged;

    public async Task<Wh40kAccountRpgRecord?> LoadAsync(
        NetUserId userId,
        CancellationToken cancel = default)
    {
        var account = await _db.GetWh40kAccountRpgAsync(userId, cancel);
        if (account != null)
            Cache(account, false);
        else
            _accounts.Remove(userId);

        return account;
    }

    public void Cache(Wh40kAccountRpgRecord account, bool notify = false)
    {
        var userId = account.Foundation.UserId;
        if (_accounts.TryGetValue(userId, out var current) &&
            current.Progress.Revision > account.Progress.Revision)
        {
            return;
        }

        _accounts[userId] = account;
        if (notify)
            ProgressChanged?.Invoke(userId, account);
    }

    public bool TryGetAccount(NetUserId userId, out Wh40kAccountRpgRecord account)
    {
        return _accounts.TryGetValue(userId, out account!);
    }

    public Wh40kExperienceAwardResult AwardTransient(
        NetUserId userId,
        Wh40kXpAwardRequest request)
    {
        if (!_accounts.TryGetValue(userId, out var account))
            throw new InvalidOperationException($"Transient WH40K RPG account {userId} is not loaded.");
        if (request.AmountTenths < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Transient WH40K XP cannot be negative.");

        var key = (userId, request.RewardId);
        if (_transientLedger.TryGetValue(key, out var duplicateLedger))
        {
            return new Wh40kExperienceAwardResult(
                Wh40kExperienceAwardStatus.Duplicate,
                account,
                duplicateLedger,
                account.Progress.Level,
                0,
                0);
        }

        var oldProgress = account.Progress;
        var experienceTenths = checked(oldProgress.ExperienceTenths + request.AmountTenths);
        var level = Wh40kExperienceCurve.GetLevel(experienceTenths);
        var levelsGained = level - oldProgress.Level;
        var developmentPointsAwarded = checked(
            levelsGained * Wh40kExperienceCurve.DevelopmentPointsPerLevel);
        var now = DateTime.UtcNow;
        var progress = oldProgress with
        {
            ExperienceTenths = experienceTenths,
            Level = level,
            UnspentDevelopmentPoints = checked(
                oldProgress.UnspentDevelopmentPoints + developmentPointsAwarded),
            UpdatedAt = request.AmountTenths > 0 ? now : oldProgress.UpdatedAt,
            Revision = request.AmountTenths > 0
                ? checked(oldProgress.Revision + 1)
                : oldProgress.Revision,
        };
        var updatedAccount = account with { Progress = progress };
        var ledger = new Wh40kExperienceLedgerRecord(
            0,
            userId,
            request.RewardId,
            request.SourceType.ToString().ToLowerInvariant(),
            request.AmountTenths,
            request.RoundId,
            request.IssuerEntity,
            request.ContextJson,
            now,
            Wh40kExperienceCurve.BalanceVersion);
        _transientLedger.Add(key, ledger);
        Cache(updatedAccount, request.AmountTenths > 0);

        return new Wh40kExperienceAwardResult(
            Wh40kExperienceAwardStatus.Awarded,
            updatedAccount,
            ledger,
            oldProgress.Level,
            levelsGained,
            developmentPointsAwarded);
    }

    public async Task<Wh40kCharacteristicSpendResult> SpendCharacteristicAsync(
        ICommonSession session,
        long expectedRevision,
        Wh40kCharacteristic characteristic,
        int count,
        CancellationToken cancel = default)
    {
        return await SpendCharacteristicsAsync(
            session,
            expectedRevision,
            [new Wh40kCharacteristicAllocation(characteristic, count)],
            cancel);
    }

    public async Task<Wh40kCharacteristicSpendResult> SpendCharacteristicsAsync(
        ICommonSession session,
        long expectedRevision,
        IReadOnlyList<Wh40kCharacteristicAllocation> allocations,
        CancellationToken cancel = default)
    {
        var result = await _db.SpendWh40kCharacteristicsAsync(
            session.UserId,
            expectedRevision,
            allocations,
            cancel);
        if (result.Account == null)
            return result;

        Cache(result.Account, result.IsSuccess);
        if (!result.IsSuccess ||
            session.AttachedEntity is not { Valid: true } mob ||
            !_entities.HasComponent<Wh40kCharacterStatsComponent>(mob))
        {
            return result;
        }

        _entities.System<Wh40kCharacterStatsSpawnSystem>().ApplyAccountStats(mob, result.Account);
        return result;
    }

    public async Task<Wh40kCharacteristicSpendResult> SpendCharacteristicAsync(
        NetUserId userId,
        long expectedRevision,
        Wh40kCharacteristic characteristic,
        int count,
        CancellationToken cancel = default)
    {
        return await SpendCharacteristicsAsync(
            userId,
            expectedRevision,
            [new Wh40kCharacteristicAllocation(characteristic, count)],
            cancel);
    }

    public async Task<Wh40kCharacteristicSpendResult> SpendCharacteristicsAsync(
        NetUserId userId,
        long expectedRevision,
        IReadOnlyList<Wh40kCharacteristicAllocation> allocations,
        CancellationToken cancel = default)
    {
        if (_players.TryGetSessionById(userId, out var session))
            return await SpendCharacteristicsAsync(session, expectedRevision, allocations, cancel);

        var result = await _db.SpendWh40kCharacteristicsAsync(
            userId,
            expectedRevision,
            allocations,
            cancel);
        if (result.Account != null)
            Cache(result.Account, result.IsSuccess);

        return result;
    }

    public void Remove(NetUserId userId)
    {
        _accounts.Remove(userId);
        foreach (var key in _transientLedger.Keys
                     .Where(key => key.UserId == userId)
                     .ToArray())
        {
            _transientLedger.Remove(key);
        }
    }
}
