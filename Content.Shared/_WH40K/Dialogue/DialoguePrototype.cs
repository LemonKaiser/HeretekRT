using Robust.Shared.Localization;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Content.Shared.Access;

namespace Content.Shared._WH40K.Dialogue;

[Prototype("wh40kDialogue")]
public sealed partial class DialoguePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("typewriterCps")]
    public float TypewriterCps = 30f;

    [DataField("scene")]
    public DialogueScenePrototype Scene = new();

    [DataField("repeatable")]
    public bool Repeatable = true;

    /// <summary>
    /// Personal dialogues may run for several players at once. Shared-world dialogues reserve the
    /// NPC exclusively because their actions can change its physical state for everyone.
    /// </summary>
    [DataField("interactionMode")]
    public DialogueInteractionMode InteractionMode = DialogueInteractionMode.SharedWorld;

    /// <summary>
    /// Presentation-only cast for this dialogue. The built-in <c>initiator</c> and <c>npc</c> ids remain
    /// available to every dialogue for backwards compatibility.
    /// </summary>
    [DataField("participants")]
    public List<DialogueParticipantPrototype> Participants = new();

    [DataField("startActions")]
    public List<DialogueActionPrototype> StartActions = new();

    [DataField("completeActions")]
    public List<DialogueActionPrototype> CompleteActions = new();

    [DataField("steps", required: true)]
    public List<DialogueStep> Steps = new();
}

[DataDefinition]
public sealed partial class DialogueScenePrototype
{
    [DataField("hideHud")]
    public bool HideHud = true;

    [DataField("allowCancel")]
    public bool AllowCancel = true;

    [DataField("resumeMode")]
    public DialogueResumeMode ResumeMode = DialogueResumeMode.Continue;

    [DataField("dimOpacity")]
    public float DimOpacity = 0.38f;

    [DataField("windowWidth")]
    public float WindowWidth = 1200f;

    [DataField("windowMinHeight")]
    public float WindowMinHeight = 200f;

    [DataField("windowMaxHeight")]
    public float WindowMaxHeight = 300f;

    [DataField("windowAnchor")]
    public DialogueWindowAnchor WindowAnchor = DialogueWindowAnchor.BottomCenter;

    [DataField("windowMargin")]
    public float WindowMargin = 48f;

    [DataField("showActors")]
    public bool ShowActors = true;

    [DataField("initiatorSide")]
    public DialogueActorSide InitiatorSide = DialogueActorSide.Left;

    [DataField("npcSide")]
    public DialogueActorSide NpcSide = DialogueActorSide.Right;

    [DataField("dimInactiveActors")]
    public bool DimInactiveActors = true;

    [DataField("inactiveActorOpacity")]
    public float InactiveActorOpacity = 0.76f;

    [DataField("actorScale")]
    public float ActorScale = 20f;

    [DataField("actorWidth")]
    public float ActorWidth = 540f;

    [DataField("actorHeight")]
    public float ActorHeight = 1000f;

    [DataField("actorGap")]
    public float ActorGap = 360f;

    [DataField("actorOverlap")]
    public float ActorOverlap = 475f;

    [DataField("actorWindowOverlap")]
    public float ActorWindowOverlap = 40f;

    [DataField("actorStageOffsetY")]
    public float ActorStageOffsetY;

    [DataField("leftActorAlignmentX")]
    public float LeftActorAlignmentX = 0.5f;

    [DataField("rightActorAlignmentX")]
    public float RightActorAlignmentX = 0.5f;

    [DataField("leftActorOffsetX")]
    public float LeftActorOffsetX = -350f;

    [DataField("leftActorOffsetY")]
    public float LeftActorOffsetY = 200f;

    [DataField("rightActorOffsetX")]
    public float RightActorOffsetX = 350f;

    [DataField("rightActorOffsetY")]
    public float RightActorOffsetY = 200f;

    [DataField("speakerFontSize")]
    public int SpeakerFontSize = 22;

