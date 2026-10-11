using System.Numerics;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.Activities.Prototypes;
using Content.Shared._WH40K.Dialogue;
using Content.Shared._WH40K.Npcs;
using Content.Shared._WH40K.Progression;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Access;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Quests;

[Prototype]
public sealed partial class Wh40kQuestPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public int Version = 1;
    // Enable only reviewed definitions whose authoritative adapters are implemented.
    [DataField] public bool Enabled;
    [DataField(required: true)] public LocId Title;
    [DataField(required: true)] public LocId Description;
    [DataField] public Wh40kQuestOwnerScope OwnerScope;
    [DataField] public Wh40kQuestOrder ObjectiveOrder = Wh40kQuestOrder.AnyOrder;
    [DataField] public Wh40kQuestCompletionPolicy CompletionPolicy = Wh40kQuestCompletionPolicy.TurnIn;
    [DataField] public Wh40kQuestRoundPolicy CrossRoundPolicy = Wh40kQuestRoundPolicy.RebindOrWait;
    [DataField] public Wh40kQuestRepeatPolicy RepeatPolicy;
    [DataField] public TimeSpan? Lifetime;
    [DataField] public TimeSpan Cooldown;
    [DataField] public List<ProtoId<Wh40kQuestPrototype>> Prerequisites = [];
    [DataField(required: true)] public List<Wh40kQuestObjectiveDefinition> Objectives = [];
    [DataField] public List<Wh40kQuestRewardDefinition> Rewards = [];
    [DataField] public ProtoId<Wh40kNpcAnchorPrototype>? Issuer;
    [DataField] public string? BranchGroup;
    [DataField] public string? BranchChoice;
}

[Prototype]
public sealed partial class Wh40kQuestPoolPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<Wh40kNpcAnchorPrototype> Source;
    [DataField] public int OfferCount = 3;
    [DataField] public int ActiveLimit = 5;
    [DataField] public int HistoryLimit = 6;
    [DataField] public TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    [DataField(required: true)] public List<Wh40kQuestPoolEntry> Entries = [];
}

[DataDefinition]
public sealed partial class Wh40kQuestPoolEntry
{
    [DataField(required: true)] public ProtoId<Wh40kQuestPrototype> Quest;
    [DataField] public int Weight = 1;
}

[DataDefinition]
public sealed partial class Wh40kQuestObjectiveDefinition
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public Wh40kQuestObjectiveKind Kind;
    [DataField(required: true)] public LocId Text;
    [DataField] public int RequiredCount = 1;
    [DataField] public bool DistinctTargets;
    [DataField] public Wh40kParticipationMode Participation = Wh40kParticipationMode.Radius;
    [DataField(required: true)] public Wh40kQuestTargetDefinition Target = new();
}

[DataDefinition]
public sealed partial class Wh40kQuestTargetDefinition
{
    [DataField(required: true)] public Wh40kQuestTargetKind Kind;
    [DataField] public EntProtoId? Entity;
    [DataField] public ProtoId<TagPrototype>? Tag;
    // Tag selectors are confined to an explicit prototype catalogue, never arbitrary matching entities.
    [DataField] public List<EntProtoId> ApprovedPrototypes = [];
    [DataField] public List<ProtoId<Wh40kNpcAnchorPrototype>> Anchors = [];
    [DataField] public ProtoId<DialoguePrototype>? Dialogue;
    [DataField] public Wh40kQuestLocationKind LocationKind;
    [DataField] public ProtoId<KoronusSystemPrototype>? System;
    [DataField] public ProtoId<KoronusSectorPrototype>? Sector;
    [DataField] public string? LocationKey;
    [DataField] public Vector2? Position;
    [DataField] public float Radius = 3f;
    [DataField] public ProtoId<KoronusActivityTemplatePrototype>? Activity;
    [DataField] public KoronusActivityTerminalReason? TerminalReason;
    [DataField] public ProtoId<Wh40kNpcAnchorPrototype>? Destination;
    [DataField] public EntProtoId? SourceItem;
    [DataField] public string? CaseKey;
    [DataField] public int DurationSeconds = 30;
}

[DataDefinition]
public sealed partial class Wh40kQuestRewardDefinition
{
    [DataField(required: true)] public string EntryId = string.Empty;
    [DataField(required: true)] public Wh40kQuestRewardKind Kind;
    [DataField] public int Amount = 1;
    [DataField] public EntProtoId? Item;
    [DataField] public ProtoId<Wh40kExperienceSourcePrototype>? ExperienceSource;
    [DataField] public ProtoId<Wh40kNpcFactionPrototype>? Faction;
    [DataField] public string? Flag;
    [DataField] public ProtoId<AccessLevelPrototype>? Access;
}
