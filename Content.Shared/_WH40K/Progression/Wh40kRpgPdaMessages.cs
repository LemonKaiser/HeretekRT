using Content.Shared.CartridgeLoader;
using Content.Shared.Eui;
using Content.Shared._WH40K.CharacterCreation;
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
