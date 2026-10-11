using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.Quests;
using Content.Shared._WH40K.Quests;

namespace Content.Server.Database;

public partial interface IServerDbManager : IWh40kQuestRepository;

public sealed partial class ServerDbManager
{
    public Task<bool> IssueWh40kQuestEvidenceAsync(Wh40kQuestOwnerKey owner, Guid actor, Guid quest, string objective, CancellationToken cancel = default)
        => RunDbCommand(() => _db.IssueWh40kQuestEvidenceAsync(owner, actor, quest, objective, cancel));
    public Task<Wh40kQuestOwnerKey> ResolveWh40kQuestOwnerAsync(Guid actor, Guid target, CancellationToken cancel = default)
        => RunDbCommand(() => _db.ResolveWh40kQuestOwnerAsync(actor, target, cancel));
    public Task<IReadOnlyList<Wh40kQuestAccountState>> GetWh40kQuestAccountStateAsync(Guid user, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestAccountStateAsync(user, cancel));
    public Task<bool> ApplyWh40kQuestStateRewardAsync(Guid quest, string entry, Guid recipient, CancellationToken cancel = default)
        => RunDbCommand(() => _db.ApplyWh40kQuestStateRewardAsync(quest, entry, recipient, cancel));
    public Task<IReadOnlySet<string>> GetEligibleWh40kQuestDefinitionsAsync(IReadOnlyList<Wh40kQuestContractSnapshot> candidates,
        int activeLimit, DateTime now, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetEligibleWh40kQuestDefinitionsAsync(candidates, activeLimit, now, cancel));
    public Task<IReadOnlySet<string>> GetCompletedWh40kQuestDefinitionsAsync(Wh40kQuestOwnerKey owner,
        IReadOnlyList<string> definitions, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetCompletedWh40kQuestDefinitionsAsync(owner, definitions, cancel));
    public Task<Wh40kQuestSubmission?> GetWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Guid id, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestSubmissionAsync(owner, id, cancel));
    public Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetPendingWh40kQuestIntentsAsync(int offset = 0, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetPendingWh40kQuestIntentsAsync(offset, cancel));
    public Task<bool> AcknowledgeWh40kQuestIntentAsync(Guid quest, string entry, Guid recipient, bool experience, CancellationToken cancel = default)
        => RunDbCommand(() => _db.AcknowledgeWh40kQuestIntentAsync(quest, entry, recipient, experience, cancel));
    public Task<bool> CommitWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Guid id, DateTime now, CancellationToken cancel = default)
        => RunDbCommand(() => _db.CommitWh40kQuestSubmissionAsync(owner, id, now, cancel));
    public Task RecoverWh40kQuestsAsync(Wh40kQuestOwnerKey owner, DateTime now, Guid generation, CancellationToken cancel = default)
        => RunDbCommand(() => _db.RecoverWh40kQuestsAsync(owner, now, generation, cancel));

    public Task<bool> PutWh40kQuestOfferAsync(Wh40kQuestOfferDraft offer, CancellationToken cancel = default)
        => RunDbCommand(() => _db.PutWh40kQuestOfferAsync(offer, cancel));

    public Task<Wh40kQuestResult> MutateWh40kQuestAsync(Wh40kQuestOwnerKey owner, Guid actor, Wh40kQuestRequest request, DateTime now, bool admin = false, CancellationToken cancel = default)
        => RunDbCommand(() => _db.MutateWh40kQuestAsync(owner, actor, request, now, admin, cancel));

    public Task<IReadOnlyList<Wh40kQuestInstance>> GetWh40kQuestsAsync(Wh40kQuestOwnerKey owner, bool history = false, int limit = 32, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestsAsync(owner, history, limit, cancel));

    public Task<IReadOnlyList<Wh40kQuestOffer>> GetWh40kQuestOffersAsync(Wh40kQuestOwnerKey owner, DateTime now, int limit = 32, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestOffersAsync(owner, now, limit, cancel));

    public Task<IReadOnlyList<Wh40kQuestObjective>> GetWh40kQuestObjectivesAsync(Wh40kQuestOwnerKey owner, Guid questId, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestObjectivesAsync(owner, questId, cancel));

    public Task<Wh40kQuestResult?> GetWh40kQuestOperationAsync(Wh40kQuestOwnerKey owner, Guid operationId, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestOperationAsync(owner, operationId, cancel));

    public Task<bool> EnqueueWh40kQuestFactAsync(Wh40kQuestOwnerKey owner, Guid questId, Wh40kQuestFact fact, CancellationToken cancel = default)
        => RunDbCommand(() => _db.EnqueueWh40kQuestFactAsync(owner, questId, fact, cancel));

    public Task<int> ProcessWh40kQuestInboxAsync(DateTime now, int limit = 64, IReadOnlySet<string>? availableDefinitions = null,
        Func<Wh40kQuestContractSnapshot, Task<bool>>? contentAvailable = null, CancellationToken cancel = default)
        => RunDbCommand(() => _db.ProcessWh40kQuestInboxAsync(now, limit, availableDefinitions, contentAvailable, cancel));

    public Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetWh40kQuestRewardIntentsAsync(Wh40kQuestOwnerKey owner, int limit = 128, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestRewardIntentsAsync(owner, limit, cancel));

    public Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetWh40kQuestVisibleRewardsAsync(Wh40kQuestOwnerKey owner,
        IReadOnlyList<Guid> quests, CancellationToken cancel = default, Guid? recipient = null)
        => RunDbCommand(() => _db.GetWh40kQuestVisibleRewardsAsync(owner, quests, cancel, recipient));

    public Task<IReadOnlyList<Wh40kQuestAudit>> GetWh40kQuestAuditAsync(Wh40kQuestOwnerKey owner, int limit = 100, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestAuditAsync(owner, limit, cancel));

    public Task<bool> SaveWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Wh40kQuestSubmission submission, long expectedRevision, CancellationToken cancel = default)
        => RunDbCommand(() => _db.SaveWh40kQuestSubmissionAsync(owner, submission, expectedRevision, cancel));

    public Task<IReadOnlyList<Wh40kQuestSubmission>> GetWh40kQuestSubmissionsAsync(Wh40kQuestOwnerKey owner, int limit = 64, CancellationToken cancel = default)
        => RunDbCommand(() => _db.GetWh40kQuestSubmissionsAsync(owner, limit, cancel));
}
