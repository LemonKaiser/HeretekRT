using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ClassProgression;

[Serializable, NetSerializable]
public enum Wh40kClassSkillPurchaseStatus : byte
{
    Success,
    AccountNotFound,
    SkillNotFound,
    ClassMismatch,
    ContentUnavailable,
    InsufficientLevel,
    MissingPrerequisite,
    InsufficientPoints,
    AlreadyPurchased,
    RevisionMismatch,
}

[Serializable, NetSerializable]
public enum Wh40kClassSkillNodeState : byte
{
    Purchased,
    Available,
    ContentUnavailable,
    InsufficientLevel,
    MissingPrerequisite,
    InsufficientPoints,
}

[Serializable, NetSerializable]
public sealed class Wh40kClassSkillNodeSnapshot
{
    public readonly string SkillId;
    public readonly Wh40kClassSkillNodeState State;

    public Wh40kClassSkillNodeSnapshot(string skillId, Wh40kClassSkillNodeState state)
    {
        SkillId = skillId;
        State = state;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kClassSpecializationSnapshot
{
    public readonly string SpecializationId;
    public readonly List<Wh40kClassSkillNodeSnapshot> Skills;

    public Wh40kClassSpecializationSnapshot(
        string specializationId,
        List<Wh40kClassSkillNodeSnapshot> skills)
    {
        SpecializationId = specializationId;
        Skills = skills;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kClassTreeSnapshot
{
    public readonly long Revision;
    public readonly int TreeVersion;
    public readonly string ClassId;
    public readonly int CurrentLevel;
    public readonly long CurrentLevelExperienceTenths;
    public readonly long ExperienceToNextLevelTenths;
    public readonly long CurrentLevelSpanTenths;
    public readonly int EarnedSkillPoints;
    public readonly int AvailableSkillPoints;
    public readonly List<string> PurchasedSkillIds;
    public readonly List<Wh40kClassSpecializationSnapshot> Specializations;

    public Wh40kClassTreeSnapshot(
        long revision,
        int treeVersion,
        string classId,
        int currentLevel,
        long currentLevelExperienceTenths,
        long experienceToNextLevelTenths,
        long currentLevelSpanTenths,
        int earnedSkillPoints,
        int availableSkillPoints,
        List<string> purchasedSkillIds,
        List<Wh40kClassSpecializationSnapshot> specializations)
    {
        Revision = revision;
        TreeVersion = treeVersion;
        ClassId = classId;
        CurrentLevel = currentLevel;
        CurrentLevelExperienceTenths = currentLevelExperienceTenths;
        ExperienceToNextLevelTenths = experienceToNextLevelTenths;
        CurrentLevelSpanTenths = currentLevelSpanTenths;
        EarnedSkillPoints = earnedSkillPoints;
        AvailableSkillPoints = availableSkillPoints;
        PurchasedSkillIds = purchasedSkillIds;
        Specializations = specializations;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kClassTreeRequestEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class Wh40kClassSkillPurchaseRequestEvent : EntityEventArgs
{
    public readonly string SkillId;
    public readonly long ExpectedRevision;

    public Wh40kClassSkillPurchaseRequestEvent(string skillId, long expectedRevision)
    {
        SkillId = skillId;
        ExpectedRevision = expectedRevision;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kClassTreeSnapshotEvent : EntityEventArgs
{
    public readonly Wh40kClassSkillPurchaseStatus Status;
    public readonly Wh40kClassTreeSnapshot? Snapshot;

    public Wh40kClassTreeSnapshotEvent(
        Wh40kClassSkillPurchaseStatus status,
        Wh40kClassTreeSnapshot? snapshot)
    {
        Status = status;
        Snapshot = snapshot;
    }
}

[Serializable, NetSerializable]
public enum Wh40kClassUiOperationStatus : byte
{
    None,
    PurchaseSucceeded,
    AccountUnavailable,
    SkillNotFound,
    ClassMismatch,
    ContentUnavailable,
    InsufficientLevel,
    MissingPrerequisite,
    InsufficientPoints,
    AlreadyPurchased,
    RevisionMismatch,
}

[Serializable, NetSerializable]
public enum Wh40kClassBonusActivationState : byte
{
    Active,
    MissingEquipment,
    ContentUnavailable,
}

/// <summary>
/// Server-resolved presentation of one purchased effect. Entity ids and supporting items never leave the server.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kClassBonusUiSnapshot
{
    public readonly string SkillId;
    public readonly string EffectId;
    public readonly Wh40kClassSkillEffectKind Kind;
    public readonly Wh40kClassBonusActivationState State;
    public readonly float AppliedRarityPercent;

    public Wh40kClassBonusUiSnapshot(
        string skillId,
        string effectId,
        Wh40kClassSkillEffectKind kind,
        Wh40kClassBonusActivationState state,
        float appliedRarityPercent)
    {
        SkillId = skillId;
        EffectId = effectId;
        Kind = kind;
        State = state;
        AppliedRarityPercent = appliedRarityPercent;
    }
}

/// <summary>
/// The single private, server-authoritative model shared by the PDA summary and detached tree window.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kClassUiSnapshot
{
    public readonly Wh40kClassTreeSnapshot Tree;
    public readonly List<string> RecentPurchasedSkillIds;
    public readonly List<Wh40kClassBonusUiSnapshot> Bonuses;

    public Wh40kClassUiSnapshot(
        Wh40kClassTreeSnapshot tree,
        List<string> recentPurchasedSkillIds,
        List<Wh40kClassBonusUiSnapshot> bonuses)
    {
        Tree = tree;
        RecentPurchasedSkillIds = recentPurchasedSkillIds;
        Bonuses = bonuses;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kClassSnapshotBuiMessage : BoundUserInterfaceMessage
{
    public readonly Wh40kClassUiOperationStatus Status;
    public readonly Wh40kClassUiSnapshot? Snapshot;

    public Wh40kClassSnapshotBuiMessage(
        Wh40kClassUiOperationStatus status,
        Wh40kClassUiSnapshot? snapshot)
    {
        Status = status;
        Snapshot = snapshot;
    }
}

[Serializable, NetSerializable]
public enum Wh40kClassUiAction : byte
{
    Refresh,
    Purchase,
}

/// <summary>
/// The client can only request a refresh or submit the selected skill with the last acknowledged revision.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kClassUiMessage : CartridgeMessageEvent
{
    public readonly Wh40kClassUiAction Action;
    public readonly string SkillId;
    public readonly long ExpectedRevision;

    public Wh40kClassUiMessage(
        Wh40kClassUiAction action,
        string skillId = "",
        long expectedRevision = 0)
    {
        Action = action;
        SkillId = skillId;
        ExpectedRevision = expectedRevision;
    }
}
