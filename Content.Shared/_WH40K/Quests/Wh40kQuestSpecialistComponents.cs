using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Quests;

[RegisterComponent]
public sealed partial class Wh40kQuestWorkstationComponent : Component
{
    [DataField(required: true)] public Wh40kQuestObjectiveKind Kind;
    [DataField] public EntProtoId? SourceItem;
    [DataField] public string? CaseKey;
    [DataField] public EntProtoId? Evidence;
    [DataField] public float Delay = 3;
    public readonly HashSet<Guid> CompletedQuests = new();
}

[RegisterComponent]
public sealed partial class Wh40kQuestSubjectComponent : Component
{
    [DataField] public string? CaseKey;
    public Guid QuestId;
    public Guid Token = Guid.NewGuid();
    public Guid Generation;
}

[RegisterComponent]
public sealed partial class Wh40kQuestSubjectSourceComponent : Component
{
    [DataField(required: true)] public EntProtoId Prototype;
    [DataField] public string? CaseKey;
}

[Serializable, NetSerializable]
public sealed partial class Wh40kQuestWorkDoAfterEvent : SimpleDoAfterEvent;

[RegisterComponent]
public sealed partial class Wh40kQuestAccountAccessComponent : Component
{
    public readonly HashSet<ProtoId<Content.Shared.Access.AccessLevelPrototype>> Tags = new();
}
