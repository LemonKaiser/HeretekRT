namespace Content.Shared._WH40K.Quests;

/// <summary>Authored local interaction whose successful use can satisfy an approved selector.</summary>
[RegisterComponent]
public sealed partial class Wh40kQuestInteractionTargetComponent : Component;

/// <summary>Stable grid/POI key; runtime entities and maps are never persistent selectors.</summary>
[RegisterComponent]
public sealed partial class Wh40kQuestLocationComponent : Component
{
    [DataField(required: true)] public string Key = string.Empty;
    [DataField] public Wh40kQuestLocationKind Kind = Wh40kQuestLocationKind.PointOfInterest;
}

/// <summary>Persisted provenance assigned by the server before reserving item units.</summary>
[RegisterComponent]
public sealed partial class Wh40kQuestReservationComponent : Component
{
    [ViewVariables] public Guid Submission;
    [ViewVariables] public Guid UserId;
    [ViewVariables] public int Units;
    [ViewVariables] public int OriginalCount;
    [ViewVariables] public bool Merchant;
}
