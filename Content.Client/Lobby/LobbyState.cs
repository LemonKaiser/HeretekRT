using Content.Client._NF.LateJoin;
using Content.Client._WH40K.CharacterCreation;
using Content.Client._WH40K.DeathTransition;
using Content.Client.Administration.Managers;
using Content.Client.Audio;
using Content.Client.Eui;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby.UI;
using Content.Client.Message;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.Voting;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared.Administration;
using Content.Shared.Preferences;
using Robust.Client;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using PickerWindow = Content.Client._NF.LateJoin.Windows.PickerWindow;

namespace Content.Client.Lobby
{
    public sealed partial class LobbyState : Robust.Client.State.State
    {
        [Dependency] private IBaseClient _baseClient = default!;
        [Dependency] private IConfigurationManager _cfg = default!;
        [Dependency] private IClientConsoleHost _consoleHost = default!;
        [Dependency] private IEntityManager _entityManager = default!;
        [Dependency] private IClyde _clyde = default!;
        [Dependency] private IResourceCache _resourceCache = default!;
        [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private IGameTiming _gameTiming = default!;
        [Dependency] private IVoteManager _voteManager = default!;
        [Dependency] private IPrototypeManager _protoMan = default!;
        [Dependency] private IClientAdminManager _adminManager = default!;
        [Dependency] private IClientPreferencesManager _preferencesManager = default!;

        private ClientGameTicker _gameTicker = default!;
        private ContentAudioSystem _contentAudioSystem = default!;
        private GhostPermissionStatusSystem _ghostPermissionStatus = default!;
        private LobbyBackgroundController? _lobbyBackgroundController;
        private ChatUIController? _chatController;
        private bool? _lastRoundStarted;
        private bool? _lastObserveAvailable;
        private bool? _lastPersonalizationAvailable;
        private bool? _lastOnboardingRequired;
        private Wh40kProfileEditMode? _lastProfileEditMode;
        private bool? _lastProfileEditBypass;
        private long _lastLobbyClockSecond = long.MinValue;
        private bool? _lastLobbyClockRoundStarted;
        private bool? _lastLobbyClockPaused;

        protected override Type? LinkedScreenType { get; } = typeof(LobbyGui);
        public LobbyGui? Lobby;

        // Frontier - save pickerwindow so it opens only once
        private PickerWindow? _pickerWindow = null;

        protected override void Startup()
        {
            if (_userInterfaceManager.ActiveScreen == null)
            {
                return;
            }

            Lobby = (LobbyGui) _userInterfaceManager.ActiveScreen;

            _chatController = _userInterfaceManager.GetUIController<ChatUIController>();
            _gameTicker = _entityManager.System<ClientGameTicker>();
            _contentAudioSystem = _entityManager.System<ContentAudioSystem>();
            _ghostPermissionStatus = _entityManager.System<GhostPermissionStatusSystem>();
            _contentAudioSystem.LobbySoundtrackChanged += UpdateLobbySoundtrackInfo;

            _chatController.SetMainChat(true);

            _voteManager.SetPopupContainer(Lobby.VoteContainer);
            LayoutContainer.SetAnchorPreset(Lobby, LayoutContainer.LayoutPreset.Wide);

            var lobbyNameCvar = _cfg.GetCVar(CCVars.ServerLobbyName);
            var serverName = _baseClient.GameInfo?.ServerName ?? string.Empty;

            Lobby.ServerName.Text = string.IsNullOrEmpty(lobbyNameCvar)
                ? Loc.GetString("ui-lobby-title", ("serverName", serverName))
                : lobbyNameCvar;

            _preferencesManager.OnWh40kProgressChanged += OnWh40kProgressChanged;
            _preferencesManager.OnWh40kOnboardingCompletionFinished += OnWh40kOnboardingCompletionFinished;
            UpdateLobbyUi();
            Lobby.BeginPresentation();
            OnWh40kProgressChanged();
            _lobbyBackgroundController = new LobbyBackgroundController(
                _cfg,
                _protoMan,
                _resourceCache,
                _clyde,
                _gameTiming,
                () => _gameTicker.LobbyBackground?.ToString() ?? string.Empty);
            _lobbyBackgroundController.Startup(Lobby);

            Lobby.CharacterPreview.CharacterSetupButton.OnPressed += OnSetupPressed;
            Lobby.PersonalizationButton.OnPressed += OnSetupPressed;
            Lobby.ReadyButton.OnPressed += OnReadyPressed;
            Lobby.ReadyButton.OnToggled += OnReadyToggled;
            Lobby.OnboardingCancelButton.OnPressed += OnOnboardingCancelPressed;
            Lobby.OnOnboardingCompletionRequested += OnOnboardingCompletionRequested;

            _gameTicker.InfoBlobUpdated += UpdateLobbyUi;
            _gameTicker.LobbyStatusUpdated += LobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated += LobbyLateJoinStatusUpdated;
            _ghostPermissionStatus.StatusUpdated += UpdateLobbyUi;
            _adminManager.AdminStatusUpdated += UpdateLobbyUi;

        }

        protected override void Shutdown()
        {
            _chatController?.SetMainChat(false);
            _gameTicker.InfoBlobUpdated -= UpdateLobbyUi;
            _gameTicker.LobbyStatusUpdated -= LobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated -= LobbyLateJoinStatusUpdated;
            _ghostPermissionStatus.StatusUpdated -= UpdateLobbyUi;
            _adminManager.AdminStatusUpdated -= UpdateLobbyUi;
            _preferencesManager.OnWh40kProgressChanged -= OnWh40kProgressChanged;
            _preferencesManager.OnWh40kOnboardingCompletionFinished -= OnWh40kOnboardingCompletionFinished;
            _contentAudioSystem.LobbySoundtrackChanged -= UpdateLobbySoundtrackInfo;

            _voteManager.ClearPopupContainer();

            Lobby!.CharacterPreview.CharacterSetupButton.OnPressed -= OnSetupPressed;
            Lobby!.PersonalizationButton.OnPressed -= OnSetupPressed;
            Lobby!.ReadyButton.OnPressed -= OnReadyPressed;
            Lobby!.ReadyButton.OnToggled -= OnReadyToggled;
            Lobby!.OnboardingCancelButton.OnPressed -= OnOnboardingCancelPressed;
            Lobby!.OnOnboardingCompletionRequested -= OnOnboardingCompletionRequested;
            Lobby!.DiscardOnboardingDraft();

            _lobbyBackgroundController?.Shutdown();
            _lobbyBackgroundController = null;
            _chatController = null;
            _lastRoundStarted = null;
            _lastObserveAvailable = null;
            _lastPersonalizationAvailable = null;
            _lastOnboardingRequired = null;
            _lastProfileEditMode = null;
            _lastProfileEditBypass = null;
            Lobby = null;
        }

        public void SwitchState(LobbyGui.LobbyGuiState state)
        {
            // Yeah I hate this but LobbyState contains all the badness for now.
            Lobby?.SwitchState(state);
        }

        /// <summary>
        /// Opens the required Act I character creator.
        /// </summary>
        public bool TryOpenWh40kOnboarding()
        {
            if (_preferencesManager.Wh40kProgress.OnboardingStatus != Wh40kOnboardingStatus.Required)
                return false;

            var preferences = _preferencesManager.Preferences;
            if (Lobby is null || preferences is null ||
                preferences.SelectedCharacter is not HumanoidCharacterProfile profile)
            {
                return false;
            }

            SetReady(false);
            Lobby.BeginOnboardingTransition(new Wh40kOnboardingDraft(
                profile,
                preferences.SelectedCharacterIndex));
            return true;
        }

        private void OnSetupPressed(BaseButton.ButtonEventArgs args)
        {
            if (_preferencesManager.Wh40kProgress.OnboardingStatus == Wh40kOnboardingStatus.Required)
            {
                TryOpenWh40kOnboarding();
                return;
            }

            if (!_preferencesManager.Wh40kProgress.CanUseLegacyPersonalization)
                return;

            if (IsWh40kProfileEditingFullyLocked)
                return;

            SetReady(false);
            Lobby?.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
        }

        private void OnReadyPressed(BaseButton.ButtonEventArgs args)
        {
            if (!_gameTicker.IsGameStarted)
            {
                return;
            }

            if (!HasWh40kCharacterProfile)
            {
                ShowWh40kProfileRequiredMessage();
                return;
            }

            if (_pickerWindow is { IsOpen: true })
            {
                _pickerWindow.Close();
                return;
            }

            OpenLateJoinPicker();
        }

        private void OnOnboardingCancelPressed(BaseButton.ButtonEventArgs args)
        {
            Lobby?.ReturnToLobbyFromOnboarding();
        }

        private void OnOnboardingCompletionRequested(Wh40kOnboardingDraft draft)
        {
            if (_preferencesManager.CompleteWh40kOnboarding(draft.Profile))
            {
                Lobby?.SetOnboardingCompletionPending(true);
                return;
            }

            Lobby?.SetOnboardingCompletionResult(Wh40kOnboardingCompletionStatus.NotAllowed);
        }

        private void OnWh40kOnboardingCompletionFinished(Wh40kOnboardingCompletionStatus status)
        {
            if (status == Wh40kOnboardingCompletionStatus.Success)
            {
                Lobby?.ReturnToLobbyFromOnboarding();

                // A profile completed during a running round follows the same late-join route as an existing profile.
                if (_gameTicker.IsGameStarted)
                    OpenLateJoinPicker();

                return;
            }

            Lobby?.SetOnboardingCompletionResult(status);
        }

        private void OnReadyToggled(BaseButton.ButtonToggledEventArgs args)
        {
            SetReady(args.Pressed);
        }

        private void OpenLateJoinPicker()
        {
            if (!HasWh40kCharacterProfile)
            {
                ShowWh40kProfileRequiredMessage();
                return;
            }

            // Keep the normal station/job selection flow; it owns the final joingame command and its validation.
            _pickerWindow ??= new PickerWindow();
            if (!_pickerWindow.IsOpen)
                _pickerWindow.OpenCentered();
        }

        public override void FrameUpdate(FrameEventArgs e)
        {
            _lobbyBackgroundController?.FrameUpdate(e.DeltaSeconds);
            Lobby?.UpdateChatAnimation(e.DeltaSeconds);
            Lobby?.UpdateVisualEffects(e.DeltaSeconds);

            UpdateObserveButton();
            UpdatePersonalizationButton();
            UpdateLobbyClock();
        }

        private void UpdateLobbyClock()
        {
            var gameStarted = _gameTicker.IsGameStarted;
            var paused = _gameTicker.Paused;
            var currentSecond = (long) _gameTiming.CurTime.TotalSeconds;
            if (_lastLobbyClockSecond == currentSecond
                && _lastLobbyClockRoundStarted == gameStarted
                && _lastLobbyClockPaused == paused)
            {
                return;
            }

            _lastLobbyClockSecond = currentSecond;
            _lastLobbyClockRoundStarted = gameStarted;
            _lastLobbyClockPaused = paused;

            if (gameStarted)
            {
                Lobby!.StartTime.Text = string.Empty;
                Lobby.RoundStatus.Text = Loc.GetString("heretek-lobby-round-active");
                var roundTime = _gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan);
                Lobby.StationTime.Text = Loc.GetString(
                    "lobby-state-player-status-round-time",
                    ("hours", roundTime.Hours),
                    ("minutes", roundTime.Minutes));
                return;
            }

            Lobby!.RoundStatus.Text = Loc.GetString("heretek-lobby-round-awaiting");
            Lobby.StationTime.Text = string.Empty;
            string text;

            if (paused)
            {
                text = Loc.GetString("lobby-state-paused");
            }
            else if (_gameTicker.StartTime < _gameTiming.CurTime)
            {
                Lobby!.StartTime.Text = Loc.GetString("lobby-state-soon");
                return;
            }
            else
            {
                var difference = _gameTicker.StartTime - _gameTiming.CurTime;
                var seconds = difference.TotalSeconds;
                if (seconds < 0)
                {
                    text = Loc.GetString(seconds < -5 ? "lobby-state-right-now-question" : "lobby-state-right-now-confirmation");
                }
                else if (difference.TotalHours >= 1)
                {
                    text = $"{Math.Floor(difference.TotalHours)}:{difference.Minutes:D2}:{difference.Seconds:D2}";
                }
                else
                {
                    text = $"{difference.Minutes}:{difference.Seconds:D2}";
                }
            }

            Lobby!.StartTime.Text = Loc.GetString("lobby-state-round-start-countdown-text", ("timeLeft", text));
        }

