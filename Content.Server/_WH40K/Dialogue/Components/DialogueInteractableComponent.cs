using Content.Shared._WH40K.Dialogue;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Dialogue.Components;

[RegisterComponent]
public sealed partial class DialogueInteractableComponent : Component
{
    [DataField("dialogue")]
    public ProtoId<DialoguePrototype>? Dialogue;

    /// <summary>
    /// The persistent/default dialogue for this NPC. It is used when no higher-priority entry in
    /// <see cref="Dialogues"/> matches, such as after a one-time introduction is completed.
    /// </summary>
    [DataField("baseDialogue")]
    public ProtoId<DialoguePrototype>? BaseDialogue;

    [DataField("dialogues")]
    public List<DialogueInteractableEntry> Dialogues = new();

    [DataField("interactionRange")]
    public float InteractionRange = 2f;

    [DataField("maxDialogueRange")]
    public float MaxDialogueRange = 12f;

    [DataField("resumeGracePeriod")]
    public float ResumeGracePeriod = 60f;

    [DataField("persistMemory")]
    public bool PersistMemory;

    [DataField("persistentMemoryKey")]
    public string? PersistentMemoryKey;

    /// <summary>
    /// Controls whether every semantic value is retained, or only the explicitly listed values.
    /// </summary>
    [DataField("persistenceMode")]
    public DialogueMemoryPersistenceMode PersistenceMode = DialogueMemoryPersistenceMode.All;

    [DataField("persistentFlags")]
    public HashSet<string> PersistentFlags = new(StringComparer.Ordinal);

    [DataField("persistentCounters")]
    public HashSet<string> PersistentCounters = new(StringComparer.Ordinal);

    [DataField("persistentCompletedDialogues")]
    public HashSet<string> PersistentCompletedDialogues = new(StringComparer.Ordinal);

    [DataField("requireLineOfSight")]
    public bool RequireLineOfSight = true;

    [DataField("autoTrigger")]
    public bool AutoTrigger;

    [DataField("autoTriggerRange")]
    public float AutoTriggerRange = 2f;

    [DataField("verbText")]
    public LocId VerbText = "heretek-dialogue-verb-talk";
}

[DataDefinition]
public sealed partial class DialogueInteractableEntry
{
    [DataField("dialogue")]
    public ProtoId<DialoguePrototype>? Dialogue;

    [DataField("chat")]
    public LocId? Chat;

    [DataField("speaker")]
    public DialogueSpeaker Speaker = DialogueSpeaker.Npc;

    [DataField("actions")]
    public List<DialogueActionPrototype> Actions = new();

    [DataField("cooldown")]
    public float Cooldown;

    [DataField("cooldownKey")]
    public string? CooldownKey;

    [DataField("conditions")]
    public List<DialogueConditionPrototype> Conditions = new();
}

public enum DialogueMemoryPersistenceMode : byte
{
    /// <summary>
    /// Preserve flags, counters, and completed dialogue identifiers. This keeps the previous behaviour.
    /// </summary>
    All,

    /// <summary>
    /// Preserve only entries named in the persistent flags, counters, and completed-dialogues lists.
    /// </summary>
    Selected
}
