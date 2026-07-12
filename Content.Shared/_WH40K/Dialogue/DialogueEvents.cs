using Robust.Shared.Localization;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Dialogue;

[Serializable, NetSerializable]
public sealed class DialogueTextData
{
    public LocId Text { get; }
    public List<DialogueLocArgumentData> Arguments { get; }

    public DialogueTextData(LocId text, List<DialogueLocArgumentData> arguments)
    {
        Text = text;
        Arguments = arguments;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueLocArgumentData
{
    public string Id { get; }
    public string? Text { get; }
    public int? Number { get; }
    public string? Prototype { get; }

    public DialogueLocArgumentData(string id, string text)
    {
        Id = id;
        Text = text;
    }

    public DialogueLocArgumentData(string id, int number)
    {
        Id = id;
        Number = number;
    }

    public DialogueLocArgumentData(string id, string prototype, bool isPrototype)
    {
        Id = id;
        Prototype = prototype;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueActorData
{
    public string Id { get; }
    public NetEntity? Entity { get; }
    public string? PortraitPrototype { get; }

    public DialogueActorData(string id, NetEntity? entity, string? portraitPrototype)
    {
        Id = id;
        Entity = entity;
        PortraitPrototype = portraitPrototype;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueActorStageData
{
    public DialogueActorData? Left { get; }
    public DialogueActorData? Right { get; }
    public DialogueActorSide ActiveSide { get; }

    public DialogueActorStageData(DialogueActorData? left, DialogueActorData? right, DialogueActorSide activeSide)
    {
        Left = left;
        Right = right;
        ActiveSide = activeSide;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueLineData
{
    public DialogueSpeaker Speaker { get; }
    public DialogueTextData? SpeakerName { get; }
    public DialogueLineType LineType { get; }
    public DialogueTextData Text { get; }
    public DialogueActorStageData Actors { get; }
    public DialogueSceneStateData SceneState { get; }
    public float? AutoAdvanceAfter { get; }
    public int StepIndex { get; }
    public int StepCount { get; }
    public List<DialogueChoiceOptionData> Choices { get; }
    public DialogueSoundCueData? Sound { get; }
    public DialogueSoundCueData? Voice { get; }
    public DialogueMusicCueData? Music { get; }

    public DialogueLineData(
        DialogueSpeaker speaker,
        DialogueTextData? speakerName,
        DialogueLineType lineType,
        DialogueTextData text,
        DialogueActorStageData actors,
        DialogueSceneStateData sceneState,
        float? autoAdvanceAfter,
        int stepIndex,
        int stepCount,
        List<DialogueChoiceOptionData> choices,
        DialogueSoundCueData? sound,
        DialogueSoundCueData? voice,
        DialogueMusicCueData? music)
    {
        Speaker = speaker;
        SpeakerName = speakerName;
        LineType = lineType;
        Text = text;
        Actors = actors;
        SceneState = sceneState;
        AutoAdvanceAfter = autoAdvanceAfter;
        StepIndex = stepIndex;
        StepCount = stepCount;
        Choices = choices;
        Sound = sound;
        Voice = voice;
        Music = music;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueSceneStateData
{
    public bool ShowWindow { get; }
    public bool ShowActors { get; }
    public bool ShowDim { get; }

    public DialogueSceneStateData(bool showWindow, bool showActors, bool showDim)
    {
        ShowWindow = showWindow;
        ShowActors = showActors;
        ShowDim = showDim;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueChoiceOptionData
{
    public DialogueTextData Text { get; }
    public int ChoiceIndex { get; }

    public DialogueChoiceOptionData(DialogueTextData text, int choiceIndex)
    {
        Text = text;
        ChoiceIndex = choiceIndex;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueSoundCueData
{
    public ResolvedSoundSpecifier Sound { get; }
    public AudioParams Audio { get; }

    public DialogueSoundCueData(ResolvedSoundSpecifier sound, AudioParams audio)
    {
        Sound = sound;
        Audio = audio;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueMusicCueData
{
    public ResolvedSoundSpecifier? Sound { get; }
    public AudioParams Audio { get; }
    public float FadeIn { get; }
    public float FadeOut { get; }
    public bool Stop { get; }

    public DialogueMusicCueData(
        ResolvedSoundSpecifier? sound,
        AudioParams audio,
        float fadeIn,
        float fadeOut,
        bool stop)
    {
        Sound = sound;
        Audio = audio;
        FadeIn = fadeIn;
        FadeOut = fadeOut;
        Stop = stop;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueOpenEvent : EntityEventArgs
{
    public int SessionId { get; }
    public NetEntity Initiator { get; }
    public NetEntity Target { get; }
    public DialogueLineData Line { get; }
    public float TypewriterCps { get; }
    public DialogueSceneData Scene { get; }

    public DialogueOpenEvent(
        int sessionId,
        NetEntity initiator,
        NetEntity target,
        DialogueLineData line,
        float typewriterCps,
        DialogueSceneData scene)
    {
        SessionId = sessionId;
        Initiator = initiator;
        Target = target;
        Line = line;
        TypewriterCps = typewriterCps;
        Scene = scene;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueSceneData
{
    public bool HideHud { get; }
    public bool AllowCancel { get; }
    public float DimOpacity { get; }
    public float WindowWidth { get; }
    public float WindowMinHeight { get; }
    public float WindowMaxHeight { get; }
    public DialogueWindowAnchor WindowAnchor { get; }
    public float WindowMargin { get; }
    public bool ShowActors { get; }
    public DialogueActorSide InitiatorSide { get; }
    public DialogueActorSide NpcSide { get; }
    public bool DimInactiveActors { get; }
    public float InactiveActorOpacity { get; }
    public float ActorScale { get; }
    public float ActorWidth { get; }
    public float ActorHeight { get; }
    public float ActorGap { get; }
    public float ActorOverlap { get; }
    public float ActorWindowOverlap { get; }
    public float ActorStageOffsetY { get; }
    public float LeftActorAlignmentX { get; }
    public float RightActorAlignmentX { get; }
    public float LeftActorOffsetX { get; }
    public float LeftActorOffsetY { get; }
    public float RightActorOffsetX { get; }
    public float RightActorOffsetY { get; }
    public int SpeakerFontSize { get; }
    public int BodyFontSize { get; }
    public int ContinueFontSize { get; }
    public bool DuckBackgroundMusic { get; }
    public float BackgroundMusicDuckGain { get; }
    public DialogueMusicCueData? Music { get; }

    public DialogueSceneData(
        bool hideHud,
        bool allowCancel,
        float dimOpacity,
        float windowWidth,
        float windowMinHeight,
        float windowMaxHeight,
        DialogueWindowAnchor windowAnchor,
        float windowMargin,
        bool showActors,
        DialogueActorSide initiatorSide,
        DialogueActorSide npcSide,
        bool dimInactiveActors,
        float inactiveActorOpacity,
        float actorScale,
        float actorWidth,
        float actorHeight,
        float actorGap,
        float actorOverlap,
        float actorWindowOverlap,
        float actorStageOffsetY,
        float leftActorAlignmentX,
        float rightActorAlignmentX,
        float leftActorOffsetX,
        float leftActorOffsetY,
        float rightActorOffsetX,
        float rightActorOffsetY,
        int speakerFontSize,
        int bodyFontSize,
        int continueFontSize,
        bool duckBackgroundMusic,
        float backgroundMusicDuckGain,
        DialogueMusicCueData? music)
    {
        HideHud = hideHud;
        AllowCancel = allowCancel;
        DimOpacity = dimOpacity;
        WindowWidth = windowWidth;
        WindowMinHeight = windowMinHeight;
        WindowMaxHeight = windowMaxHeight;
        WindowAnchor = windowAnchor;
        WindowMargin = windowMargin;
        ShowActors = showActors;
        InitiatorSide = initiatorSide;
        NpcSide = npcSide;
        DimInactiveActors = dimInactiveActors;
        InactiveActorOpacity = inactiveActorOpacity;
        ActorScale = actorScale;
        ActorWidth = actorWidth;
        ActorHeight = actorHeight;
        ActorGap = actorGap;
        ActorOverlap = actorOverlap;
        ActorWindowOverlap = actorWindowOverlap;
        ActorStageOffsetY = actorStageOffsetY;
        LeftActorAlignmentX = leftActorAlignmentX;
        RightActorAlignmentX = rightActorAlignmentX;
        LeftActorOffsetX = leftActorOffsetX;
        LeftActorOffsetY = leftActorOffsetY;
        RightActorOffsetX = rightActorOffsetX;
        RightActorOffsetY = rightActorOffsetY;
        SpeakerFontSize = speakerFontSize;
        BodyFontSize = bodyFontSize;
        ContinueFontSize = continueFontSize;
        DuckBackgroundMusic = duckBackgroundMusic;
        BackgroundMusicDuckGain = backgroundMusicDuckGain;
        Music = music;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueLineUpdateEvent : EntityEventArgs
{
    public int SessionId { get; }
    public DialogueLineData Line { get; }

    public DialogueLineUpdateEvent(int sessionId, DialogueLineData line)
    {
        SessionId = sessionId;
        Line = line;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueAdvanceRequestEvent : EntityEventArgs
{
    public int SessionId { get; }

    public DialogueAdvanceRequestEvent(int sessionId)
    {
        SessionId = sessionId;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueChoiceRequestEvent : EntityEventArgs
{
    public int SessionId { get; }
    public int ChoiceIndex { get; }

    public DialogueChoiceRequestEvent(int sessionId, int choiceIndex)
    {
        SessionId = sessionId;
        ChoiceIndex = choiceIndex;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueCancelRequestEvent : EntityEventArgs
{
    public int SessionId { get; }

    public DialogueCancelRequestEvent(int sessionId)
    {
        SessionId = sessionId;
    }
}

[Serializable, NetSerializable]
public sealed class DialogueCloseEvent : EntityEventArgs
{
    public int SessionId { get; }

    public DialogueCloseEvent(int sessionId)
    {
        SessionId = sessionId;
    }
}
