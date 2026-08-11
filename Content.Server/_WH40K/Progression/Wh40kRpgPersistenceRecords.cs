using System.Collections.Generic;
using System.Linq;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Progression;

public enum Wh40kRpgFoundationSource : byte
{
    Onboarding,
    LegacyRandom,
}

public enum Wh40kRewardDeliveryStatus : byte
{
    Pending,
    Delivered,
    Claimed,
}

public enum Wh40kExperienceAwardStatus : byte
{
    Awarded,
    Duplicate,
}

public enum Wh40kPartyMutationStatus : byte
{
    Success,
    AccountNotFound,
    AlreadyInParty,
    NotInParty,
    PartyNotFound,
    PartyExpired,
    NotLeader,
    PartyFull,
    RevisionMismatch,
}

public enum Wh40kPartyInvitationStatus : byte
{
    Success,
    InvalidTarget,
    InvitesDisabled,
    AlreadyInParty,
    NotLeader,
    PartyFull,
    InvitationNotFound,
    InvitationExpired,
}

public enum Wh40kCharacteristicSpendStatus : byte
{
    Success,
    AccountNotFound,
    InvalidCharacteristic,
    InvalidCount,
    RevisionMismatch,
    InsufficientDevelopmentPoints,
}

public enum Wh40kClassAdminOperation : byte
{
    Purchase,
    SetClass,
    ReplaceSkills,
    GrantSkill,
    RevokeSkill,
    TreeMigration,
}

public sealed record Wh40kXpAwardRequest(
    string RewardId,
    Wh40kExperienceSourceType SourceType,
    long AmountTenths,
    int? RoundId = null,
    string? IssuerEntity = null,
    string? ContextJson = null,
    IReadOnlyList<Wh40kLevelRewardDefinition>? LevelRewards = null);

public sealed record Wh40kLevelRewardDefinition(
    int Level,
    string RewardId,
    IReadOnlyList<Wh40kRewardDeliveryDraft> Entries);

public sealed record Wh40kRewardDeliveryDraft(
    string RewardId,
    string EntryId,
    string RewardType,
    string? PrototypeId,
    long Amount,
    string? ContextJson);

public sealed record Wh40kRpgFoundationDraft(
    string HomeworldId,
    string OriginId,
    string ClassId,
    string InitialPortraitId,
    IReadOnlyDictionary<Wh40kCharacteristic, int> InitialCharacteristicPoints,
    Wh40kRpgFoundationSource Source)
{
    public Wh40kCharacterBuild ToCharacterBuild()
    {
        return new Wh40kCharacterBuild
        {
            HomeworldId = HomeworldId,
            OriginId = OriginId,
            ClassId = ClassId,
            PortraitId = InitialPortraitId,
            CharacteristicPoints = new Dictionary<Wh40kCharacteristic, int>(InitialCharacteristicPoints),
        };
    }
}

public sealed record Wh40kRpgFoundationRecord(
    NetUserId UserId,
    string HomeworldId,
    string OriginId,
    string ClassId,
    string InitialPortraitId,
    IReadOnlyDictionary<Wh40kCharacteristic, int> InitialCharacteristicPoints,
    Wh40kRpgFoundationSource Source,
    DateTime CreatedAt)
{
    public Wh40kCharacterBuild ToCharacterBuild()
    {
        return new Wh40kCharacterBuild
        {
            HomeworldId = HomeworldId,
            OriginId = OriginId,
            ClassId = ClassId,
            PortraitId = InitialPortraitId,
            CharacteristicPoints = new Dictionary<Wh40kCharacteristic, int>(InitialCharacteristicPoints),
        };
    }
}

public sealed record Wh40kRpgProgressRecord(
    NetUserId UserId,
    int SchemaVersion,
    long ExperienceTenths,
    int Level,
    int UnspentDevelopmentPoints,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Revision);

public sealed record Wh40kAttributePurchaseRecord(
    Wh40kCharacteristic Characteristic,
    int PurchasedPoints,
    DateTime FirstPurchasedAt,
    DateTime UpdatedAt);

public sealed record Wh40kAccountRpgRecord(
    Wh40kRpgFoundationRecord Foundation,
    Wh40kRpgProgressRecord Progress,
    IReadOnlyDictionary<Wh40kCharacteristic, Wh40kAttributePurchaseRecord> AttributePurchases);

public sealed record Wh40kAccountClassSkillRecord(
    string SkillId,
    DateTime PurchasedAt);

public sealed record Wh40kAccountClassProgressRecord(
    NetUserId UserId,
    int TreeVersion,
    long Revision,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<Wh40kAccountClassSkillRecord> Skills)
{
    public IReadOnlySet<string> PurchasedSkillIds =>
        Skills.Select(skill => skill.SkillId).ToHashSet(StringComparer.Ordinal);
}

