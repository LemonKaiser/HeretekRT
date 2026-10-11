using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Quests;

[RegisterComponent]
public sealed partial class Wh40kQuestUiComponent : Component
{
    // No pool means a remote, read-only journal. Mutations require the actual issuer.
    [DataField] public ProtoId<Wh40kQuestPoolPrototype>? Pool;
}

[Serializable, NetSerializable]
public enum Wh40kQuestUiKey : byte { Key }
[Serializable, NetSerializable]
public enum Wh40kQuestUiAction : byte { Refresh, Accept, TurnIn, Abandon, Submit }

[Serializable, NetSerializable]
public sealed class Wh40kQuestUiRequest : BoundUserInterfaceMessage
{
    public Wh40kQuestUiAction Action;
    public Guid Operation;
    public Guid Target;
    public long Revision;
    public string Objective;
    public Wh40kQuestUiRequest(Wh40kQuestUiAction action, Guid operation, Guid target = default, long revision = 0, string objective = "")
    { Action = action; Operation = operation; Target = target; Revision = revision; Objective = objective; }
}

// Sent to one authenticated BUI actor, never placed in globally replicated UiState.
[Serializable, NetSerializable]
public sealed class Wh40kQuestUiSnapshot : BoundUserInterfaceMessage
{
    public List<Wh40kQuestUiRow> Offers;
    public List<Wh40kQuestUiRow> Active;
    public List<Wh40kQuestUiRow> History;
    public Wh40kQuestOperationResult Result;
    public bool Pending;
    public Wh40kQuestUiSnapshot(List<Wh40kQuestUiRow> offers, List<Wh40kQuestUiRow> active, List<Wh40kQuestUiRow> history,
        Wh40kQuestOperationResult result, bool pending = false)
    { Offers = offers; Active = active; History = history; Result = result; Pending = pending; }
}

[Serializable, NetSerializable]
public sealed class Wh40kQuestUiRow
{
    public Guid Id;
    public long Revision;
    public string Title;
    public string Description;
    public string? Issuer;
    public string? Deadline;
    public string? OfferExpiry;
    public Wh40kQuestOwnerScope OwnerScope;
    public Wh40kQuestState State;
    public Wh40kQuestBindingState Binding;
    public List<Wh40kQuestUiObjective> Objectives;
    public List<Wh40kQuestUiReward> Rewards;
    public bool CanAct;
    public Wh40kQuestUiRow(Guid id, long revision, string title, string description, string? issuer, string? deadline,
        Wh40kQuestOwnerScope ownerScope, Wh40kQuestState state, Wh40kQuestBindingState binding,
        List<Wh40kQuestUiObjective> objectives, List<Wh40kQuestUiReward> rewards, bool canAct)
    { Id = id; Revision = revision; Title = title; Description = description; Issuer = issuer; Deadline = deadline;
      OwnerScope = ownerScope; State = state; Binding = binding; Objectives = objectives; Rewards = rewards; CanAct = canAct; }
}

[Serializable, NetSerializable]
public sealed class Wh40kQuestUiObjective
{
    public string Id;
    public string Text;
    public Wh40kQuestObjectiveKind Kind;
    public int Count;
    public int Required;
    public Wh40kQuestUiObjective(string id, string text, Wh40kQuestObjectiveKind kind, int count, int required)
    { Id = id; Text = text; Kind = kind; Count = count; Required = required; }
}

[Serializable, NetSerializable]
public sealed class Wh40kQuestUiReward
{
    public Wh40kQuestRewardKind Kind;
    public int Amount;
    public string? Item;
    public Wh40kQuestRewardState State;
    public Wh40kQuestUiReward(Wh40kQuestRewardKind kind, int amount, string? item, Wh40kQuestRewardState state)
    { Kind = kind; Amount = amount; Item = item; State = state; }
}
