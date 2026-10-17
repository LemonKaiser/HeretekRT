using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.Quests;
using Content.Shared._WH40K.Quests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Content.Server.Database;

public abstract partial class ServerDbBase : IWh40kQuestRepository
{
    public async Task RecoverWh40kQuestsAsync(Wh40kQuestOwnerKey owner, DateTime now, Guid generation, CancellationToken cancel = default)
    {
        QuestUtc(now);
        if (generation == Guid.Empty) throw new ArgumentException("Missing round/server generation.");
        var key = QuestOwnerKey(owner);
        await QuestWriteAsync(owner, async db =>
        {
            await ExpireQuestRows(db, key, now, cancel);
            var rows = await db.Wh40kQuestInstances.Where(x => x.OwnerKey == key && x.State <= 1).Take(32).ToListAsync(cancel);
            foreach (var row in rows)
            {
                if (!Wh40kQuestSnapshotCodec.TryDeserialize(row.SnapshotJson, out var snapshot)) continue;
                if (row.LastGeneration is {} previous && previous != generation && snapshot!.RoundPolicy == Wh40kQuestRoundPolicy.Fail)
                {
                    row.State = (int) Wh40kQuestState.Failed; row.ClosedAt = now; row.Revision++;
                    db.Wh40kQuestAudits.Add(new() { OwnerKey = key, QuestId = row.Id, Action = "RoundChange", Reason = "Authored Fail policy.", At = now });
                }
                row.LastGeneration = generation;
            }
            return true;
        }, cancel);
    }
    private static string QuestOwnerKey(Wh40kQuestOwnerKey owner)
    {
        if (!owner.IsValid) throw new ArgumentException("Invalid quest owner.");
        return $"{(int) owner.Scope}:{owner.Id:N}";
    }

    private static void QuestUtc(DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("Quest timestamps must be UTC.");
    }