    [DataField("bodyFontSize")]
    public int BodyFontSize = 18;

    [DataField("continueFontSize")]
    public int ContinueFontSize = 18;

    [DataField("duckBackgroundMusic")]
    public bool DuckBackgroundMusic = true;

    [DataField("backgroundMusicDuckGain")]
    public float BackgroundMusicDuckGain = 0.08f;

    [DataField("music")]
    public DialogueMusicCuePrototype? Music;
}

[DataDefinition]
public sealed partial class DialogueStep
{
    [DataField("id")]
    public string? Id;

    [DataField("type")]
    public DialogueStepType Type = DialogueStepType.Line;

    [DataField("speaker", required: true)]
    public DialogueSpeaker Speaker;

    /// <summary>
    /// Optional named participant that speaks this line. When omitted, the legacy <see cref="Speaker"/>
    /// field resolves to <c>initiator</c> or <c>npc</c>.
    /// </summary>
    [DataField("speakerId")]
    public string? SpeakerId;

    [DataField("lineType")]
    public DialogueLineType LineType = DialogueLineType.Speech;

    [DataField("text", required: true)]
    public LocId Text = default!;

    [DataField("textArgs")]
    public List<DialogueLocArgumentPrototype> TextArgs = new();

    /// <summary>
    /// Expression applied to the current speaker for this line.
    /// </summary>
    [DataField("expression")]
    public string? Expression;

    /// <summary>
    /// Expressions for any cast members. Values apply only to the current line, making every step
    /// self-contained and safe to resume after a reconnect.
    /// </summary>
    [DataField("expressions")]
    public Dictionary<string, string> Expressions = new(StringComparer.Ordinal);

    /// <summary>
    /// Cast members presented in the two portrait slots for this line. The dialogue may have more than
    /// two named participants; scenes select the pair relevant to each line.
    /// </summary>
    [DataField("leftActor")]
    public string? LeftActor;

    [DataField("rightActor")]
    public string? RightActor;

    [DataField("nextStep")]
    public string? NextStep;

    [DataField("sceneState")]
    public DialogueStepSceneStatePrototype? SceneState;

    [DataField("autoAdvanceAfter")]
    public float? AutoAdvanceAfter;

    [DataField("choices")]
    public List<DialogueChoiceOptionPrototype> Choices = new();

    [DataField("actions")]
    public List<DialogueActionPrototype> Actions = new();

    [DataField("sound")]
    public SoundSpecifier? Sound;

    [DataField("voice")]
    public SoundSpecifier? Voice;

    [DataField("music")]
    public DialogueMusicCuePrototype? Music;
}

[DataDefinition]
public sealed partial class DialogueChoiceOptionPrototype
{
    [DataField("text", required: true)]
    public LocId Text = default!;

    [DataField("textArgs")]
    public List<DialogueLocArgumentPrototype> TextArgs = new();

    [DataField("nextStep")]
    public string? NextStep;

    /// <summary>
    /// Optional dialogue branch to open after this option's response steps finish.
    /// The current dialogue stays open, so the transition does not unlock either participant.
    /// </summary>
    [DataField("nextDialogue")]
    public ProtoId<DialoguePrototype>? NextDialogue;

    /// <summary>
    /// Conditions required for this option to be shown and accepted.
    /// </summary>
    [DataField("conditions")]
    public List<DialogueConditionPrototype> Conditions = new();

    [DataField("actions")]
    public List<DialogueActionPrototype> Actions = new();

    [DataField("responseSteps")]
    public List<DialogueStep> ResponseSteps = new();

    /// <summary>
    /// Optional lines shown when one of this choice's transactional actions cannot be completed.
    /// The dialogue returns to this choice after these lines unless a failure line explicitly jumps elsewhere.
    /// Without a failure branch, the server closes the dialogue instead of advancing it.
    /// </summary>
    [DataField("failureResponseSteps")]
    public List<DialogueStep> FailureResponseSteps = new();
}