        private void InvalidateLobbyClock()
        {
            _lastLobbyClockSecond = long.MinValue;
            _lastLobbyClockRoundStarted = null;
            _lastLobbyClockPaused = null;
        }

        private void LobbyStatusUpdated()
        {
            _lobbyBackgroundController?.RefreshBackground();
            InvalidateLobbyClock();
            UpdateLobbyUi();
        }

        private void LobbyLateJoinStatusUpdated()
        {
            UpdateLobbyUi();
        }

        private void UpdateLobbyUi()
        {
            InvalidateLobbyClock();
            UpdateChatRoundState();

            if (_gameTicker.IsGameStarted)
            {
                Lobby!.ReadyButton.Text = Loc.GetString("lobby-state-ready-button-join-state");
                Lobby!.ReadyButton.ToggleMode = false;
                Lobby!.ReadyButton.Disabled = _gameTicker.DisallowedLateJoin;
                Lobby!.ReadyButton.Pressed = false;
                Lobby.UpdateReadyButtonVisual(ready: false, roundStarted: true);
            }
            else
            {
                Lobby!.StartTime.Text = string.Empty;
                var ready = _gameTicker.AreWeReady;
                Lobby!.ReadyButton.Text = Loc.GetString(ready ? "lobby-state-player-status-ready" : "lobby-state-player-status-not-ready");
                Lobby!.ReadyButton.ToggleMode = true;
                Lobby!.ReadyButton.Disabled = !HasWh40kCharacterProfile;
                Lobby!.ReadyButton.Pressed = ready;
                Lobby.UpdateReadyButtonVisual(ready, roundStarted: false);
            }

            UpdateObserveButton();

            if (_gameTicker.ServerInfoBlob != null)
            {
                //Lobby!.ServerInfo.SetInfoBlob(_gameTicker.ServerInfoBlob); // Frontier: ???
            }
        }

