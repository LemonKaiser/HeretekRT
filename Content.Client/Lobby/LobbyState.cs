using Content.Client._NF.LateJoin;
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
using Content.Shared.Administration;
using Robust.Client;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
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

        private ClientGameTicker _gameTicker = default!;
        private ContentAudioSystem _contentAudioSystem = default!;
        private GhostPermissionStatusSystem _ghostPermissionStatus = default!;
        private LobbyBackgroundController? _lobbyBackgroundController;
        private ChatUIController? _chatController;
        private bool? _lastRoundStarted;
        private bool? _lastObserveAvailable;
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

            UpdateLobbyUi();
            Lobby.BeginPresentation();
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
            _contentAudioSystem.LobbySoundtrackChanged -= UpdateLobbySoundtrackInfo;

            _voteManager.ClearPopupContainer();

            Lobby!.CharacterPreview.CharacterSetupButton.OnPressed -= OnSetupPressed;
            Lobby!.PersonalizationButton.OnPressed -= OnSetupPressed;
            Lobby!.ReadyButton.OnPressed -= OnReadyPressed;
            Lobby!.ReadyButton.OnToggled -= OnReadyToggled;

            _lobbyBackgroundController?.Shutdown();
            _lobbyBackgroundController = null;
            _chatController = null;
            _lastRoundStarted = null;
            _lastObserveAvailable = null;
            Lobby = null;
        }

        public void SwitchState(LobbyGui.LobbyGuiState state)
        {
            // Yeah I hate this but LobbyState contains all the badness for now.
            Lobby?.SwitchState(state);
        }

        private void OnSetupPressed(BaseButton.ButtonEventArgs args)
        {
            SetReady(false);
            Lobby?.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
        }

        private void OnReadyPressed(BaseButton.ButtonEventArgs args)
        {
            if (!_gameTicker.IsGameStarted)
            {
                return;
            }
            // Frontier to downstream: if you want to skip the first window and go straight to station picker,
            // simply change the enum to station or crew in the PickerWindow constructor.
            if (_pickerWindow is { IsOpen: true })
            {
                _pickerWindow.Close();
                return;
            }

            _pickerWindow ??= new PickerWindow();
            _pickerWindow.OpenCentered();
        }

        private void OnReadyToggled(BaseButton.ButtonToggledEventArgs args)
        {
            SetReady(args.Pressed);
        }

        public override void FrameUpdate(FrameEventArgs e)
        {
            _lobbyBackgroundController?.FrameUpdate(e.DeltaSeconds);
            Lobby?.UpdateChatAnimation(e.DeltaSeconds);
            Lobby?.UpdateVisualEffects(e.DeltaSeconds);

            UpdateObserveButton();
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
            Lobby!.ReadyButton.Disabled = _gameTicker.DisallowedLateJoin;
        }

        private void UpdateLobbyUi()
        {
            InvalidateLobbyClock();
            UpdateChatRoundState();

            if (_gameTicker.IsGameStarted)
            {
                Lobby!.ReadyButton.Text = Loc.GetString("lobby-state-ready-button-join-state");
                Lobby!.ReadyButton.ToggleMode = false;
                Lobby!.ReadyButton.Pressed = false;
                Lobby.UpdateReadyButtonVisual(ready: false, roundStarted: true);
            }
            else
            {
                Lobby!.StartTime.Text = string.Empty;
                var ready = _gameTicker.AreWeReady;
                Lobby!.ReadyButton.Text = Loc.GetString(ready ? "lobby-state-player-status-ready" : "lobby-state-player-status-not-ready");
                Lobby!.ReadyButton.ToggleMode = true;
                Lobby!.ReadyButton.Disabled = false;
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

            _consoleHost.ExecuteCommand($"toggleready {newReady}");
        }
    }
}
