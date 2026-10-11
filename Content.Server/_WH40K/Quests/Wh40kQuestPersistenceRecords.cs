using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WH40K.Quests;

namespace Content.Server._WH40K.Quests;

public enum Wh40kQuestMutation : byte { Accept, Abandon, Complete, Expire, AdminFail }
public enum Wh40kQuestSubmissionState : byte { Reserved, Captured, Committed, Restored, Quarantined }

public sealed record Wh40kQuestRequest(Guid OperationId, Wh40kQuestMutation Kind, Guid TargetId,
    long ExpectedRevision = 0, string Reason = "");
public sealed record Wh40kQuestResult(Wh40kQuestOperationResult Result, Guid? QuestId, long Revision);
public sealed record Wh40kQuestOfferDraft(Guid Id, Wh40kQuestContractSnapshot Terms, DateTime ExpiresAt, int ActiveLimit = 5);
public sealed record Wh40kQuestFact(Guid EventId, string ObjectiveId, int Amount, bool SetCount,
    DateTime OccurredAt, string? TargetToken = null, Wh40kQuestActivityBinding? Activity = null, Guid? Actor = null);
public sealed record Wh40kQuestStoredStatus(Wh40kQuestInstance Contract, IReadOnlyList<Wh40kQuestObjective> Objectives,
    Wh40kQuestContractSnapshot? Snapshot, Wh40kQuestBindingState Binding);

/// <summary>Durable boundary. Implemented through the existing DB manager, not a second database service.</summary>
public interface IWh40kQuestRepository
{
    Task<Wh40kQuestOwnerKey> ResolveWh40kQuestOwnerAsync(Guid actor, Guid target, CancellationToken cancel = default);
    Task<IReadOnlyList<Wh40kQuestAccountState>> GetWh40kQuestAccountStateAsync(Guid user, CancellationToken cancel = default);
    Task<bool> ApplyWh40kQuestStateRewardAsync(Guid quest, string entry, Guid recipient, CancellationToken cancel = default);
    Task<bool> IssueWh40kQuestEvidenceAsync(Wh40kQuestOwnerKey owner, Guid actor, Guid quest, string objective, CancellationToken cancel = default);
    Task<IReadOnlySet<string>> GetCompletedWh40kQuestDefinitionsAsync(Wh40kQuestOwnerKey owner,
        IReadOnlyList<string> definitions, CancellationToken cancel = default);
    Task<IReadOnlySet<string>> GetEligibleWh40kQuestDefinitionsAsync(IReadOnlyList<Wh40kQuestContractSnapshot> candidates,
        int activeLimit, DateTime now, CancellationToken cancel = default);
    Task<Wh40kQuestSubmission?> GetWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Guid id, CancellationToken cancel = default);
    Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetPendingWh40kQuestIntentsAsync(int offset = 0, CancellationToken cancel = default);
    Task<bool> AcknowledgeWh40kQuestIntentAsync(Guid quest, string entry, Guid recipient, bool experience, CancellationToken cancel = default);
    Task<bool> CommitWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Guid id, DateTime now, CancellationToken cancel = default);
    Task RecoverWh40kQuestsAsync(Wh40kQuestOwnerKey owner, DateTime now, Guid generation, CancellationToken cancel = default);
    Task<bool> PutWh40kQuestOfferAsync(Wh40kQuestOfferDraft offer, CancellationToken cancel = default);
    Task<Wh40kQuestResult> MutateWh40kQuestAsync(Wh40kQuestOwnerKey owner, Guid actor,
        Wh40kQuestRequest request, DateTime now, bool admin = false, CancellationToken cancel = default);
    Task<IReadOnlyList<Wh40kQuestInstance>> GetWh40kQuestsAsync(Wh40kQuestOwnerKey owner,
        bool history = false, int limit = 32, CancellationToken cancel = default);
    Task<IReadOnlyList<Wh40kQuestOffer>> GetWh40kQuestOffersAsync(Wh40kQuestOwnerKey owner, DateTime now,
        int limit = 32, CancellationToken cancel = default);
    Task<IReadOnlyList<Wh40kQuestObjective>> GetWh40kQuestObjectivesAsync(Wh40kQuestOwnerKey owner,
        Guid questId, CancellationToken cancel = default);
    Task<Wh40kQuestResult?> GetWh40kQuestOperationAsync(Wh40kQuestOwnerKey owner, Guid operationId,
        CancellationToken cancel = default);
    Task<bool> EnqueueWh40kQuestFactAsync(Wh40kQuestOwnerKey owner, Guid questId, Wh40kQuestFact fact,
        CancellationToken cancel = default);
    Task<int> ProcessWh40kQuestInboxAsync(DateTime now, int limit = 64,
        IReadOnlySet<string>? availableDefinitions = null,
        Func<Wh40kQuestContractSnapshot, Task<bool>>? contentAvailable = null, CancellationToken cancel = default);
    Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetWh40kQuestRewardIntentsAsync(Wh40kQuestOwnerKey owner,
        int limit = 128, CancellationToken cancel = default);
    Task<IReadOnlyList<Wh40kQuestRewardIntent>> GetWh40kQuestVisibleRewardsAsync(Wh40kQuestOwnerKey owner,
        IReadOnlyList<Guid> quests, CancellationToken cancel = default, Guid? recipient = null);
    Task<IReadOnlyList<Wh40kQuestAudit>> GetWh40kQuestAuditAsync(Wh40kQuestOwnerKey owner,
        int limit = 100, CancellationToken cancel = default);
    Task<bool> SaveWh40kQuestSubmissionAsync(Wh40kQuestOwnerKey owner, Wh40kQuestSubmission submission,
        long expectedRevision, CancellationToken cancel = default);
    Task<IReadOnlyList<Wh40kQuestSubmission>> GetWh40kQuestSubmissionsAsync(Wh40kQuestOwnerKey owner,
        int limit = 64, CancellationToken cancel = default);
}