public sealed record Wh40kClassSkillPurchaseSpec(
    string SkillId,
    string ClassId,
    string? PrerequisiteSkillId,
    int MinimumLevel,
    int Cost,
    int TreeVersion,
    Wh40kClassContentAvailability Availability)
{
    /// <summary>
    /// Immutable server-side catalogue of canonical entitlement costs. The database uses it inside the purchase
    /// transaction, so zero-cost mirror display nodes never make the point calculation depend on a stale cache.
    /// </summary>
    public IReadOnlyDictionary<string, int> PersistentSkillCosts { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

public sealed record Wh40kClassSkillPurchaseResult(
    Wh40kClassSkillPurchaseStatus Status,
    Wh40kAccountRpgRecord? Account,
    Wh40kAccountClassProgressRecord? ClassProgress)
{
    public bool IsSuccess => Status == Wh40kClassSkillPurchaseStatus.Success;
}

public sealed record Wh40kClassAdminMutationRequest(
    Guid OperationId,
    Wh40kClassAdminOperation Operation,
    long ExpectedRevision,
    string NewClassId,
    IReadOnlyList<string> NewSkillIds,
    int TreeVersion,
    string ActorId,
    string ActorName,
    string Reason);

public sealed record Wh40kClassAdminMutationResult(
    Wh40kClassSkillPurchaseStatus Status,
    Wh40kAccountRpgRecord? Account,
    Wh40kAccountClassProgressRecord? ClassProgress)
{
    public bool IsSuccess => Status == Wh40kClassSkillPurchaseStatus.Success;
}

public sealed record Wh40kClassAuditRecord(
    Guid OperationId,
    NetUserId UserId,
    string Operation,
    string ActorId,
    string ActorName,
    string Reason,
    string PreviousClassId,
    string NewClassId,
    IReadOnlyList<string> PreviousSkillIds,
    IReadOnlyList<string> NewSkillIds,
    DateTime CreatedAt);

public sealed record Wh40kExperienceAwardResult(
    Wh40kExperienceAwardStatus Status,
    Wh40kAccountRpgRecord Account,
    Wh40kExperienceLedgerRecord Ledger,
    int PreviousLevel,
    int LevelsGained,
    int DevelopmentPointsAwarded)
{
    public bool IsAwarded => Status == Wh40kExperienceAwardStatus.Awarded;
}

public sealed record Wh40kDevelopmentPointGrantResult(
    Wh40kExperienceAwardStatus Status,
    Wh40kAccountRpgRecord Account,
    Wh40kExperienceLedgerRecord Ledger,
    int DevelopmentPointsAwarded)
{
    public bool IsAwarded => Status == Wh40kExperienceAwardStatus.Awarded;
}

public sealed record Wh40kCharacteristicSpendResult(
    Wh40kCharacteristicSpendStatus Status,
    Wh40kAccountRpgRecord? Account)
{
    public bool IsSuccess => Status == Wh40kCharacteristicSpendStatus.Success;
}

public sealed record Wh40kExperienceLedgerRecord(
    long Id,
    NetUserId UserId,
    string RewardId,
    string SourceType,
    long AmountTenths,
    int? RoundId,
    string? IssuerEntity,
    string? ContextJson,
    DateTime AwardedAt,
    int BalanceVersion);

public sealed record Wh40kRewardDeliveryRecord(
    long Id,
    NetUserId UserId,
    string RewardId,
    string EntryId,
    string RewardType,
    string? PrototypeId,
    long Amount,
    string? ContextJson,
    Wh40kRewardDeliveryStatus Status,
    DateTime CreatedAt,
    DateTime? DeliveredAt,
    int AttemptCount,
    DateTime? LastAttemptAt);

public sealed record Wh40kPartyMemberRecord(
    NetUserId UserId,
    DateTime JoinedAt);

public sealed record Wh40kPartyRecord(
    Guid Id,
    NetUserId LeaderUserId,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    long Revision,
    IReadOnlyList<Wh40kPartyMemberRecord> Members);

public sealed record Wh40kPartyMutationResult(
    Wh40kPartyMutationStatus Status,
    Wh40kPartyRecord? Party)
{
    public bool IsSuccess => Status == Wh40kPartyMutationStatus.Success;
}

public sealed record Wh40kPartyInvitation(
    Guid Id,
    Guid PartyId,
    NetUserId LeaderUserId,
    NetUserId TargetUserId,
    DateTime ExpiresAt);

public sealed record Wh40kPartyInvitationResult(
    Wh40kPartyInvitationStatus Status,
    Wh40kPartyRecord? Party = null,
    Wh40kPartyInvitation? Invitation = null)
{
    public bool IsSuccess => Status == Wh40kPartyInvitationStatus.Success;
}
