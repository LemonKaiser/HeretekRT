using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.Quests;
using Content.Shared._WH40K.Quests;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public Task<bool> IssueWh40kQuestEvidenceAsync(Wh40kQuestOwnerKey owner, Guid actor, Guid quest, string objective, CancellationToken cancel = default)
        => QuestWriteAsync(owner, async db =>
        {
            if (owner.Scope != Wh40kQuestOwnerScope.Account || owner.Id != actor) return false;
            var row = await db.Wh40kQuestInstances.SingleOrDefaultAsync(x => x.Id == quest && x.OwnerKey == QuestOwnerKey(owner), cancel);
            if (row == null || row.State > 1 || !Wh40kQuestSnapshotCodec.TryDeserialize(row.SnapshotJson, out var terms) ||
                !terms!.Objectives.Any(x => x.Id == objective && x.Kind == Wh40kQuestObjectiveKind.CollectEvidence)) return false;
            if (await db.Wh40kQuestAudits.AnyAsync(x => x.QuestId == quest && x.Action == "EvidenceIssued" && x.Reason == objective, cancel)) return false;
            db.Wh40kQuestAudits.Add(new() { OwnerKey = row.OwnerKey, QuestId = quest, Actor = actor,
                Action = "EvidenceIssued", Reason = objective, At = DateTime.UtcNow });
            return true;
        }, cancel);
    public async Task<Wh40kQuestOwnerKey> ResolveWh40kQuestOwnerAsync(Guid actor, Guid target, CancellationToken cancel = default)
    {
        await using var guard = await GetDb(cancel);
        var db = guard.DbContext;
        var key = await db.Wh40kQuestInstances.Where(x => x.Id == target).Select(x => x.OwnerKey).FirstOrDefaultAsync(cancel) ??
            await db.Wh40kQuestOffers.Where(x => x.Id == target).Select(x => x.OwnerKey).FirstOrDefaultAsync(cancel);
        var account = new Wh40kQuestOwnerKey(Wh40kQuestOwnerScope.Account, actor);
        if (key == QuestOwnerKey(account)) return account;
        var party = await db.Wh40kPartyMembers.Where(x => x.UserId == actor).Select(x => (Guid?) x.PartyId).SingleOrDefaultAsync(cancel);
        if (party is {} id && key == QuestOwnerKey(Wh40kQuestOwnerKey.Party(id))) return Wh40kQuestOwnerKey.Party(id);
        return account;
    }

    private static async Task<bool> QuestActorAllowed(ServerDbContext db, Wh40kQuestOwnerKey owner,
        Guid actor, bool leaderOnly, DateTime now, CancellationToken cancel)
    {
        if (owner.Scope == Wh40kQuestOwnerScope.Account) return actor == owner.Id;
        return await db.Wh40kParties.AnyAsync(p => p.Id == owner.Id && p.ExpiresAt > now &&
            (!leaderOnly || p.LeaderUserId == actor) && db.Wh40kPartyMembers.Any(m => m.PartyId == p.Id && m.UserId == actor), cancel);
    }

    public async Task<IReadOnlyList<Wh40kQuestAccountState>> GetWh40kQuestAccountStateAsync(Guid user, CancellationToken cancel = default)
    {
        await using var guard = await GetDb(cancel);
        return await guard.DbContext.Wh40kQuestAccountStates.AsNoTracking().Where(x => x.UserId == user)
            .OrderBy(x => x.Key).Take(512).ToListAsync(cancel);
    }

    public Task<bool> ApplyWh40kQuestStateRewardAsync(Guid quest, string entry, Guid recipient, CancellationToken cancel = default)
        => QuestWriteAsync(new(Wh40kQuestOwnerScope.Account, recipient), async db =>
        {
            var intent = await db.Wh40kQuestRewardIntents.FindAsync([quest, entry, recipient], cancel);
            if (intent == null) return false;
            if (intent.State == 2) return true;
            var reward = JsonSerializer.Deserialize<Wh40kQuestRewardSnapshot>(intent.PayloadJson);
            var key = reward?.Kind switch
            {
                Wh40kQuestRewardKind.FactionReputation when reward.Faction != null => "rep:" + reward.Faction,
                Wh40kQuestRewardKind.Flag when reward.Flag != null => "flag:" + reward.Flag,
                Wh40kQuestRewardKind.Access when reward.Access != null => "access:" + reward.Access,
                _ => null,
            };
            if (key == null || key.Length > 128 || reward!.EntryId != entry || reward.Amount <= 0) return false;
            var state = await db.Wh40kQuestAccountStates.FindAsync([recipient, key], cancel);
            if (state == null)
            {
                if (await db.Wh40kQuestAccountStates.CountAsync(x => x.UserId == recipient, cancel) >= 512) return false;
                state = new() { UserId = recipient, Key = key };
                db.Wh40kQuestAccountStates.Add(state);
            }
            state.Amount = reward.Kind == Wh40kQuestRewardKind.FactionReputation ? checked(state.Amount + reward.Amount) : 1;
            intent.State = 2;
            return true;
        }, cancel);

    private static async Task<bool> CommitQuestBranch(ServerDbContext db, Wh40kQuestContractSnapshot snapshot, CancellationToken cancel)
    {
        if (snapshot.BranchGroup == null) return true;
        var key = "branch:" + snapshot.BranchGroup;
        var row = await db.Wh40kQuestAccountStates.FindAsync([snapshot.Owner.Id, key], cancel);
        if (row != null) return row.Value == snapshot.BranchChoice;
        if (await db.Wh40kQuestAccountStates.CountAsync(x => x.UserId == snapshot.Owner.Id, cancel) >= 512) return false;
        db.Wh40kQuestAccountStates.Add(new() { UserId = snapshot.Owner.Id, Key = key, Value = snapshot.BranchChoice, Amount = 1 });
        return true;
    }
}
