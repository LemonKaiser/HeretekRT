using Robust.Shared.Network;

namespace Content.Shared._WH40K.Quests;

public enum Wh40kQuestOwnerScope : byte { Account, Party }
public enum Wh40kQuestOrder : byte { Sequential, AnyOrder }
public enum Wh40kQuestCompletionPolicy : byte { TurnIn, Automatic }
public enum Wh40kQuestRoundPolicy : byte { RebindOrWait, Fail }
public enum Wh40kQuestRepeatPolicy : byte { Once, Repeatable }
public enum Wh40kQuestState : byte { Active, ReadyToTurnIn, Completed, Failed, Abandoned, Expired }
public enum Wh40kQuestBindingState : byte { Bound, WaitingForTarget, WaitingForContent }
public enum Wh40kQuestOfferState : byte { Open, Accepted, Expired, Withdrawn }
public enum Wh40kQuestRewardState : byte { Pending, Dispatching, Delivered }
public enum Wh40kQuestOperationResult : byte
{
    Succeeded, Pending, InvalidContent, UnavailableTarget, NotOwner, StaleRevision,
    CapacityReached, PrerequisiteMissing, NotReady, Expired, PersistenceUnavailable,
}

public enum Wh40kQuestObjectiveKind : byte
{
    KillEntity, CollectItem, DeliverItem, ReachLocation, TalkToNpc, InteractWithEntity,
    CompleteActivity, RecoverActivityObjective, EscortEntity, SurviveDuration, DiscoverLocation,
    RepairEntity, InstallComponent, ScanOrAnalyze, CollectEvidence, InvestigateLocation,
    CaptureAlive, DeliverEntity, RescueAndDeliver, StabilizeTarget, DecodeSignal, ProtectTarget,
    FactionDecision, PayDebtOrFee, ExtractResource,
}

public enum Wh40kQuestTargetKind : byte { Entity, Tag, NpcAnchor, Location, Activity }
public enum Wh40kQuestLocationKind : byte { System, Sector, Grid, PointOfInterest, Coordinates }
public enum Wh40kQuestRewardKind : byte { Experience, Currency, Item, FactionReputation, Flag, Access }

public static class Wh40kQuestObjectiveKinds
{
    public static bool IsSubmission(this Wh40kQuestObjectiveKind kind)
        => kind is Wh40kQuestObjectiveKind.DeliverItem or Wh40kQuestObjectiveKind.CollectEvidence or Wh40kQuestObjectiveKind.PayDebtOrFee;
}

/// <summary>Stable owner identity. A party uses the existing persistent party Guid.</summary>
public readonly record struct Wh40kQuestOwnerKey(Wh40kQuestOwnerScope Scope, Guid Id)
{
    public bool IsValid => Enum.IsDefined(Scope) && Id != Guid.Empty;
    public static Wh40kQuestOwnerKey Account(NetUserId user) => new(Wh40kQuestOwnerScope.Account, user.UserId);
    public static Wh40kQuestOwnerKey Party(Guid party) => new(Wh40kQuestOwnerScope.Party, party);
}

/// <summary>Deduplication identity; a single last-event id cannot reject delayed duplicates.</summary>
public readonly record struct Wh40kQuestEventReceiptKey(Guid QuestId, string ObjectiveId, Guid EventId)
{
    public bool IsValid => QuestId != Guid.Empty && !string.IsNullOrWhiteSpace(ObjectiveId) && EventId != Guid.Empty;
}

/// <summary>Runtime provenance only. Never use an activity's numeric id without its round generation.</summary>
public readonly record struct Wh40kQuestActivityBinding(Guid ServerGeneration, int RoundId, long InstanceId)
{
    public bool IsValid => ServerGeneration != Guid.Empty && RoundId >= 0 && InstanceId > 0;
}