        private void UpdateChatRoundState()
        {
            var roundStarted = _gameTicker.IsGameStarted;
            if (_lastRoundStarted == roundStarted)
                return;

            _lastRoundStarted = roundStarted;
            Lobby!.SetChatExpanded(!roundStarted);
        }

        private bool CanObserve()
        {
            return _ghostPermissionStatus.CanObserve
                   || (_adminManager.IsActive()
                       && (_adminManager.HasFlag(AdminFlags.Admin)
                           || _adminManager.HasFlag(AdminFlags.Moderator)));
        }

        /// <summary>
        /// The admin-status packet normally invokes this through <see cref="UpdateLobbyUi"/>.
        /// The state comparison in the frame loop also covers a status change received while
        /// the lobby is being recreated, without repeatedly touching the UI.
        /// </summary>
        private void UpdateObserveButton()
        {
            if (Lobby == null)
                return;

            var available = _gameTicker.IsGameStarted && CanObserve();
            if (_lastObserveAvailable == available)
                return;

            _lastObserveAvailable = available;
            Lobby.SetObserveAvailable(available);
        }

        private void UpdatePersonalizationButton()
        {
            if (Lobby == null)
                return;

            var onboardingRequired = _preferencesManager.Wh40kProgress.OnboardingStatus == Wh40kOnboardingStatus.Required;
            var profileEditMode = Wh40kProfileEditPolicy.ParseMode(_cfg.GetCVar(CCVars.Wh40kProfileEditMode));
            var adminBypass = CanBypassWh40kProfileEditing;
            var available = onboardingRequired ||
                            (_preferencesManager.Wh40kProgress.CanUseLegacyPersonalization &&
                             (profileEditMode != Wh40kProfileEditMode.FullLocked || adminBypass));
            if (_lastPersonalizationAvailable == available &&
                _lastOnboardingRequired == onboardingRequired &&
                _lastProfileEditMode == profileEditMode &&
                _lastProfileEditBypass == adminBypass)
                return;

            _lastPersonalizationAvailable = available;
            _lastOnboardingRequired = onboardingRequired;
            _lastProfileEditMode = profileEditMode;
            _lastProfileEditBypass = adminBypass;
            Lobby.SetPersonalizationAvailable(available);
            Lobby.CharacterPreview.CharacterSetupButton.Disabled = !available;
            Lobby.PersonalizationButton.Text = Loc.GetString(onboardingRequired
                ? "heretek-lobby-create-character"
                : "heretek-lobby-personalization");
        }