[DataDefinition]
public sealed partial class DialogueActionPrototype
{
    [DataField("type", required: true)]
    public DialogueActionType Type;

    [DataField("speaker")]
    public DialogueSpeaker Speaker = DialogueSpeaker.Npc;

    [DataField("targetSpeaker")]
    public DialogueSpeaker TargetSpeaker = DialogueSpeaker.Initiator;

    [DataField("prototype")]
    public EntProtoId? Prototype;

    /// <summary>
    /// Source used by item-taking actions. It deliberately does not recurse into bags or other storage containers.
    /// </summary>
    [DataField("source")]
    public DialogueItemSource Source = DialogueItemSource.Hands;

    /// <summary>
    /// Selects the player's ID card to be modified by access actions.
    /// </summary>
    [DataField("accessCardSource")]
    public DialogueAccessCardSource AccessCardSource = DialogueAccessCardSource.Auto;

    /// <summary>
    /// Exact access levels to add to or remove from the selected ID card.
    /// </summary>
    [DataField("accesses")]
    public List<ProtoId<AccessLevelPrototype>> Accesses = new();

    [DataField("message")]
    public LocId Message = default!;

    [DataField("messageArgs")]
    public List<DialogueLocArgumentPrototype> MessageArgs = new();

    [DataField("flag")]
    public string? Flag;

    [DataField("counter")]
    public string? Counter;

    [DataField("value")]
    public bool Value = true;

    [DataField("amount")]
    public int Amount = 1;

    /// <summary>
    /// Runs this action only when the immediately preceding action in the same list succeeded.
    /// This is primarily useful for coupling rewards, such as counting an item only after it was placed in a hand.
    /// </summary>
    [DataField("onlyIfPreviousActionSucceeded")]
    public bool OnlyIfPreviousActionSucceeded;

    [DataField("offsetX")]
    public float OffsetX;

    [DataField("offsetY")]
    public float OffsetY;

    [DataField("range")]
    public float Range = 0.2f;

    [DataField("directMove")]
    public bool DirectMove;

    [DataField("inRangeMaxSpeed")]
    public float? InRangeMaxSpeed;

    [DataField("degrees")]
    public float Degrees;

    [DataField("hideChat")]
    public bool HideChat;
}

/// <summary>
/// A named member of a dialogue cast. Participants affect only the presentation: gameplay actions still
/// operate on the initiating player and the interactable NPC.
/// </summary>
[DataDefinition]
public sealed partial class DialogueParticipantPrototype
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    /// <summary>
    /// Uses a live entity as the portrait source. Leave unset for a static portrait-only participant,
    /// such as a narrator, radio voice, or an off-screen character.
    /// </summary>
    [DataField("source")]
    public DialogueParticipantSource? Source;

    [DataField("name")]
    public LocId? Name;

    [DataField("nameArgs")]
    public List<DialogueLocArgumentPrototype> NameArgs = new();

    /// <summary>
    /// Entity prototype rendered locally when this participant has no live source or no expression override.
    /// </summary>
    [DataField("portrait")]
    public EntProtoId? Portrait;

    /// <summary>
    /// Maps expression ids to local portrait entity prototypes. Each prototype is presentation-only and is
    /// spawned in client nullspace for the overlay.
    /// </summary>
    [DataField("expressions")]
    public Dictionary<string, EntProtoId> Expressions = new(StringComparer.Ordinal);

    [DataField("side")]
    public DialogueActorSide Side = DialogueActorSide.Hidden;
}

/// <summary>
/// Declarative arguments for Fluent dialogue strings. Values are resolved server-authoritatively and the
/// client localizes the final line in its own language.
/// </summary>
[DataDefinition]
public sealed partial class DialogueLocArgumentPrototype
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("type")]
    public DialogueLocArgumentType Type = DialogueLocArgumentType.Literal;

    [DataField("value")]
    public string? Value;

    [DataField("amount")]
    public int Amount;

    [DataField("counter")]
    public string? Counter;

    [DataField("participant")]
    public string? Participant;

    [DataField("prototype")]
    public EntProtoId? Prototype;
}

