using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.Quests;

namespace Content.Server._WH40K.Quests;

/// <summary>Immutable, versioned persistence payload. Contains no runtime entity/map or session identities.</summary>
public sealed record Wh40kQuestContractSnapshot(
    int SchemaVersion,
    string DefinitionId,
    int DefinitionVersion,
    Wh40kQuestOwnerKey Owner,
    Wh40kQuestOrder Order,
    Wh40kQuestCompletionPolicy Completion,
    Wh40kQuestRoundPolicy RoundPolicy,
    Wh40kQuestRepeatPolicy Repeat,
    TimeSpan Cooldown,
    string Title,
    string Description,
    string? Issuer,
    DateTimeOffset AcceptedAt,
    DateTimeOffset? Deadline,
    int GeneratorVersion,
    long Seed,
    ImmutableArray<Wh40kQuestObjectiveSnapshot> Objectives,
    ImmutableArray<Wh40kQuestRewardSnapshot> Rewards,
    ImmutableArray<string> Prerequisites,
    ImmutableArray<Guid> Recipients)
{
    public string? BranchGroup { get; init; }
    public string? BranchChoice { get; init; }
    public int RewardAmount(Wh40kQuestRewardSnapshot reward, Guid recipient)
    {
        if (!Recipients.Contains(recipient)) return 0;
        if (Owner.Scope != Wh40kQuestOwnerScope.Party || reward.Kind is Wh40kQuestRewardKind.Flag or Wh40kQuestRewardKind.Access) return reward.Amount;
        var recipients = Recipients.OrderBy(x => x).ToArray();
        return reward.Amount / recipients.Length + (Array.IndexOf(recipients, recipient) < reward.Amount % recipients.Length ? 1 : 0);
    }
    // Call only after resolving generated parameters and validating the contract.
    public static Wh40kQuestContractSnapshot Freeze(Wh40kQuestPrototype definition,
        Wh40kQuestOwnerKey owner, DateTimeOffset acceptedAt, int generatorVersion, long seed)
    {
        if (!owner.IsValid || acceptedAt.Offset != TimeSpan.Zero || generatorVersion <= 0)
            throw new ArgumentException("Snapshot requires a stable owner, UTC timestamp and generator version.");

        return new Wh40kQuestContractSnapshot(1, definition.ID, definition.Version, owner,
            definition.ObjectiveOrder, definition.CompletionPolicy, definition.CrossRoundPolicy,
            definition.RepeatPolicy, definition.Cooldown, definition.Title.Id, definition.Description.Id,
            definition.Issuer?.Id, acceptedAt, definition.Lifetime is {} lifetime ? acceptedAt + lifetime : null,
            generatorVersion, seed,
            definition.Objectives.Select(objective => new Wh40kQuestObjectiveSnapshot(
                objective.Id, objective.Kind, objective.Text.Id, objective.RequiredCount, objective.DistinctTargets,
                objective.Participation, Wh40kQuestTargetSnapshot.Freeze(objective.Target))).ToImmutableArray(),
            definition.Rewards.Select(reward => new Wh40kQuestRewardSnapshot(reward.EntryId, reward.Kind,
                reward.Amount, reward.Item?.Id, reward.ExperienceSource?.Id, reward.Faction?.Id,
                reward.Flag, reward.Access?.Id)).ToImmutableArray(),
            definition.Prerequisites.Select(id => id.Id).ToImmutableArray(),
            owner.Scope == Wh40kQuestOwnerScope.Account ? [owner.Id] : [])
        { BranchGroup = definition.BranchGroup, BranchChoice = definition.BranchChoice };
    }
}

public sealed record Wh40kQuestObjectiveSnapshot(string Id, Wh40kQuestObjectiveKind Kind, string Text,
    int RequiredCount, bool DistinctTargets, Content.Shared._WH40K.Progression.Wh40kParticipationMode Participation,
    Wh40kQuestTargetSnapshot Target);

public sealed record Wh40kQuestTargetSnapshot(Wh40kQuestTargetKind Kind, string? Entity, string? Tag,
    ImmutableArray<string> ApprovedPrototypes, ImmutableArray<string> Anchors, string? Dialogue,
    Wh40kQuestLocationKind LocationKind, string? System, string? Sector, string? LocationKey,
    Vector2? Position, float Radius, string? Activity, KoronusActivityTerminalReason? TerminalReason)
{
    public string? Destination { get; init; }
    public string? SourceItem { get; init; }
    public string? CaseKey { get; init; }
    public int DurationSeconds { get; init; } = 30;
    public Wh40kQuestTargetSnapshot SubmissionTarget() => Kind == Wh40kQuestTargetKind.NpcAnchor
        ? this with { Kind = Wh40kQuestTargetKind.Entity, Entity = SourceItem ?? "SpaceCash", Anchors = [], Dialogue = null }
        : this;
    public static Wh40kQuestTargetSnapshot Freeze(Wh40kQuestTargetDefinition target) => new(target.Kind,
        target.Entity?.Id, target.Tag?.Id, target.ApprovedPrototypes.Select(p => p.Id).ToImmutableArray(),
        target.Anchors.Select(p => p.Id).ToImmutableArray(), target.Dialogue?.Id, target.LocationKind,
        target.System?.Id, target.Sector?.Id, target.LocationKey, target.Position, target.Radius,
        target.Activity?.Id, target.TerminalReason)
    { Destination = target.Destination?.Id, SourceItem = target.SourceItem?.Id, CaseKey = target.CaseKey, DurationSeconds = target.DurationSeconds };
}

public sealed record Wh40kQuestRewardSnapshot(string EntryId, Wh40kQuestRewardKind Kind, int Amount,
    string? Item, string? ExperienceSource, string? Faction, string? Flag, string? Access);
