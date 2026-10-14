using System.Linq;
using Content.Client._WH40K.Dialogue.UI;
using Content.Client.Audio;
using Content.Client._WH40K.Audio;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Robust.Client.Audio;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Content.Shared._WH40K.Dialogue;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client._WH40K.Dialogue;

public sealed class DialogueUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    private const float DialogueMusicSidechainTail = 0.2f;

    [Dependency] private IEntityNetworkManager _net = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [UISystemDependency] private readonly AudioSystem? _audio = default!;
    [UISystemDependency] private readonly ContentAudioSystem? _contentAudio = default!;
    [UISystemDependency] private readonly Wh40kMusicOverrideSystem? _music = default!;
    [UISystemDependency] private readonly DialoguePreviewSystem? _preview = default!;

    private DialogueOverlay? _overlay;
    private int? _sessionId;
    private int _lineRevision;
    private float _defaultTypewriterCps;
    private float _typewriterCps;
    private string _currentText = string.Empty;
    private int _revealedCharacters;
    private float _revealAccumulator;
    private bool _lineFullyRevealed;
    private bool _lineHasChoices;
    private bool _allowSkip = true;
    private bool _awaitingAdvance;
    private bool _awaitingChoiceResponse;
    private bool _awaitingCancel;
    private float? _autoAdvanceAfter;
    private float _autoAdvanceRemaining;
    private bool _hideHud;
    private bool _duckBackgroundMusic;
    private float _backgroundMusicDuckGain = 0.08f;
    private DialogueSceneData? _pendingScene;
    private DialogueLineData? _pendingLine;
    private EntityUid? _voiceStream;
    private ResolvedSoundSpecifier? _currentMusic;
    private float _musicLineDuckRemaining;
    private readonly List<EntityUid> _actorPreviewEntities = new();
    private float _syncRequestDelay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<DialogueOpenEvent>(OnDialogueOpened);
        SubscribeNetworkEvent<DialogueLineUpdateEvent>(OnDialogueUpdated);
        SubscribeNetworkEvent<DialogueCloseEvent>(OnDialogueClosed);
        SubscribeNetworkEvent<DialogueOperationEvent>(OnOperation);

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
    }

    public void OnStateEntered(GameplayState state)
    {
        EnsureOverlay();
        ReattachOverlay();
        PresentPendingDialogue();
    }

    private void EnsureOverlay()
    {
        if (_overlay != null)
            return;

        _overlay = new DialogueOverlay();
        _overlay.AdvancePressed += OnAdvancePressed;
        _overlay.TextAreaPressed += OnTextAreaPressed;
        _overlay.ChoiceSelected += OnChoiceSelected;
        _overlay.CancelPressed += OnCancelPressed;
    }

    public void OnStateExited(GameplayState state)
    {
        ResetDialogueState(immediateMusicStop: true);

        if (_overlay != null)
        {
            _overlay.AdvancePressed -= OnAdvancePressed;
            _overlay.TextAreaPressed -= OnTextAreaPressed;
            _overlay.ChoiceSelected -= OnChoiceSelected;
            _overlay.CancelPressed -= OnCancelPressed;
            _overlay.Orphan();
            _overlay.Dispose();
            _overlay = null;
        }
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (UIManager.ActiveScreen is InGameScreen { VisibleInTree: true })
        {
            if (_sessionId == null && _players.LocalEntity is { } player && EntityManager.HasComponent<DialogueInputLockComponent>(player))
            {
                _syncRequestDelay -= args.DeltaSeconds;
                if (_syncRequestDelay <= 0f)
                {
                    _net.SendSystemNetworkMessage(new DialogueSyncRequestEvent());
                    _syncRequestDelay = 1f;
                }
            }
            else
            {
                _syncRequestDelay = 0f;
            }

            if (_sessionId != null && (_overlay == null || !_overlay.VisibleInTree))
            {
                EnsureOverlay();
                ReattachOverlay();
                PresentPendingDialogue();
            }
        }

        UpdateDialogueMusicDuck(args.DeltaSeconds);

        if (_sessionId != null
            && _lineFullyRevealed
            && !_lineHasChoices
            && !_awaitingAdvance
            && !_awaitingChoiceResponse
            && _autoAdvanceAfter != null)
        {
            _autoAdvanceRemaining -= args.DeltaSeconds;

            if (_autoAdvanceRemaining <= 0f)
            {
                _autoAdvanceAfter = null;
                RequestAdvance(fromAutoAdvance: true);
            }
        }

        if (_overlay == null || _sessionId == null || _lineFullyRevealed)
            return;

        if (_typewriterCps <= 0f)
        {
            RevealFullLine();
            return;
        }

        _revealAccumulator += args.DeltaSeconds * _typewriterCps;
        var revealCount = Math.Clamp((int) MathF.Floor(_revealAccumulator), 0, _currentText.Length);

        if (revealCount == _revealedCharacters)
            return;

        _revealedCharacters = revealCount;
        _overlay.SetBodyText(_currentText[.._revealedCharacters], _currentText);

        if (_revealedCharacters >= _currentText.Length)
            FinishReveal();
    }

    private void OnScreenLoad()
    {
        ReattachOverlay();
        UpdateHudVisibility();
    }

    private void OnDialogueOpened(DialogueOpenEvent ev, EntitySessionEventArgs args)
    {
        var preservePresentation = _sessionId == ev.SessionId && _overlay != null;

        if (!preservePresentation)
            ResetDialogueState();

        if (_duckBackgroundMusic && !ev.Scene.DuckBackgroundMusic)
            _contentAudio?.PopMusicDuck();
        else if (!_duckBackgroundMusic && ev.Scene.DuckBackgroundMusic)
            _contentAudio?.PushMusicDuck(ev.Scene.BackgroundMusicDuckGain);

        _sessionId = ev.SessionId;
        _defaultTypewriterCps = ev.TypewriterCps;
        _typewriterCps = ev.TypewriterCps;
        _hideHud = ev.Scene.HideHud;
        _allowSkip = ev.Scene.AllowSkip;
        _duckBackgroundMusic = ev.Scene.DuckBackgroundMusic;
        _backgroundMusicDuckGain = ev.Scene.BackgroundMusicDuckGain;
        _pendingScene = ev.Scene;
        _pendingLine = ev.Line;

        if (_overlay == null)
            return;

        PresentDialogue(ev.Scene, ev.Line, playEntrance: !preservePresentation);
    }

    private void OnDialogueUpdated(DialogueLineUpdateEvent ev, EntitySessionEventArgs args)
    {
        if (_sessionId != ev.SessionId)
            return;

        _pendingLine = ev.Line;
        if (_overlay == null)
            return;

        ApplyLine(ev.Line);
    }

    private void OnDialogueClosed(DialogueCloseEvent ev, EntitySessionEventArgs args)
    {
        if (_sessionId != ev.SessionId)
            return;

        ResetDialogueState();
    }

    private void PresentPendingDialogue()
    {
        if (_pendingScene == null || _pendingLine == null)
            return;

        PresentDialogue(_pendingScene, _pendingLine, playEntrance: true);
    }

    private void PresentDialogue(DialogueSceneData scene, DialogueLineData line, bool playEntrance)
    {
        if (_overlay == null)
            return;

        ReattachOverlay();
        _overlay.ApplyScene(scene, playEntrance);

        ApplyMusicCue(scene.Music);
        ApplyLine(line, playActorEntrance: playEntrance);
        // Build the line, choices and their final layout while the overlay is
        // still hidden. Showing it earlier allowed an opening frame with the
        // controls' default positions before the next UI arrange pass.
        _overlay.Visible = true;
        UpdateHudVisibility();
    }

    private void ApplyLine(DialogueLineData line, bool playActorEntrance = false)
    {
        _lineRevision = line.Revision;
        _overlay?.SetOperationStatus(string.Empty);
        if (_overlay == null)
            return;

        _currentText = FormatText(line.Text);
        _revealedCharacters = 0;
        _revealAccumulator = 0f;
        _lineFullyRevealed = false;
        _lineHasChoices = line.Choices.Count > 0;
        _awaitingAdvance = false;
        _awaitingChoiceResponse = false;
        _awaitingCancel = false;
        _autoAdvanceAfter = line.AutoAdvanceAfter;
        _autoAdvanceRemaining = MathF.Max(line.AutoAdvanceAfter ?? 0f, 0f);

        StopVoiceTrack();
        PlayLineSound(line.Sound);
        var voiceDuration = PlayVoiceTrack(line.Voice);
        ApplyMusicCue(line.Music);
        _overlay.ApplySceneState(line.SceneState);
        _overlay.SetLineType(line.LineType);
        UpdateActors(line.Actors, playActorEntrance);
        _overlay.SetSpeakerName(line.SpeakerName == null ? string.Empty : FormatText(line.SpeakerName));
        _overlay.SetChoices(line.Choices, FormatText);
        _overlay.SetChoicesVisible(false);
        _overlay.SetChoicesDisabled(false);
        _overlay.SetCancelDisabled(false);
        // The line is skipped by clicking its text area.  Keeping a button here used
        // to visually compete with choices and made the primary interaction less natural.
        _overlay.SetContinueText(Loc.GetString("heretek-dialogue-ui-continue"));
        _overlay.SetContinueVisible(false);
        _currentText = _overlay.PrepareBodyText(_currentText);
        _typewriterCps = ResolveLineTypewriterCps(voiceDuration);

        if (_currentText.Length == 0 || _typewriterCps <= 0f)
        {
            RevealFullLine();
            return;
        }

        _overlay.SetBodyText(string.Empty, _currentText);
    }

    private void OnOperation(DialogueOperationEvent ev, EntitySessionEventArgs args)
    {
        if (_sessionId != ev.SessionId || _overlay == null) return;
        _overlay.SetChoicesDisabled(ev.Pending);
        _overlay.SetOperationStatus(Loc.GetString("wh40k-quest-result-" + ev.Result.ToString().ToLowerInvariant()));
    }

    private void OnAdvancePressed()
    {
        RequestAdvance(fromAutoAdvance: false);
    }

    private void RequestAdvance(bool fromAutoAdvance)
    {
        if (_overlay == null || _sessionId == null)
            return;

        if (!_lineFullyRevealed)
        {
            if (!_allowSkip)
                return;

            RevealFullLine(skipVoiceTrack: true);
            return;
        }

        if (!_allowSkip && !fromAutoAdvance)
            return;

        // Timed cutscene lines may be revealed early, but their transition is
        // owned by the timer.  Previously this sent a request the server had to
        // reject, leaving the client permanently waiting for a reply.
        if (!fromAutoAdvance && _autoAdvanceAfter != null)
            return;

        if (_lineHasChoices || _awaitingAdvance || _awaitingChoiceResponse || _awaitingCancel)
            return;

        _awaitingAdvance = true;
        _overlay.SetContinueVisible(false);
        _net.SendSystemNetworkMessage(new DialogueAdvanceRequestEvent(_sessionId.Value, fromAutoAdvance, _lineRevision));
    }

    private void OnTextAreaPressed()
    {
        // The frame is the primary dialogue control: first click completes the
        // typewriter effect, the next one advances an ordinary line.
        OnAdvancePressed();
    }

    private void OnChoiceSelected(int choiceIndex)
    {
        if (_overlay == null || _sessionId == null || !_lineFullyRevealed || !_lineHasChoices || _awaitingChoiceResponse || _awaitingCancel)
            return;

        _awaitingChoiceResponse = true;
        _overlay.SetChoicesDisabled(true);
        _net.SendSystemNetworkMessage(new DialogueChoiceRequestEvent(_sessionId.Value, choiceIndex, _lineRevision));
    }

    private void OnCancelPressed()
    {
        if (_overlay == null || _sessionId == null || _awaitingCancel)
            return;

        _awaitingCancel = true;
        _overlay.SetCancelDisabled(true);
        _overlay.SetChoicesDisabled(true);
        _overlay.SetContinueVisible(false);
        _net.SendSystemNetworkMessage(new DialogueCancelRequestEvent(_sessionId.Value));
    }

    private void RevealFullLine(bool skipVoiceTrack = false)
    {
        if (_overlay == null)
            return;

        if (skipVoiceTrack)
            StopVoiceTrack();

        _revealedCharacters = _currentText.Length;
        _overlay.SetBodyText(_currentText);
        FinishReveal();
    }

    private void FinishReveal()
    {
        _lineFullyRevealed = true;

        if (_lineHasChoices)
        {
            _overlay?.SetChoicesVisible(true);
            _overlay?.SetContinueVisible(false);
            return;
        }

        _overlay?.SetContinueText(Loc.GetString("heretek-dialogue-ui-continue"));
        _overlay?.SetContinueVisible(_allowSkip && _autoAdvanceAfter == null);
    }

    private void ResetDialogueState(bool immediateMusicStop = false)
    {
        _syncRequestDelay = 0f;
        if (_hideHud && UIManager.ActiveScreen is InGameScreen activeScreen)
            activeScreen.SetHudVisible(true);

        _sessionId = null;
        _defaultTypewriterCps = 0f;
        _typewriterCps = 0f;
        _currentText = string.Empty;
        _revealedCharacters = 0;
        _revealAccumulator = 0f;
        _lineFullyRevealed = false;
        _lineHasChoices = false;
        _allowSkip = true;
        _awaitingAdvance = false;
        _awaitingChoiceResponse = false;
        _awaitingCancel = false;
        _autoAdvanceAfter = null;
        _autoAdvanceRemaining = 0f;
        _hideHud = false;
        _pendingScene = null;
        _pendingLine = null;
        if (_duckBackgroundMusic)
            _contentAudio?.PopMusicDuck();

        _duckBackgroundMusic = false;
        _backgroundMusicDuckGain = 0.08f;
        _musicLineDuckRemaining = 0f;
        StopVoiceTrack();
        StopDialogueMusic(immediateMusicStop);
        DeleteActorPreviews();

        if (_overlay == null)
            return;

        _overlay.Visible = false;
        _overlay.ClearActors();
        _overlay.SetSpeakerName(string.Empty);
        _overlay.SetLineType(DialogueLineType.Speech);
        _overlay.SetBodyText(string.Empty);
        _overlay.ClearChoices();
        _overlay.SetCancelVisible(false);
        _overlay.SetContinueText(Loc.GetString("heretek-dialogue-ui-continue"));
        _overlay.SetContinueVisible(false);
        _overlay.ApplySceneState(new DialogueSceneStateData(true, true, true));
        _overlay.SetDimOpacity(0.38f);
    }

    private void UpdateHudVisibility()
    {
        if (UIManager.ActiveScreen is not InGameScreen activeScreen)
            return;

        activeScreen.SetHudVisible(_sessionId == null || !_hideHud);
    }

    private void ReattachOverlay()
    {
        if (_overlay == null)
            return;

        if (UIManager.ActiveScreen is InGameScreen activeScreen)
        {
            activeScreen.AttachDialogueOverlay(_overlay);
            return;
        }

        if (_overlay.Parent != UIManager.PopupRoot)
        {
            _overlay.Orphan();
            UIManager.PopupRoot.AddChild(_overlay);
            LayoutContainer.SetAnchorPreset(_overlay, LayoutContainer.LayoutPreset.Wide);
        }

        _overlay.SetPositionLast();
    }

    private void PlayLineSound(DialogueSoundCueData? cue)
    {
        if (cue == null || _audio == null)
            return;

        _audio.PlayGlobal(cue.Sound, Filter.Local(), false, cue.Audio);
        DuckDialogueMusicForLine(cue.Sound);
    }

    private float? PlayVoiceTrack(DialogueSoundCueData? cue)
    {
        if (cue == null || _audio == null)
            return null;

        var playResult = _audio.PlayGlobal(cue.Sound, Filter.Local(), false, cue.Audio);
        if (playResult == null)
            return null;

        _voiceStream = playResult.Value.Entity;
        ApplyDialogueMusicMix();
        return MathF.Max((float) _audio.GetAudioLength(cue.Sound).TotalSeconds, 0.01f);
    }

    private void ApplyMusicCue(DialogueMusicCueData? cue)
    {
        if (cue == null || _music == null)
            return;

        if (cue.Stop || cue.Sound == null || ResolvedSoundSpecifier.IsNullOrEmpty(cue.Sound))
        {
            StopDialogueMusic(false, cue.FadeOut);
            return;
        }

        if (Equals(_currentMusic, cue.Sound))
            return;

        _currentMusic = cue.Sound;
        _music.Request(this, cue.Sound, cue.Audio, 100, cue.FadeIn, cue.FadeOut);
        ApplyDialogueMusicMix();
    }

    private void StopDialogueMusic(bool immediate, float? fadeOut = null)
    {
        _music?.Release(this, immediate, fadeOut);
        _currentMusic = null;
        _musicLineDuckRemaining = 0f;
        _music?.SetVoiceDuck(this, false);
    }

    private void StopVoiceTrack()
    {
        if (_voiceStream == null)
            return;

        _audio?.Stop(_voiceStream);
        _voiceStream = null;
        ApplyDialogueMusicMix();
    }

    private void DuckDialogueMusicForLine(ResolvedSoundSpecifier cue)
    {
        if (_audio == null)
            return;

        var duration = MathF.Max((float) _audio.GetAudioLength(cue).TotalSeconds, 0.05f);
        _musicLineDuckRemaining = MathF.Max(_musicLineDuckRemaining, duration + DialogueMusicSidechainTail);
        ApplyDialogueMusicMix();
    }

    private void UpdateDialogueMusicDuck(float frameTime)
    {
        UpdateVoiceTrackState();

        if (_musicLineDuckRemaining <= 0f)
            return;

        _musicLineDuckRemaining = MathF.Max(0f, _musicLineDuckRemaining - frameTime);
        ApplyDialogueMusicMix();

        if (_musicLineDuckRemaining <= 0f)
            ApplyDialogueMusicMix();
    }

    private void ApplyDialogueMusicMix()
    {
        _music?.SetVoiceDuck(this, _musicLineDuckRemaining > 0f || _voiceStream != null);
    }

    private void UpdateVoiceTrackState()
    {
        if (_voiceStream == null)
            return;

        if (EntityManager.EntityExists(_voiceStream.Value)
            && EntityManager.TryGetComponent(_voiceStream.Value, out AudioComponent? _))
        {
            return;
        }

        _voiceStream = null;
        ApplyDialogueMusicMix();
    }

    private float ResolveLineTypewriterCps(float? voiceDuration)
    {
        if (voiceDuration == null || voiceDuration <= 0f || _currentText.Length == 0)
            return _defaultTypewriterCps;

        return _currentText.Length / voiceDuration.Value;
    }

    private void UpdateActors(DialogueActorStageData actors, bool playEntrance)
    {
        if (_overlay == null)
            return;

        DeleteActorPreviews();
        var left = ResolveDialogueActor(actors.Left);
        var right = ResolveDialogueActor(actors.Right);
        _overlay.SetActors(left, right, playEntrance);
        _overlay.SetActiveSpeaker(actors.ActiveSide);
    }

    private EntityUid? ResolveDialogueActor(DialogueActorData? actor)
    {
        if (actor == null)
            return null;

        if (actor.PortraitPrototype is { } portrait)
        {
            var staticPreview = _preview?.TryCreatePrototypePreview(portrait);
            if (staticPreview != null)
            {
                _actorPreviewEntities.Add(staticPreview.Value);
                return staticPreview;
            }
        }

        if (actor.Entity == null)
            return null;

        var resolved = EntityManager.GetEntity(actor.Entity.Value);

        if (resolved == EntityUid.Invalid || !EntityManager.EntityExists(resolved))
            return null;

        var preview = _preview?.TryCreatePreview(resolved);
        if (preview != null)
            _actorPreviewEntities.Add(preview.Value);

        return preview ?? resolved;
    }

    private void DeleteActorPreviews()
    {
        foreach (var preview in _actorPreviewEntities)
        {
            EntityUid? entity = preview;
            _preview?.DeletePreview(ref entity);
        }

        _actorPreviewEntities.Clear();
    }

    private string FormatText(DialogueTextData text)
    {
        return Loc.GetString(
            text.Text,
            text.Arguments.Select(argument => (argument.Id, ResolveLocalizationArgument(argument))).ToArray());
    }

    private object ResolveLocalizationArgument(DialogueLocArgumentData argument)
    {
        if (argument.Text != null)
            return argument.Text;
        if (argument.Number != null)
            return argument.Number.Value;
        if (argument.Prototype != null
            && _prototypeManager.TryIndex<EntityPrototype>(argument.Prototype, out var prototype))
        {
            return prototype.Name;
        }

        return argument.Prototype ?? string.Empty;
    }
}