    // Updating the owner row acquires a database write lock before reading capacity/history.
    // The concurrency token and bounded retry also cover independent repositories/server processes.
    private async Task<T> QuestWriteAsync<T>(Wh40kQuestOwnerKey owner,
        Func<ServerDbContext, Task<T>> action, CancellationToken cancel)
    {
        var key = QuestOwnerKey(owner);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var guard = await GetDb(cancel);
                var db = guard.DbContext;
                await using var transaction = await db.Database.BeginTransactionAsync(cancel);
                var row = await db.Wh40kQuestOwners.SingleOrDefaultAsync(x => x.Key == key, cancel);
                if (row == null) db.Wh40kQuestOwners.Add(new() { Key = key, Revision = 1 });
                else row.Revision++;
                await db.SaveChangesAsync(cancel);
                var result = await action(db);
                await db.SaveChangesAsync(cancel);
                await transaction.CommitAsync(cancel);
                return result;
            }
            catch (Exception error) when (attempt < 7 && QuestRetryable(error))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), cancel);
            }
        }
    }

    private static bool QuestRetryable(Exception error)
    {
        if (error is DbUpdateConcurrencyException) return true;
        if (error is DbUpdateException && error.InnerException is {} inner) error = inner;
        return error is SqliteException { SqliteErrorCode: 5 or 6 } or
            SqliteException { SqliteExtendedErrorCode: 1555 or 2067 } or
            PostgresException { SqlState: "23505" or "40001" or "40P01" };
    }

    public Task<bool> PutWh40kQuestOfferAsync(Wh40kQuestOfferDraft offer, CancellationToken cancel = default)
    {
        var json = Wh40kQuestSnapshotCodec.Serialize(offer.Terms);
        QuestUtc(offer.ExpiresAt);
        // PostgreSQL stores microseconds. Freeze offer expiry at the same precision on both providers.
        var expiry = new DateTime(offer.ExpiresAt.Ticks - offer.ExpiresAt.Ticks % 10, DateTimeKind.Utc);
        if (offer.Id == Guid.Empty || offer.ExpiresAt <= offer.Terms.AcceptedAt.UtcDateTime || offer.ActiveLimit is < 1 or > 5)
            throw new ArgumentException("Invalid offer identity, expiry or active limit.");
        return QuestWriteAsync(offer.Terms.Owner, async db =>
        {
            var existing = await db.Wh40kQuestOffers.FindAsync([offer.Id], cancel);
            if (existing != null) return existing.OwnerKey == QuestOwnerKey(offer.Terms.Owner) &&
                existing.TermsJson == json && existing.ExpiresAt == expiry && existing.ActiveLimit == offer.ActiveLimit;
            var key = QuestOwnerKey(offer.Terms.Owner);
            if (await db.Wh40kQuestOffers.CountAsync(x => x.OwnerKey == key && x.State == 0 && x.ExpiresAt > DateTime.UtcNow, cancel) >= 32)
                return false;
            db.Wh40kQuestOffers.Add(new()
            {
                Id = offer.Id, OwnerKey = key, DefinitionId = offer.Terms.DefinitionId, TermsJson = json,
                CreatedAt = offer.Terms.AcceptedAt.UtcDateTime, ExpiresAt = expiry, ActiveLimit = offer.ActiveLimit,
            });
            return true;
        }, cancel);
    }

    public Task<Wh40kQuestResult> MutateWh40kQuestAsync(Wh40kQuestOwnerKey owner, Guid actor,
        Wh40kQuestRequest request, DateTime now, bool admin = false, CancellationToken cancel = default)
    {
        QuestUtc(now);
        if (request.OperationId == Guid.Empty || request.TargetId == Guid.Empty || request.Reason.Length > 512 || !Enum.IsDefined(request.Kind))
            throw new ArgumentException("Invalid operation identity or reason.");
        if (!admin && owner.Scope == Wh40kQuestOwnerScope.Account && actor != owner.Id || actor == Guid.Empty || request.Kind == Wh40kQuestMutation.AdminFail && !admin)
            return Task.FromResult(new Wh40kQuestResult(Wh40kQuestOperationResult.NotOwner, null, 0));
        var key = QuestOwnerKey(owner);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { actor, request, admin }))));
        return QuestWriteAsync(owner, async db =>
        {
            if (!admin && !await QuestActorAllowed(db, owner, actor,
                    request.Kind is Wh40kQuestMutation.Accept or Wh40kQuestMutation.Abandon, now, cancel))
                return new Wh40kQuestResult(Wh40kQuestOperationResult.NotOwner, null, 0);
            if (!admin && owner.Scope == Wh40kQuestOwnerScope.Party)
            {
                // Acquire the membership service's row lock without changing its public revision.
                await db.Wh40kParties.Where(x => x.Id == owner.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.Revision, x => x.Revision), cancel);
                if (!await QuestActorAllowed(db, owner, actor,
                        request.Kind is Wh40kQuestMutation.Accept or Wh40kQuestMutation.Abandon, now, cancel))
                    return new Wh40kQuestResult(Wh40kQuestOperationResult.NotOwner, null, 0);
            }
            var existing = await db.Wh40kQuestOperations.FindAsync([key, request.OperationId], cancel);
            if (existing != null)
                return existing.RequestHash == hash ? new((Wh40kQuestOperationResult) existing.Result, existing.QuestId, existing.Revision) :
                    new Wh40kQuestResult(Wh40kQuestOperationResult.InvalidContent, null, 0);
            await ExpireQuestRows(db, key, now, cancel);
            var result = request.Kind == Wh40kQuestMutation.Accept
                ? await AcceptQuest(db, owner, request.TargetId, now, cancel)
                : await ChangeQuest(db, owner, actor, request, now, cancel);
            db.Wh40kQuestOperations.Add(new()
            {
                OwnerKey = key, OperationId = request.OperationId, RequestHash = hash, Result = (int) result.Result,
                QuestId = result.QuestId, Revision = result.Revision, CreatedAt = now,
            });
            db.Wh40kQuestAudits.Add(new()
            {
                OwnerKey = key, QuestId = result.QuestId, Actor = actor, Action = request.Kind.ToString(),
                Reason = ($"{result.Result}: {request.Reason}")[..Math.Min(512, result.Result.ToString().Length + 2 + request.Reason.Length)], At = now,
            });
            return result;
        }, cancel);
    }

    private static async Task<Wh40kQuestResult> AcceptQuest(ServerDbContext db, Wh40kQuestOwnerKey owner,
        Guid offerId, DateTime now, CancellationToken cancel)
    {
        var key = QuestOwnerKey(owner);
        var offer = await db.Wh40kQuestOffers.SingleOrDefaultAsync(x => x.Id == offerId && x.OwnerKey == key, cancel);
        if (offer == null) return new(Wh40kQuestOperationResult.NotOwner, null, 0);
        var accepted = await db.Wh40kQuestInstances.SingleOrDefaultAsync(x => x.OfferId == offerId, cancel);
        if (accepted != null) return new(Wh40kQuestOperationResult.Succeeded, accepted.Id, accepted.Revision);
        if (offer.ExpiresAt <= now)
        {
            offer.State = (int) Wh40kQuestOfferState.Expired;
            return new(Wh40kQuestOperationResult.Expired, null, 0);
        }
        if (offer.State != (int) Wh40kQuestOfferState.Open ||
            !Wh40kQuestSnapshotCodec.TryDeserialize(offer.TermsJson, out var terms) || terms!.Owner != owner)
            return new(Wh40kQuestOperationResult.InvalidContent, null, 0);
        var eligibility = await QuestEligibility(db, terms, offer.ActiveLimit, now, cancel);
        if (eligibility != Wh40kQuestOperationResult.Succeeded) return new(eligibility, null, 0);
        var snapshot = terms with
        {
            AcceptedAt = new DateTimeOffset(now),
            Deadline = terms.Deadline is {} deadline ? new DateTimeOffset(now) + (deadline - terms.AcceptedAt) : null,
        };
        if (owner.Scope == Wh40kQuestOwnerScope.Party)
        {
            var recipients = await db.Wh40kPartyMembers.Where(x => x.PartyId == owner.Id).Select(x => x.UserId).OrderBy(x => x).ToListAsync(cancel);
            if (recipients.Count is <= 0 or > 5) return new(Wh40kQuestOperationResult.InvalidContent, null, 0);
            snapshot = snapshot with { Recipients = [..recipients] };
        }
        var quest = new Wh40kQuestInstance
        {
            Id = Guid.NewGuid(), OfferId = offer.Id, OwnerKey = key, DefinitionId = terms.DefinitionId,
            SnapshotJson = Wh40kQuestSnapshotCodec.Serialize(snapshot), AcceptedAt = now, Deadline = snapshot.Deadline?.UtcDateTime,
            Revision = 1,
        };
        db.Wh40kQuestInstances.Add(quest);
        for (var i = 0; i < snapshot.Objectives.Length; i++)
        {
            var objective = snapshot.Objectives[i];
            db.Wh40kQuestObjectives.Add(new() { QuestId = quest.Id, ObjectiveId = objective.Id, Ordinal = i, RequiredCount = objective.RequiredCount });
        }
        offer.State = (int) Wh40kQuestOfferState.Accepted;
        return new(Wh40kQuestOperationResult.Succeeded, quest.Id, quest.Revision);
    }

    public async Task<IReadOnlySet<string>> GetEligibleWh40kQuestDefinitionsAsync(IReadOnlyList<Wh40kQuestContractSnapshot> candidates,
        int activeLimit, DateTime now, CancellationToken cancel = default)
    {
        QuestUtc(now);
        if (candidates.Count > 128 || activeLimit is < 1 or > 5 || candidates.Select(x => x.Owner).Distinct().Count() > 1)
            throw new ArgumentException("Invalid eligibility batch.");
        await using var guard = await GetDb(cancel);
        var result = new HashSet<string>();
        foreach (var terms in candidates)
            if (await QuestEligibility(guard.DbContext, terms, activeLimit, now, cancel) == Wh40kQuestOperationResult.Succeeded)
                result.Add(terms.DefinitionId);
        return result;
    }

    public async Task<IReadOnlySet<string>> GetCompletedWh40kQuestDefinitionsAsync(Wh40kQuestOwnerKey owner,
        IReadOnlyList<string> definitions, CancellationToken cancel = default)
    {
        if (definitions.Count > 128) throw new ArgumentException("Unbounded completion query.");
        var key = QuestOwnerKey(owner);
        await using var guard = await GetDb(cancel);
        return (await guard.DbContext.Wh40kQuestInstances.Where(x => x.OwnerKey == key && x.State == 2 && definitions.Contains(x.DefinitionId))
            .Select(x => x.DefinitionId).Distinct().ToListAsync(cancel)).ToHashSet();
    }

    private static async Task<Wh40kQuestOperationResult> QuestEligibility(ServerDbContext db,
        Wh40kQuestContractSnapshot terms, int activeLimit, DateTime now, CancellationToken cancel)
    {
        var key = QuestOwnerKey(terms.Owner);
        if (terms.BranchGroup != null && await db.Wh40kQuestAccountStates.AnyAsync(x => x.UserId == terms.Owner.Id &&
                x.Key == "branch:" + terms.BranchGroup && x.Value != terms.BranchChoice, cancel))
            return Wh40kQuestOperationResult.PrerequisiteMissing;
        if (await db.Wh40kQuestInstances.CountAsync(x => x.OwnerKey == key && x.State <= 1 && (x.Deadline == null || x.Deadline > now), cancel) >= activeLimit)
            return Wh40kQuestOperationResult.CapacityReached;
        foreach (var prerequisite in terms.Prerequisites)
            if (!await db.Wh40kQuestInstances.AnyAsync(x => x.OwnerKey == key && x.DefinitionId == prerequisite && x.State == 2, cancel))
                return Wh40kQuestOperationResult.PrerequisiteMissing;
        if (await db.Wh40kQuestInstances.AnyAsync(x => x.OwnerKey == key && x.DefinitionId == terms.DefinitionId && x.State <= 1 && (x.Deadline == null || x.Deadline > now), cancel))
            return Wh40kQuestOperationResult.CapacityReached;
        var previous = await db.Wh40kQuestInstances.Where(x => x.OwnerKey == key && x.DefinitionId == terms.DefinitionId && x.State == 2)
            .OrderByDescending(x => x.ClosedAt).FirstOrDefaultAsync(cancel);
        if (previous != null && (terms.Repeat == Wh40kQuestRepeatPolicy.Once || previous.ClosedAt + terms.Cooldown > now))
            return Wh40kQuestOperationResult.PrerequisiteMissing;
        return Wh40kQuestOperationResult.Succeeded;
    }

    private static async Task<Wh40kQuestResult> ChangeQuest(ServerDbContext db, Wh40kQuestOwnerKey owner,
        Guid actor, Wh40kQuestRequest request, DateTime now, CancellationToken cancel)
    {
        var key = QuestOwnerKey(owner);
        var quest = await db.Wh40kQuestInstances.SingleOrDefaultAsync(x => x.OwnerKey == key && x.Id == request.TargetId, cancel);
        if (quest == null) return new(Wh40kQuestOperationResult.NotOwner, null, 0);
        if (quest.State == (int) Wh40kQuestState.Expired) return new(Wh40kQuestOperationResult.Expired, quest.Id, quest.Revision);
        if (quest.Revision != request.ExpectedRevision) return new(Wh40kQuestOperationResult.StaleRevision, quest.Id, quest.Revision);
        if (quest.State > 1) return new(quest.State == 5 ? Wh40kQuestOperationResult.Expired : Wh40kQuestOperationResult.NotReady, quest.Id, quest.Revision);
        if (request.Kind == Wh40kQuestMutation.Expire)
        {
            if (quest.Deadline == null || quest.Deadline > now) return new(Wh40kQuestOperationResult.NotReady, quest.Id, quest.Revision);
            quest.State = (int) Wh40kQuestState.Expired;
        }
        else if (request.Kind == Wh40kQuestMutation.Complete)
        {
            if (!Wh40kQuestSnapshotCodec.TryDeserialize(quest.SnapshotJson, out var snapshot) || snapshot!.Owner != owner ||
                snapshot.DefinitionId != quest.DefinitionId)
                return new(Wh40kQuestOperationResult.InvalidContent, quest.Id, quest.Revision);
            var objectives = await db.Wh40kQuestObjectives.Where(x => x.QuestId == quest.Id).ToListAsync(cancel);
            if (objectives.Count != snapshot!.Objectives.Length || objectives.Any(x => x.Count != x.RequiredCount))
                return new(Wh40kQuestOperationResult.NotReady, quest.Id, quest.Revision);
            if (!snapshot.Recipients.Contains(actor) || !await CommitQuestBranch(db, snapshot, cancel))
                return new(Wh40kQuestOperationResult.NotOwner, quest.Id, quest.Revision);
            await CompleteQuestAsync(db, quest, snapshot, now, cancel);
            return new(Wh40kQuestOperationResult.Succeeded, quest.Id, quest.Revision);
        }
        else quest.State = (int) (request.Kind == Wh40kQuestMutation.AdminFail ? Wh40kQuestState.Failed : Wh40kQuestState.Abandoned);
        quest.ClosedAt = now;
        quest.Revision++;
        return new(Wh40kQuestOperationResult.Succeeded, quest.Id, quest.Revision);
    }

    private static async Task CompleteQuestAsync(ServerDbContext db, Wh40kQuestInstance quest,
        Wh40kQuestContractSnapshot snapshot, DateTime now, CancellationToken cancel)
    {
        // Party contracts freeze recipients on acceptance. A former character must not earn rewards after a wipe.
        var resetRecipients = await db.Wh40kPlayerProgresses
            .Where(progress => snapshot.Recipients.Contains(progress.UserId) && progress.CreatedAt > snapshot.AcceptedAt.UtcDateTime)
            .Select(progress => progress.UserId).ToListAsync(cancel);
        quest.State = (int) Wh40kQuestState.Completed;
        quest.ClosedAt = now;
        quest.Revision++;
        foreach (var reward in snapshot.Rewards)
        foreach (var recipient in snapshot.Recipients.OrderBy(x => x))
        {
            if (resetRecipients.Contains(recipient)) continue;
            var amount = snapshot.RewardAmount(reward, recipient);
            if (amount == 0) continue;
            db.Wh40kQuestRewardIntents.Add(new()
            {
                QuestId = quest.Id, EntryId = reward.EntryId, Recipient = recipient,
                RewardId = $"quest:{quest.Id:N}:completion:{reward.EntryId}", PayloadJson = JsonSerializer.Serialize(reward with { Amount = amount }), CreatedAt = now,
            });
        }
    }

    private static async Task ExpireQuestRows(ServerDbContext db, string key, DateTime now, CancellationToken cancel)
    {
        var rows = await db.Wh40kQuestInstances.Where(x => x.OwnerKey == key && (x.State == 0 || x.State == 1) && x.Deadline <= now).ToListAsync(cancel);
        foreach (var quest in rows)
        {
            quest.State = (int) Wh40kQuestState.Expired; quest.ClosedAt = now; quest.Revision++;
            db.Wh40kQuestAudits.Add(new() { OwnerKey = key, QuestId = quest.Id, Action = "Expire", Reason = "UTC deadline reached.", At = now });
        }
        // Make expiration visible to subsequent SQL capacity queries in this transaction.
        await db.SaveChangesAsync(cancel);
    }

    public async Task<IReadOnlyList<Wh40kQuestInstance>> GetWh40kQuestsAsync(Wh40kQuestOwnerKey owner,
        bool history = false, int limit = 32, CancellationToken cancel = default)
    {
        var key = QuestOwnerKey(owner);
        await using var guard = await GetDb(cancel);
        var query = guard.DbContext.Wh40kQuestInstances.AsNoTracking().Where(x => x.OwnerKey == key && (history ? x.State > 1 : x.State <= 1));
        return await (history ? query.OrderByDescending(x => x.ClosedAt).ThenByDescending(x => x.AcceptedAt).ThenBy(x => x.Id) :
            query.OrderByDescending(x => x.AcceptedAt).ThenBy(x => x.Id)).Take(Math.Clamp(limit, 1, 128)).ToListAsync(cancel);
    }

    public async Task<IReadOnlyList<Wh40kQuestOffer>> GetWh40kQuestOffersAsync(Wh40kQuestOwnerKey owner, DateTime now,
        int limit = 32, CancellationToken cancel = default)
    {
        QuestUtc(now); var key = QuestOwnerKey(owner);
        await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestOffers.AsNoTracking().Where(x => x.OwnerKey == key && x.State == 0 && x.ExpiresAt > now)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(Math.Clamp(limit, 1, 32)).ToListAsync(cancel);
    }

    public async Task<IReadOnlyList<Wh40kQuestObjective>> GetWh40kQuestObjectivesAsync(Wh40kQuestOwnerKey owner,
        Guid questId, CancellationToken cancel = default)
    {
        var key = QuestOwnerKey(owner);
        await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestObjectives.AsNoTracking().Where(x => x.QuestId == questId &&
            guard.DbContext.Wh40kQuestInstances.Any(q => q.Id == x.QuestId && q.OwnerKey == key)).OrderBy(x => x.Ordinal).Take(32).ToListAsync(cancel);
    }

    public async Task<Wh40kQuestResult?> GetWh40kQuestOperationAsync(Wh40kQuestOwnerKey owner, Guid operationId, CancellationToken cancel = default)
    {
        await using var guard = await GetDb(cancel);
        var row = await guard.DbContext.Wh40kQuestOperations.FindAsync([QuestOwnerKey(owner), operationId], cancel);
        return row == null ? null : new((Wh40kQuestOperationResult) row.Result, row.QuestId, row.Revision);
    }

    public Task<bool> EnqueueWh40kQuestFactAsync(Wh40kQuestOwnerKey owner, Guid questId, Wh40kQuestFact fact, CancellationToken cancel = default)
    {
        QuestUtc(fact.OccurredAt);
        if (questId == Guid.Empty || fact.EventId == Guid.Empty || string.IsNullOrWhiteSpace(fact.ObjectiveId) || fact.ObjectiveId.Length > 64 ||
            fact.Amount is < 0 or > 10000 || !fact.SetCount && fact.Amount == 0 || fact.TargetToken?.Length > 128 ||
            fact.Activity is {} activity && !activity.IsValid) throw new ArgumentException("Invalid authoritative quest fact.");
        var key = QuestOwnerKey(owner);
        var json = JsonSerializer.Serialize(fact);
        return QuestWriteAsync(owner, async db =>
        {
            if (!await db.Wh40kQuestInstances.AnyAsync(x => x.Id == questId && x.OwnerKey == key, cancel) ||
                !await db.Wh40kQuestObjectives.AnyAsync(x => x.QuestId == questId && x.ObjectiveId == fact.ObjectiveId, cancel)) return false;
            if (owner.Scope == Wh40kQuestOwnerScope.Party)
            {
                var row = await db.Wh40kQuestInstances.SingleAsync(x => x.Id == questId, cancel);
                if (fact.Actor is not {} actor || !Wh40kQuestSnapshotCodec.TryDeserialize(row.SnapshotJson, out var terms) ||
                    !terms!.Recipients.Contains(actor) || !await QuestActorAllowed(db, owner, actor, false, fact.OccurredAt, cancel)) return false;
            }
            var existing = await db.Wh40kQuestInboxes.FindAsync([questId, fact.ObjectiveId, fact.EventId], cancel);
            if (existing != null) return existing.FactJson == json;
            if (await db.Wh40kQuestInboxes.CountAsync(x => x.QuestId == questId && x.State == 0, cancel) >= 256) return false;
            db.Wh40kQuestInboxes.Add(new() { QuestId = questId, ObjectiveId = fact.ObjectiveId, EventId = fact.EventId, OccurredAt = fact.OccurredAt, FactJson = json });
            return true;
        }, cancel);
    }

    public async Task<int> ProcessWh40kQuestInboxAsync(DateTime now, int limit = 64,
        IReadOnlySet<string>? availableDefinitions = null,
        Func<Wh40kQuestContractSnapshot, Task<bool>>? contentAvailable = null, CancellationToken cancel = default)
    {
        QuestUtc(now);
        List<Wh40kQuestInbox> pending;
        await using (var guard = await GetDb(cancel))
        {
            var query = guard.DbContext.Wh40kQuestInboxes.AsNoTracking().Where(x => x.State == 0 && x.OccurredAt <= now &&
                (x.RetryAfter == null || x.RetryAfter <= now));
            if (availableDefinitions != null)
            {
                var available = availableDefinitions.ToArray();
                query = query.Where(x => guard.DbContext.Wh40kQuestInstances.Any(q => q.Id == x.QuestId && available.Contains(q.DefinitionId)));
            }
            pending = await query.OrderBy(x => x.OccurredAt).ThenBy(x => x.EventId)
                .Take(Math.Clamp(limit, 1, 128)).ToListAsync(cancel);
        }
        var processed = 0;
        foreach (var entry in pending)
        {
            Wh40kQuestInstance quest;
            await using (var guard = await GetDb(cancel)) quest = await guard.DbContext.Wh40kQuestInstances.AsNoTracking().SingleAsync(x => x.Id == entry.QuestId, cancel);
            var snapshotValid = Wh40kQuestSnapshotCodec.TryDeserialize(quest.SnapshotJson, out var snapshot);
            if (!snapshotValid || QuestOwnerKey(snapshot!.Owner) != quest.OwnerKey ||
                contentAvailable != null && !await contentAvailable(snapshot))
            {
                // Keep the fact durable while letting other accounts advance past unavailable content.
                await using var guard = await GetDb(cancel);
                await guard.DbContext.Wh40kQuestInboxes.Where(x => x.QuestId == entry.QuestId &&
                    x.ObjectiveId == entry.ObjectiveId && x.EventId == entry.EventId && x.State == 0)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.RetryAfter, now.AddSeconds(30)), cancel);
                continue;
            }
            processed += await QuestWriteAsync(snapshot!.Owner, async db =>
            {
                var inbox = await db.Wh40kQuestInboxes.FindAsync([entry.QuestId, entry.ObjectiveId, entry.EventId], cancel);
                if (inbox == null || inbox.State != 0) return 0;
                var current = await db.Wh40kQuestInstances.SingleAsync(x => x.Id == entry.QuestId, cancel);
                var fact = JsonSerializer.Deserialize<Wh40kQuestFact>(inbox.FactJson)!;
                var objectives = await db.Wh40kQuestObjectives.Where(x => x.QuestId == current.Id).OrderBy(x => x.Ordinal).ToListAsync(cancel);
                var objective = objectives.Single(x => x.ObjectiveId == entry.ObjectiveId);
                var definition = snapshot.Objectives.Single(x => x.Id == entry.ObjectiveId);
                var duplicateTarget = definition.DistinctTargets && (string.IsNullOrWhiteSpace(fact.TargetToken) ||
                    await db.Wh40kQuestReceipts.AnyAsync(x => x.QuestId == current.Id && x.ObjectiveId == entry.ObjectiveId && x.TargetToken == fact.TargetToken, cancel));
                // A delayed possession observation must not replace a newer committed observation.
                var staleObservation = fact.SetCount && await db.Wh40kQuestInboxes.AnyAsync(x => x.QuestId == current.Id &&
                    x.ObjectiveId == entry.ObjectiveId && x.State == 1 && x.OccurredAt >= fact.OccurredAt, cancel);
                if (current.State > 1 || definition.Kind.IsSubmission() ||
                    fact.SetCount != (definition.Kind == Wh40kQuestObjectiveKind.CollectItem) ||
                    !fact.SetCount && objective.Count >= objective.RequiredCount ||
                    fact.OccurredAt < current.AcceptedAt || fact.OccurredAt > now || current.Deadline <= now || duplicateTarget || staleObservation ||
                    snapshot.Order == Wh40kQuestOrder.Sequential && objectives.Any(x => x.Ordinal < objective.Ordinal && x.Count < x.RequiredCount))
                { inbox.State = 2; return 1; }
                objective.Count = Math.Clamp(fact.SetCount ? fact.Amount : objective.Count + fact.Amount, 0, objective.RequiredCount);
                db.Wh40kQuestReceipts.Add(new()
                {
                    QuestId = current.Id, ObjectiveId = objective.ObjectiveId, EventId = fact.EventId,
                    TargetToken = definition.DistinctTargets ? fact.TargetToken : null, AppliedAt = now,
                });
                inbox.State = 1; current.Revision++;
                current.State = objectives.All(x => x.Count == x.RequiredCount) ? (int) Wh40kQuestState.ReadyToTurnIn : (int) Wh40kQuestState.Active;
                if (current.State == 1 && snapshot.Completion == Wh40kQuestCompletionPolicy.Automatic)
                    await CompleteQuestAsync(db, current, snapshot, now, cancel);
                db.Wh40kQuestAudits.Add(new() { OwnerKey = current.OwnerKey, QuestId = current.Id, Action = "Progress", Reason = objective.ObjectiveId, At = now });
                return 1;
            }, cancel);
        }
        return processed;
    }

    public async Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetWh40kQuestRewardIntentsAsync(Wh40kQuestOwnerKey owner, int limit = 128, CancellationToken cancel = default)
    {
        var key = QuestOwnerKey(owner); await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestRewardIntents.AsNoTracking().Where(x => guard.DbContext.Wh40kQuestInstances.Any(q => q.Id == x.QuestId && q.OwnerKey == key))
            .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 128)).ToListAsync(cancel);
    }

    public async Task<IReadOnlyList<Wh40kQuestAudit>> GetWh40kQuestAuditAsync(Wh40kQuestOwnerKey owner, int limit = 100, CancellationToken cancel = default)
    {
        var key = QuestOwnerKey(owner); await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestAudits.AsNoTracking().Where(x => x.OwnerKey == key).OrderByDescending(x => x.Id).Take(Math.Clamp(limit, 1, 100)).ToListAsync(cancel);
    }

    public async Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetWh40kQuestVisibleRewardsAsync(Wh40kQuestOwnerKey owner,
        IReadOnlyList<Guid> quests, CancellationToken cancel = default, Guid? recipient = null)
    {
        // Five active contracts and at most 32 visible history entries; filter before applying a limit.
        if (quests.Count > 42 || quests.Any(x => x == Guid.Empty)) throw new ArgumentException("Invalid visible quest window.");
        var actualRecipient = recipient ?? owner.Id;
        var key = QuestOwnerKey(owner);
        var ids = quests.Distinct().ToArray();
        await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestRewardIntents.AsNoTracking().Where(x => ids.Contains(x.QuestId) && x.Recipient == actualRecipient &&
            guard.DbContext.Wh40kQuestInstances.Any(q => q.Id == x.QuestId && q.OwnerKey == key))
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.EntryId).Take(42 * 32).ToListAsync(cancel);
    }

    public Task<bool> SaveWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Wh40kQuestSubmission submission, long expectedRevision, CancellationToken cancel = default)
    {
        if (submission.Id == Guid.Empty || submission.Amount is <= 0 or > 10000 || submission.PayloadJson.Length is <= 0 or > 1048576 ||
            !Enum.IsDefined((Wh40kQuestSubmissionState) submission.State) || submission.State < 0 || submission.State > 4)
            throw new ArgumentException("Invalid submission metadata.");
        var key = QuestOwnerKey(owner);
        return QuestWriteAsync(owner, async db =>
        {
            if (!await db.Wh40kQuestInstances.AnyAsync(x => x.Id == submission.QuestId && x.OwnerKey == key, cancel) ||
                !await db.Wh40kQuestObjectives.AnyAsync(x => x.QuestId == submission.QuestId && x.ObjectiveId == submission.ObjectiveId, cancel)) return false;
            var existing = await db.Wh40kQuestSubmissions.FindAsync([submission.Id], cancel);
            if (existing == null)
            {
                if (expectedRevision != 0 || submission.State != 0) return false;
                db.Wh40kQuestSubmissions.Add(new()
                {
                    Id = submission.Id, QuestId = submission.QuestId, ObjectiveId = submission.ObjectiveId,
                    Amount = submission.Amount, State = 0, PayloadJson = submission.PayloadJson, Revision = 1,
                });
                return true;
            }
            if (existing.QuestId != submission.QuestId || existing.ObjectiveId != submission.ObjectiveId ||
                existing.Amount != submission.Amount || existing.PayloadJson != submission.PayloadJson) return false;
            if (existing.State == submission.State) return true;
            if (existing.Revision != expectedRevision || existing.State >= 2 ||
                !(existing.State == 0 && submission.State is 1 or 3 or 4 || existing.State == 1 && submission.State is 3 or 4)) return false;
            existing.State = submission.State; existing.Revision++;
            db.Wh40kQuestAudits.Add(new() { OwnerKey = key, QuestId = submission.QuestId, Actor = owner.Id,
                Action = "SubmissionState", Reason = $"{submission.Id}:{(Wh40kQuestSubmissionState) submission.State}", At = DateTime.UtcNow });
            return true;
        }, cancel);
    }

    public async Task<IReadOnlyList<Wh40kQuestSubmission>> GetWh40kQuestSubmissionsAsync(Wh40kQuestOwnerKey owner, int limit = 64, CancellationToken cancel = default)
    {
        var key = QuestOwnerKey(owner); await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestSubmissions.AsNoTracking().Where(x => guard.DbContext.Wh40kQuestInstances.Any(q => q.Id == x.QuestId && q.OwnerKey == key))
            .OrderBy(x => x.State).ThenBy(x => x.Id).Take(Math.Clamp(limit, 1, 128)).ToListAsync(cancel);
    }
}