[DataDefinition]
public sealed partial class DialogueConditionPrototype
{
    [DataField("type", required: true)]
    public DialogueConditionType Type;

    [DataField("flag")]
    public string? Flag;

    [DataField("counter")]
    public string? Counter;

    [DataField("dialogue")]
    public ProtoId<DialoguePrototype>? Dialogue;

    [DataField("prototype")]
    public EntProtoId? Prototype;

    [DataField("source")]
    public DialogueItemSource Source = DialogueItemSource.Hands;

    [DataField("value")]
    public bool Value = true;

    [DataField("amount")]
    public int Amount = 1;
}

[DataDefinition]
public sealed partial class DialogueMusicCuePrototype
{
    [DataField("sound")]
    public SoundSpecifier? Sound;

    [DataField("fadeIn")]
    public float FadeIn = 1.5f;

    [DataField("fadeOut")]
    public float FadeOut = 1.5f;

    [DataField("stop")]
    public bool Stop;
}

[DataDefinition]
public sealed partial class DialogueStepSceneStatePrototype
{
    [DataField("showWindow")]
    public bool? ShowWindow;

    [DataField("showActors")]
    public bool? ShowActors;

    [DataField("showDim")]
    public bool? ShowDim;
}

public enum DialogueSpeaker : byte
{
    Initiator,
    Npc
}

public enum DialogueParticipantSource : byte
{
    Initiator,
    Target
}

public enum DialogueLocArgumentType : byte
{
    Literal,
    Number,
    Counter,
    BankBalance,
    ParticipantName,
    PrototypeName
}

public enum DialogueStepType : byte
{
    Line,
    Choice
}

public enum DialogueLineType : byte
{
    Speech,
    Thought,
    Narration
}

public enum DialogueActionType : byte
{
    GiveItem,
    SendChat,
    OpenStore,
    SetFlag,
    AddCounter,
    SetCounter,
    CloseDialogue,
    MoveSpeakerToSpeaker,
    StopSpeakerMovement,
    SleepSpeakerAi,
    WakeSpeakerAi,
    RotateSpeakerRelative,
    FaceSpeaker,
    TakeItem,
    DebitBankAccount,
    CreditBankAccount,
    AddAccess,
    RemoveAccess
}

public enum DialogueConditionType : byte
{
    Flag,
    CounterAtLeast,
    CounterAtMost,
    CounterEquals,
    DialogueCompleted,
    StoreAvailable,
    ItemCountAtLeast,
    BankBalanceAtLeast
}

/// <summary>
/// Item locations that dialogue actions may inspect or consume. Equipment slots and storage contents are
/// intentionally different concepts in SS14: the latter are not searched by this system.
/// </summary>
public enum DialogueItemSource : byte
{
    Hands,
    Equipped
}

/// <summary>
/// Locations from which dialogue access actions may select an ID card.
/// </summary>
public enum DialogueAccessCardSource : byte
{
    /// <summary>
    /// A direct ID card in a hand first, then the card inside the equipped PDA.
    /// </summary>
    Auto,
    /// <summary>
    /// The card inside the PDA equipped in the ID slot, or a PDA held in a hand.
    /// </summary>
    Pda,
    /// <summary>
    /// Only a direct ID card held in either hand.
    /// </summary>
    Hands
}

public enum DialogueWindowAnchor : byte
{
    BottomLeft,
    BottomCenter,
    BottomRight
}

public enum DialogueActorSide : byte
{
    Hidden,
    Left,
    Right
}

public enum DialogueResumeMode : byte
{
    Continue,
    Restart
}

public enum DialogueInteractionMode : byte
{
    Personal,
    SharedWorld
}