        private void OnWh40kProgressChanged()
        {
            UpdatePersonalizationButton();
            UpdateLobbyUi();
        }

        private void UpdateLobbySoundtrackInfo(LobbySoundtrackChangedEvent ev)
        {
            if (ev.SoundtrackFilename == null)
            {
                Lobby!.LobbySong.SetMarkup(Loc.GetString("lobby-state-song-no-song-text"));
            }
            else if (
                ev.SoundtrackFilename != null
                && _resourceCache.TryGetResource<AudioResource>(ev.SoundtrackFilename, out var lobbySongResource)
                )
            {
                var lobbyStream = lobbySongResource.AudioStream;

                var title = string.IsNullOrEmpty(lobbyStream.Title)
                    ? Loc.GetString("lobby-state-song-unknown-title")
                    : lobbyStream.Title;

                var artist = string.IsNullOrEmpty(lobbyStream.Artist)
                    ? Loc.GetString("lobby-state-song-unknown-artist")
                    : lobbyStream.Artist;

                var markup = Loc.GetString("lobby-state-song-text",
                    ("songTitle", title),
                    ("songArtist", artist));

                Lobby!.LobbySong.SetMarkup(markup);
            }
        }

        private void SetReady(bool newReady)
        {
            if (_gameTicker.IsGameStarted)
            {
                return;
            }

            if (newReady && !HasWh40kCharacterProfile)
            {
                ShowWh40kProfileRequiredMessage();
                return;
            }

            _consoleHost.ExecuteCommand($"toggleready {newReady}");
        }

        private bool HasWh40kCharacterProfile => _preferencesManager.Wh40kProgress.CanUseLegacyPersonalization;

        private bool IsWh40kProfileEditingFullyLocked =>
            Wh40kProfileEditPolicy.ParseMode(_cfg.GetCVar(CCVars.Wh40kProfileEditMode)) == Wh40kProfileEditMode.FullLocked &&
            !CanBypassWh40kProfileEditing;

        private bool CanBypassWh40kProfileEditing =>
            _cfg.GetCVar(CCVars.Wh40kProfileEditAdminBypass) &&
            _adminManager.IsActive() &&
            (_adminManager.HasFlag(AdminFlags.Admin) || _adminManager.HasFlag(AdminFlags.Moderator));

        private void ShowWh40kProfileRequiredMessage()
        {
            if (_chatController == null)
                return;

            var message = Loc.GetString("heretek-lobby-profile-required");
            var wrappedMessage = Loc.GetString(
                "chat-manager-server-wrap-message",
                ("message", FormattedMessage.EscapeText(message)));
            _chatController.ProcessChatMessage(
                new ChatMessage(ChatChannel.Server, message, wrappedMessage, default, null),
                speechBubble: false);
        }
    }
}
