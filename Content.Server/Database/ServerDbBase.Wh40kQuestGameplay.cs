using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.Quests;
using Content.Shared._WH40K.Quests;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public async Task<Wh40kQuestSubmission?> GetWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Guid id, CancellationToken cancel = default)
    {
        var key = QuestOwnerKey(owner);
        await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            guard.DbContext.Wh40kQuestInstances.Any(q => q.Id == x.QuestId && q.OwnerKey == key), cancel);
    }

    public async Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetPendingWh40kQuestIntentsAsync(int offset = 0, CancellationToken cancel = default)
    {
        await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestRewardIntents.AsNoTracking().Where(x => x.State < 2)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.QuestId).ThenBy(x => x.EntryId).ThenBy(x => x.Recipient)
            .Skip(Math.Max(0, offset)).Take(64).ToListAsync(cancel);
    }

    public async Task<bool> AcknowledgeWh40kQuestIntentAsync(Guid quest, string entry, Guid recipient,
        bool experience, CancellationToken cancel = default)
    {
        await using var guard = await GetDb(cancel);
        var intent = await guard.DbContext.Wh40kQuestRewardIntents.FindAsync([quest, entry, recipient], cancel);
        if (intent == null) return false;
        var delivered = experience
            ? await guard.DbContext.Wh40kExperienceLedgers.AnyAsync(x => x.UserId == recipient && x.RewardId == intent.RewardId, cancel)
            : await guard.DbContext.Wh40kRewardDeliveries.AnyAsync(x => x.UserId == recipient && x.RewardId == intent.RewardId &&
                x.EntryId == entry && x.Status == (int) Content.Server._WH40K.Progression.Wh40kRewardDeliveryStatus.Delivered, cancel);
        await guard.DbContext.Wh40kQuestRewardIntents.Where(x => x.QuestId == quest && x.EntryId == entry && x.Recipient == recipient && x.State < 2)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.State, delivered ? 2 : 1), cancel);
        return delivered;
    }

    public Task<bool> CommitWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Guid id, DateTime now, CancellationToken cancel = default)
    {
        QuestUtc(now);
        return QuestWriteAsync(owner, async db =>
        {
            var submission = await db.Wh40kQuestSubmissions.FindAsync([id], cancel);
            if (submission == null) return false;
            var key = QuestOwnerKey(owner);
            var quest = await db.Wh40kQuestInstances.SingleOrDefaultAsync(x => x.Id == submission.QuestId && x.OwnerKey == key, cancel);
            if (quest == null) return false;
            if (submission.State == 2) return true;
            if (submission.State != 1 || quest.State > 1 || quest.Deadline <= now ||
                !Wh40kQuestSnapshotCodec.TryDeserialize(quest.SnapshotJson, out var snapshot) || snapshot!.Owner != owner) return false;
            var definition = snapshot.Objectives.SingleOrDefault(x => x.Id == submission.ObjectiveId);
            var rows = await db.Wh40kQuestObjectives.Where(x => x.QuestId == quest.Id).OrderBy(x => x.Ordinal).ToListAsync(cancel);
            var objective = rows.SingleOrDefault(x => x.ObjectiveId == submission.ObjectiveId);
            if (definition == null || !definition.Kind.IsSubmission() || objective == null ||
                submission.Amount > objective.RequiredCount - objective.Count ||
                snapshot.Order == Wh40kQuestOrder.Sequential && rows.Any(x => x.Ordinal < objective.Ordinal && x.Count < x.RequiredCount)) return false;
            objective.Count += submission.Amount;
            submission.State = 2; submission.Revision++;
            db.Wh40kQuestReceipts.Add(new() { QuestId = quest.Id, ObjectiveId = objective.ObjectiveId, EventId = id, AppliedAt = now });
            quest.Revision++;
            quest.State = rows.All(x => x.Count == x.RequiredCount) ? 1 : 0;
            if (quest.State == 1 && snapshot.Completion == Wh40kQuestCompletionPolicy.Automatic)
                await CompleteQuestAsync(db, quest, snapshot, now, cancel);
            db.Wh40kQuestAudits.Add(new() { OwnerKey = key, QuestId = quest.Id, Actor = owner.Id, Action = "SubmitItems", Reason = id.ToString(), At = now });
            return true;
        }, cancel);
    }
}
