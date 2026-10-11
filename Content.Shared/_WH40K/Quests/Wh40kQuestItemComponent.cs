namespace Content.Shared._WH40K.Quests;

/// <summary>Runtime objective provenance, separate from an NPC's protected personal gear.</summary>
[RegisterComponent]
public sealed partial class Wh40kQuestItemComponent : Component
{
    // Runtime provenance is assigned by the authoritative item adapter, never authored in YAML.
    // Wh40kQuestItemStateAdapter preserves these values across inventory snapshots.
    [ViewVariables] public Guid QuestId;
    [ViewVariables] public string ObjectiveId = string.Empty;
}
