using Content.Shared.CartridgeLoader;
using Content.Shared.Eui;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Progression;

public static class Wh40kRpgPdaConstants
{
    public const int MaximumCkeyLength = 64;
}

[Serializable, NetSerializable]
public enum Wh40kPlayerUiOperationStatus : byte
{
    None,
    Success,
    AccountUnavailable,
    InvalidCharacteristic,
    InvalidCount,
    RevisionMismatch,
    InsufficientDevelopmentPoints,
}

[Serializable, NetSerializable]
public enum Wh40kPartyUiOperationStatus : byte
{
    None,
    Success,
    AccountUnavailable,
    InvalidTarget,
    InvitesDisabled,
    AlreadyInParty,
    NotInParty,
    PartyNotFound,
    PartyExpired,
    NotLeader,
    PartyFull,
    RevisionMismatch,
    InvitationNotFound,
    InvitationExpired,
}

[Serializable, NetSerializable]
public sealed class Wh40kCharacteristicUiSnapshot
{
    public readonly Wh40kCharacteristic Characteristic;
    public readonly int CreationAllocation;
    public readonly int Homeworld;
    public readonly int Origin;
    public readonly int Class;
    public readonly int LevelPurchases;
    public readonly int Final;

    public Wh40kCharacteristicUiSnapshot(
        Wh40kCharacteristic characteristic,
        int creationAllocation,
        int homeworld,
        int origin,
        int characterClass,
        int levelPurchases,
        int final)
    {
        Characteristic = characteristic;
        CreationAllocation = creationAllocation;
        Homeworld = homeworld;
        Origin = origin;
        Class = characterClass;
        LevelPurchases = levelPurchases;
        Final = final;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kPlayerProgressUiSnapshot
{
    public readonly string CharacterName;
    public readonly int Level;
    public readonly long TotalExperienceTenths;
    public readonly long CurrentLevelExperienceTenths;
    public readonly long ExperienceToNextLevelTenths;
    public readonly long CurrentLevelSpanTenths;
    public readonly int UnspentDevelopmentPoints;
    public readonly long Revision;
    public readonly string HomeworldId;
    public readonly string OriginId;
    public readonly string ClassId;
    public readonly List<Wh40kCharacteristicUiSnapshot> Characteristics;

    public Wh40kPlayerProgressUiSnapshot(
        string characterName,
        int level,
        long totalExperienceTenths,
        long currentLevelExperienceTenths,
        long experienceToNextLevelTenths,
        long currentLevelSpanTenths,
        int unspentDevelopmentPoints,
        long revision,
        string homeworldId,
        string originId,
        string classId,
        List<Wh40kCharacteristicUiSnapshot> characteristics)
    {
        CharacterName = characterName;
        Level = level;
        TotalExperienceTenths = totalExperienceTenths;
        CurrentLevelExperienceTenths = currentLevelExperienceTenths;
        ExperienceToNextLevelTenths = experienceToNextLevelTenths;
        CurrentLevelSpanTenths = currentLevelSpanTenths;
        UnspentDevelopmentPoints = unspentDevelopmentPoints;
        Revision = revision;
        HomeworldId = homeworldId;
        OriginId = originId;
        ClassId = classId;
        Characteristics = characteristics;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kPlayerSnapshotBuiMessage : BoundUserInterfaceMessage
{
    public readonly Wh40kPlayerUiOperationStatus Status;
    public readonly Wh40kPlayerProgressUiSnapshot? Snapshot;

    public Wh40kPlayerSnapshotBuiMessage(
        Wh40kPlayerUiOperationStatus status,
        Wh40kPlayerProgressUiSnapshot? snapshot)
    {
        Status = status;
        Snapshot = snapshot;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kCharacteristicAllocation
{
    public readonly Wh40kCharacteristic Characteristic;
    public readonly int Count;

    public Wh40kCharacteristicAllocation(
        Wh40kCharacteristic characteristic,
        int count)
    {
        Characteristic = characteristic;
        Count = count;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kSpendCharacteristicsUiMessage : CartridgeMessageEvent
{
    public readonly long ExpectedRevision;
    public readonly List<Wh40kCharacteristicAllocation> Allocations;

    public Wh40kSpendCharacteristicsUiMessage(
        long expectedRevision,
        List<Wh40kCharacteristicAllocation> allocations)
    {
        ExpectedRevision = expectedRevision;
        Allocations = allocations;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kPartyMemberUiSnapshot
{
    public readonly Guid UserId;
    public readonly string Ckey;
    public readonly bool IsLeader;
    public readonly bool IsSelf;
    public readonly bool IsOnline;

    public Wh40kPartyMemberUiSnapshot(
        Guid userId,
        string ckey,
        bool isLeader,
        bool isSelf,
        bool isOnline)
    {
        UserId = userId;
        Ckey = ckey;
        IsLeader = isLeader;
        IsSelf = isSelf;
        IsOnline = isOnline;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kPartyUiSnapshot
{
    public readonly Guid? PartyId;
    public readonly bool IsLeader;
    public readonly bool InvitesAllowed;
    public readonly long ExpiresAtUtcTicks;
    public readonly List<Wh40kPartyMemberUiSnapshot> Members;

    public Wh40kPartyUiSnapshot(
        Guid? partyId,
        bool isLeader,
        bool invitesAllowed,
        long expiresAtUtcTicks,
        List<Wh40kPartyMemberUiSnapshot> members)
    {
        PartyId = partyId;
        IsLeader = isLeader;
        InvitesAllowed = invitesAllowed;
        ExpiresAtUtcTicks = expiresAtUtcTicks;
        Members = members;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kPartySnapshotBuiMessage : BoundUserInterfaceMessage
{
    public readonly Wh40kPartyUiOperationStatus Status;
    public readonly Wh40kPartyUiSnapshot? Snapshot;

    public Wh40kPartySnapshotBuiMessage(
        Wh40kPartyUiOperationStatus status,
        Wh40kPartyUiSnapshot? snapshot)
    {
        Status = status;
        Snapshot = snapshot;
    }
}

[Serializable, NetSerializable]
public enum Wh40kPartyUiAction : byte
{
    Refresh,
    Invite,
    Leave,
    Kick,
    SetInvitesAllowed,
}

[Serializable, NetSerializable]
public sealed class Wh40kPartyUiMessage : CartridgeMessageEvent
{
    public readonly Wh40kPartyUiAction Action;
    public readonly string Ckey;
    public readonly Guid TargetUserId;
    public readonly bool AllowInvites;

    public Wh40kPartyUiMessage(
        Wh40kPartyUiAction action,
        string ckey = "",
        Guid targetUserId = default,
        bool allowInvites = true)
    {
        Action = action;
        Ckey = ckey;
        TargetUserId = targetUserId;
        AllowInvites = allowInvites;
    }
}

[Serializable, NetSerializable]
public enum Wh40kFactionUiOperationStatus : byte
{
    None,
    Success,
    AccountUnavailable,
    InvalidFaction,
    AlreadyMember,
    AlreadyRequested,
    RequestNotFound,
    NotAuthorized,
    CannotManageLeader,
    RequestSubmitted,
    RequestCancelled,
    RequestAccepted,
    RequestRejected,
    MemberRemoved,
    OfficerPromoted,
    OfficerDemoted,
    LeftFaction,
    Failed,
    ProfileSaved,
    InvalidProfile,
    ProfileConflict,
    InvalidPayout,
    BankUnavailable,
    InsufficientFactionFunds,
    RecipientUnavailable,
    RecipientBalanceLimit,
    PayoutSent,
    PayoutQueued,
    InvalidSocialText,
    SocialCooldown,
    TransmissionUnavailable,
    MessageSent,
    DistressSent,
    AnnouncementPublished,
    AnnouncementRemoved,
    InvalidRelation,
    RelationConflict,
    RelationSaved,
    RelationCooldown,
}

[Serializable, NetSerializable]
public sealed class Wh40kFactionOptionUiSnapshot
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Description;
    public readonly string Color;
    public readonly FactionRole? Role;
    public readonly bool HasRequest;
    public readonly string? LeaderName;
    public readonly int MemberCount;
    public readonly int OfficerCount;
    public readonly FactionProfile Profile;

    public Wh40kFactionOptionUiSnapshot(string id, string name, string description, string color,
        FactionRole? role = null, bool hasRequest = false, string? leaderName = null, int memberCount = 0, int officerCount = 0,
        FactionProfile? profile = null)
    {
        Id = id;
        Name = name;
        Description = description;
        Color = color;
        Role = role;
        HasRequest = hasRequest;
        LeaderName = leaderName;
        MemberCount = memberCount;
        OfficerCount = officerCount;
        Profile = profile ?? new FactionProfile();
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kFactionMemberUiSnapshot
{
    public readonly Guid UserId;
    public readonly string Ckey;
    public readonly FactionRole Role;
    public readonly bool IsSelf;
    public readonly bool IsOnline;

    public Wh40kFactionMemberUiSnapshot(Guid userId, string ckey, FactionRole role, bool isSelf, bool isOnline)
    {
        UserId = userId;
        Ckey = ckey;
        Role = role;
        IsSelf = isSelf;
        IsOnline = isOnline;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kFactionRequestUiSnapshot
{
    public readonly Guid UserId;
    public readonly string Ckey;

    public Wh40kFactionRequestUiSnapshot(Guid userId, string ckey)
    {
        UserId = userId;
        Ckey = ckey;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kFactionUiSnapshot
{
    public readonly int RelationCooldownSeconds;
    public readonly FactionSocialSnapshot? Social;
    public readonly FactionRelation[] Relations;
    public readonly FactionBankSnapshot? Treasury;
    public readonly string? FactionId;
    public readonly string? FactionName;
    public readonly string? FactionDescription;
    public readonly string? FactionColor;
    public readonly FactionRole? ViewerRole;
    public readonly List<Wh40kFactionOptionUiSnapshot> AvailableFactions;
    public readonly List<Wh40kFactionMemberUiSnapshot> Members;
    public readonly List<Wh40kFactionRequestUiSnapshot> Requests;
    public readonly bool AccountAvailable;

    public Wh40kFactionUiSnapshot(
        string? factionId,
        string? factionName,
        string? factionDescription,
        string? factionColor,
        FactionRole? viewerRole,
        List<Wh40kFactionOptionUiSnapshot> availableFactions,
        List<Wh40kFactionMemberUiSnapshot> members,
        List<Wh40kFactionRequestUiSnapshot> requests,
        bool accountAvailable = true, FactionBankSnapshot? treasury = null,
        FactionSocialSnapshot? social = null, FactionRelation[]? relations = null, int relationCooldownSeconds = 0)
    {
        FactionId = factionId;
        FactionName = factionName;
        FactionDescription = factionDescription;
        FactionColor = factionColor;
        ViewerRole = viewerRole;
        AvailableFactions = availableFactions;
        Members = members;
        Requests = requests;
        AccountAvailable = accountAvailable;
        Treasury = treasury;
        Social = social;
        Relations = relations ?? [];
        RelationCooldownSeconds = relationCooldownSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kFactionSnapshotBuiMessage : BoundUserInterfaceMessage
{
    public readonly Wh40kFactionUiOperationStatus Status;
    public readonly Wh40kFactionUiSnapshot? Snapshot;

    public Wh40kFactionSnapshotBuiMessage(Wh40kFactionUiOperationStatus status, Wh40kFactionUiSnapshot? snapshot)
    {
        Status = status;
        Snapshot = snapshot;
    }
}

[Serializable, NetSerializable]
public enum Wh40kFactionUiAction : byte
{
    Refresh,
    RequestJoin,
    AcceptRequest,
    RejectRequest,
    RemoveMember,
    PromoteOfficer,
    DemoteOfficer,
    CancelRequest,
    Leave,
    SaveProfile,
    Payout,
    SendMessage,
    SendDistress,
    PublishAnnouncement,
    RemoveAnnouncement,
    SetRelation,
}

[Serializable, NetSerializable]
public sealed class Wh40kFactionUiMessage : CartridgeMessageEvent
{
    public readonly FactionSocialRequest? Social;
    public readonly Wh40kFactionUiAction Action;
    public readonly string FactionId;
    public readonly Guid TargetUserId;
    public readonly FactionProfile? Profile;
    public readonly FactionBankPayout? Payout;

    public Wh40kFactionUiMessage(Wh40kFactionUiAction action, string factionId = "", Guid targetUserId = default,
        FactionProfile? profile = null, FactionBankPayout? payout = null, FactionSocialRequest? social = null)
    {
        Action = action;
        FactionId = factionId;
        TargetUserId = targetUserId;
        Profile = profile;
        Payout = payout;
        Social = social;
    }
}

[Serializable, NetSerializable]
public enum Wh40kPartyInvitationChoice : byte
{
    Decline,
    Accept,
}

[Serializable, NetSerializable]
public sealed class Wh40kPartyInvitationEuiState : EuiStateBase
{
    public readonly string LeaderCkey;
    public readonly long ExpiresAtUtcTicks;

    public Wh40kPartyInvitationEuiState(string leaderCkey, long expiresAtUtcTicks)
    {
        LeaderCkey = leaderCkey;
        ExpiresAtUtcTicks = expiresAtUtcTicks;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kPartyInvitationChoiceMessage : EuiMessageBase
{
    public readonly Wh40kPartyInvitationChoice Choice;

    public Wh40kPartyInvitationChoiceMessage(Wh40kPartyInvitationChoice choice)
    {
        Choice = choice;
    }
}
