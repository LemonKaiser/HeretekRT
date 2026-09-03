using System.Linq;
using System.Text;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Chat;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared._Forge.Text;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Replays;
using Robust.Shared.Timing;

namespace Content.Client.Popups
{
    public sealed partial class PopupSystem : SharedPopupSystem
    {
        [Dependency] private IConfigurationManager _configManager = default!;
        [Dependency] private IInputManager _inputManager = default!;
        [Dependency] private IOverlayManager _overlay = default!;
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private IPrototypeManager _prototype = default!;
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IUserInterfaceManager _uiManager = default!;
        [Dependency] private IReplayRecordingManager _replayRecording = default!;
        [Dependency] private ExamineSystemShared _examine = default!;
        [Dependency] private SharedTransformSystem _transform = default!;

        public IReadOnlyCollection<WorldPopupLabel> WorldLabels => _aliveWorldLabels.Values;
        public IReadOnlyCollection<CursorPopupLabel> CursorLabels => _aliveCursorLabels.Values;

        private readonly Dictionary<WorldPopupData, WorldPopupLabel> _aliveWorldLabels = new();
        private readonly Dictionary<CursorPopupData, CursorPopupLabel> _aliveCursorLabels = new();

        public const float MinimumPopupLifetime = 0.7f;
        public const float MaximumPopupLifetime = 5f;
        public const float PopupLifetimePerCharacter = 0.04f;
        public const float MaximumWorldPopupWidth = 360f;

        // WD EDIT START
        private static readonly Dictionary<PopupType, string> FontSizeDict = new()
        {
            { PopupType.Medium, "12" },
            { PopupType.MediumCaution, "12" },
            { PopupType.Large, "15" },
            { PopupType.LargeCaution, "15" }
        };

        private bool _shouldLogInChat;
        // WD EDIT END

        public override void Initialize()
        {
            SubscribeNetworkEvent<PopupCursorEvent>(OnPopupCursorEvent);
            SubscribeNetworkEvent<PopupCoordinatesEvent>(OnPopupCoordinatesEvent);
            SubscribeNetworkEvent<PopupEntityEvent>(OnPopupEntityEvent);
            SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
            _overlay
                .AddOverlay(new PopupOverlay(
                    _configManager,
                    EntityManager,
                    _playerManager,
                    _prototype,
                    _uiManager,
                    _uiManager.GetUIController<PopupUIController>(),
                    _examine,
                    _transform,
                    this));

            // WD EDIT START
            _shouldLogInChat = _configManager.GetCVar(CCVars.LogInChat);
            _configManager.OnValueChanged(CCVars.LogInChat, log => { _shouldLogInChat = log; });
            // WD EDIT END
        }

        public override void Shutdown()
        {
            base.Shutdown();
            _overlay
                .RemoveOverlay<PopupOverlay>();
        }

        private void WrapAndRepeatPopup(PopupLabel existingLabel, string popupMessage)
        {
            existingLabel.TotalTime = 0;
            existingLabel.Repeats += 1;

            if (existingLabel is WorldPopupLabel worldLabel)
            {
                // A world popup has an independent reveal clock. Only refresh its lifetime and
                // counter: resetting its message would restart the typewriter every time a player
                // repeats the same action.
                worldLabel.SetRepeatCount(existingLabel.Repeats);
                return;
            }

            var repeatedMessage = Loc.GetString("popup-system-repeated-popup-stacking-wrap",
                ("popup-message", popupMessage),
                ("count", existingLabel.Repeats));
            existingLabel.Text = repeatedMessage;
        }

        private void PopupMessage(string? message, PopupType type, EntityCoordinates coordinates, EntityUid? entity, bool recordReplay)
        {
            if (message == null)
                return;

            // Filter out specific messages
            if (message.StartsWith("+") || message.StartsWith("combat", StringComparison.OrdinalIgnoreCase))
                return;

            if (recordReplay && _replayRecording.IsRecording)
            {
                if (entity != null)
                    _replayRecording.RecordClientMessage(new PopupEntityEvent(message, type, GetNetEntity(entity.Value)));
                else
                    _replayRecording.RecordClientMessage(new PopupCoordinatesEvent(message, type, GetNetCoordinates(coordinates)));
            }

            // WD EDIT START
            if (_shouldLogInChat &&
                _playerManager.LocalEntity != null &&
                _examine.InRangeUnOccluded(_playerManager.LocalEntity.Value, coordinates, 10))
            {
                var fontsize = FontSizeDict.GetValueOrDefault(type, "10");
                var fontcolor = type is PopupType.LargeCaution or PopupType.MediumCaution or PopupType.SmallCaution
                    ? "#C62828"
                    : "#AEABC4";

                var wrappedMessage = $"[font size={fontsize}][color={fontcolor}]{message}[/color][/font]";
                var chatMsg = new ChatMessage(ChatChannel.Emotes, message, wrappedMessage, GetNetEntity(EntityUid.Invalid), null);
                _uiManager.GetUIController<ChatUIController>().ProcessChatMessage(chatMsg);
            }
            // WD EDIT END

            var popupData = new WorldPopupData(message, type, coordinates, entity);
            if (_aliveWorldLabels.TryGetValue(popupData, out var existingLabel))
            {
                WrapAndRepeatPopup(existingLabel, popupData.Message);
                return;
            }

            var label = new WorldPopupLabel(coordinates)
            {
                Type = type,
            };
            label.SetMessage(message, ShouldAnimateTypewriter(), GetTypewriterSpeed());

            _aliveWorldLabels.Add(popupData, label);
        }

        #region Abstract Method Implementations
        public override void PopupCoordinates(string? message, EntityCoordinates coordinates, PopupType type = PopupType.Small)
        {
            PopupMessage(message, type, coordinates, null, true);
        }

        public override void PopupCoordinates(string? message, EntityCoordinates coordinates, ICommonSession recipient, PopupType type = PopupType.Small)
        {
            if (_playerManager.LocalSession == recipient)
                PopupMessage(message, type, coordinates, null, true);
        }

        public override void PopupCoordinates(string? message, EntityCoordinates coordinates, EntityUid recipient, PopupType type = PopupType.Small)
        {
            if (_playerManager.LocalEntity == recipient)
                PopupMessage(message, type, coordinates, null, true);
        }

        public override void PopupPredictedCoordinates(string? message, EntityCoordinates coordinates, EntityUid? recipient, PopupType type = PopupType.Small)
        {
            if (recipient != null && _timing.IsFirstTimePredicted)
                PopupCoordinates(message, coordinates, recipient.Value, type);
        }

        private void PopupCursorInternal(string? message, PopupType type, bool recordReplay)
        {
            if (message == null)
                return;

            if (recordReplay && _replayRecording.IsRecording)
                _replayRecording.RecordClientMessage(new PopupCursorEvent(message, type));

            var popupData = new CursorPopupData(message, type);
            if (_aliveCursorLabels.TryGetValue(popupData, out var existingLabel))
            {
                WrapAndRepeatPopup(existingLabel, popupData.Message);
                return;
            }

            var label = new CursorPopupLabel(_inputManager.MouseScreenPosition)
            {
                Text = message,
                Type = type,
            };

            _aliveCursorLabels.Add(popupData, label);
        }

        public override void PopupCursor(string? message, PopupType type = PopupType.Small)
        {
            if (!_timing.IsFirstTimePredicted)
                return;

            PopupCursorInternal(message, type, true);
        }

        public override void PopupCursor(string? message, ICommonSession recipient, PopupType type = PopupType.Small)
        {
            if (_playerManager.LocalSession == recipient)
                PopupCursor(message, type);
        }

        public override void PopupCursor(string? message, EntityUid recipient, PopupType type = PopupType.Small)
        {
            if (_playerManager.LocalEntity == recipient)
                PopupCursor(message, type);
        }

        public override void PopupPredictedCursor(string? message, ICommonSession recipient, PopupType type = PopupType.Small)
        {
            PopupCursor(message, recipient, type);
        }

        public override void PopupPredictedCursor(string? message, EntityUid recipient, PopupType type = PopupType.Small)
        {
            PopupCursor(message, recipient, type);
        }

        public override void PopupCoordinates(string? message, EntityCoordinates coordinates, Filter filter, bool replayRecord, PopupType type = PopupType.Small)
        {
            PopupCoordinates(message, coordinates, type);
        }

        public override void PopupEntity(string? message, EntityUid uid, EntityUid recipient, PopupType type = PopupType.Small)
        {
            if (_playerManager.LocalEntity == recipient)
                PopupEntity(message, uid, type);
        }

        public override void PopupEntity(string? message, EntityUid uid, ICommonSession recipient, PopupType type = PopupType.Small)
        {
            if (_playerManager.LocalSession == recipient)
                PopupEntity(message, uid, type);
        }

        public override void PopupEntity(string? message, EntityUid uid, Filter filter, bool recordReplay, PopupType type = PopupType.Small)
        {
            if (!filter.Recipients.Contains(_playerManager.LocalSession))
                return;

            PopupEntity(message, uid, type);
        }

        public override void PopupClient(string? message, EntityUid? recipient, PopupType type = PopupType.Small)
        {
            if (recipient == null)
                return;

            if (_timing.IsFirstTimePredicted)
                PopupCursor(message, recipient.Value, type);
        }

        public override void PopupClient(string? message, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        {
            if (recipient == null)
                return;

            if (_timing.IsFirstTimePredicted)
                PopupEntity(message, uid, recipient.Value, type);
        }

        public override void PopupClient(string? message, EntityCoordinates coordinates, EntityUid? recipient, PopupType type = PopupType.Small)
        {
            if (recipient == null)
                return;

            if (_timing.IsFirstTimePredicted)
                PopupCoordinates(message, coordinates, recipient.Value, type);
        }

        public override void PopupEntity(string? message, EntityUid uid, PopupType type = PopupType.Small)
        {
            if (TryComp(uid, out TransformComponent? transform))
                PopupMessage(message, type, transform.Coordinates, uid, true);
        }

        public override void PopupPredicted(string? message, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        {
            if (recipient != null && _timing.IsFirstTimePredicted)
                PopupEntity(message, uid, recipient.Value, type);
        }

        public override void PopupPredicted(string? recipientMessage, string? othersMessage, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        {
            if (recipient != null && _timing.IsFirstTimePredicted)
                PopupEntity(recipientMessage, uid, recipient.Value, type);
        }

        #endregion

        #region Network Event Handlers

        private void OnPopupCursorEvent(PopupCursorEvent ev)
        {
            PopupCursorInternal(ev.Message, ev.Type, false);
        }

        private void OnPopupCoordinatesEvent(PopupCoordinatesEvent ev)
        {
            PopupMessage(ev.Message, ev.Type, GetCoordinates(ev.Coordinates), null, false);
        }

        private void OnPopupEntityEvent(PopupEntityEvent ev)
        {
            var entity = GetEntity(ev.Uid);

            if (TryComp(entity, out TransformComponent? transform))
                PopupMessage(ev.Message, ev.Type, transform.Coordinates, entity, false);
        }

        private void OnRoundRestart(RoundRestartCleanupEvent ev)
        {
            _aliveCursorLabels.Clear();
            _aliveWorldLabels.Clear();
        }

        #endregion

        public static float GetPopupLifetime(PopupLabel label)
        {
            var text = label is WorldPopupLabel worldLabel ? worldLabel.FullText : label.Text;
            var readingTime = Math.Clamp(PopupLifetimePerCharacter * text.Length,
                MinimumPopupLifetime,
                MaximumPopupLifetime);

            return label is WorldPopupLabel world ? readingTime + world.RevealDuration : readingTime;
        }

        public static float GetPopupFadeStart(PopupLabel label)
        {
            var lifetime = GetPopupLifetime(label);
            if (label is not WorldPopupLabel world)
                return lifetime / 2f;

            return Math.Clamp(Math.Max(world.RevealDuration, lifetime - 0.5f), 0f, lifetime);
        }

        public override void FrameUpdate(float frameTime)
        {
            if (_aliveWorldLabels.Count == 0 && _aliveCursorLabels.Count == 0)
                return;

            if (_aliveWorldLabels.Count > 0)
            {
                var aliveWorldToRemove = new ValueList<WorldPopupData>();
                foreach (var (data, label) in _aliveWorldLabels)
                {
                    label.TotalTime += frameTime;
                    label.AdvanceReveal(frameTime, !ShouldAnimateTypewriter());
                    if (label.TotalTime > GetPopupLifetime(label) || Deleted(label.InitialPos.EntityId))
                    {
                        aliveWorldToRemove.Add(data);
                    }
                }
                foreach (var data in aliveWorldToRemove)
                {
                    _aliveWorldLabels.Remove(data);
                }
            }

            if (_aliveCursorLabels.Count > 0)
            {
                var aliveCursorToRemove = new ValueList<CursorPopupData>();
                foreach (var (data, label) in _aliveCursorLabels)
                {
                    label.TotalTime += frameTime;
                    if (label.TotalTime > GetPopupLifetime(label))
                    {
                        aliveCursorToRemove.Add(data);
                    }
                }
                foreach (var data in aliveCursorToRemove)
                {
                    _aliveCursorLabels.Remove(data);
                }
            }
        }

        public abstract class PopupLabel
        {
            public PopupType Type = PopupType.Small;
            public string Text { get; set; } = string.Empty;
            public float TotalTime { get; set; }
            public int Repeats = 1;
        }

        public sealed class WorldPopupLabel(EntityCoordinates coordinates) : PopupLabel
        {
            /// <summary>
            /// The original EntityCoordinates of the label.
            /// </summary>
            public EntityCoordinates InitialPos = coordinates;

            public string FullText { get; private set; } = string.Empty;
            /// <summary>
            ///     The complete pre-wrapped text used to keep the world-space anchor stable while a
            ///     prefix is being revealed.
            /// </summary>
            public string ReservedText { get; private set; } = string.Empty;
            public float RevealDuration { get; private set; }

            /// <summary>
            ///     The counter is drawn separately from the typewriter source, so repeated popups
            ///     can refresh their lifetime without recreating or rewinding the reveal.
            /// </summary>
            public string RepeatSuffix { get; private set; } = string.Empty;
            public string TextWithRepeatCount => Text + RepeatSuffix;
            public string ReservedTextWithRepeatCount => ReservedText + RepeatSuffix;

            private TypewriterText? _typewriter;
            private int _visibleTextElements = -1;
            private float _revealElapsed;
            private bool _animate;
            private bool _layoutPrepared;
            private bool _revealImmediately;
            private float _speedMultiplier;

            public void SetMessage(string message, bool animate, float speedMultiplier)
            {
                FullText = message;
                ReservedText = message;
                Text = animate ? string.Empty : message;
                RevealDuration = 0f;
                _typewriter = null;
                _visibleTextElements = -1;
                _revealElapsed = 0f;
                _animate = animate;
                _layoutPrepared = false;
                _revealImmediately = false;
                _speedMultiplier = speedMultiplier;
                RepeatSuffix = string.Empty;
            }

            public void SetRepeatCount(int repeats)
            {
                RepeatSuffix = repeats > 1 ? $" x{repeats}" : string.Empty;
            }

            /// <summary>
            ///     Builds the final visual layout before the first draw. The visible text is capped before it is
            ///     wrapped, keeping an unusually long popup from reserving most of the viewport.
            /// </summary>
            public void PrepareLayout(Font font, float scale)
            {
                if (_layoutPrepared)
                    return;

                _layoutPrepared = true;
                var boundedText = TypewriterText.Create(
                    FullText,
                    TextRevealTiming.MaxWorldTextElements,
                    TimeSpan.Zero,
                    _speedMultiplier);
                var displayText = boundedText.GetVisibleText(boundedText.RevealPlan.ElementCount);
                ReservedText = WrapText(displayText, font, scale, MaximumWorldPopupWidth * scale);

                if (!_animate || _revealImmediately)
                {
                    Text = ReservedText;
                    return;
                }

                // The lifetime includes the complete reveal duration. The visible text has already been
                // bounded above, so its pace stays constant without producing an oversized popup.
                _typewriter = TypewriterText.Create(
                    ReservedText,
                    Math.Max(ReservedText.Length, 1),
                    TimeSpan.Zero,
                    _speedMultiplier);
                RevealDuration = (float) _typewriter.RevealPlan.Duration.TotalSeconds;
                UpdateReveal(false);
            }

            public void AdvanceReveal(float frameTime, bool revealImmediately)
            {
                if (!revealImmediately)
                    _revealElapsed += Math.Max(0f, frameTime);

                UpdateReveal(revealImmediately);
            }

            public void UpdateReveal(bool revealImmediately)
            {
                _revealImmediately |= revealImmediately;
                if (_typewriter == null)
                    return;

                var visible = revealImmediately
                    ? _typewriter.RevealPlan.ElementCount
                    : _typewriter.RevealPlan.GetVisibleElementCount(TimeSpan.FromSeconds(_revealElapsed));

                if (visible == _visibleTextElements)
                    return;

                _visibleTextElements = visible;
                Text = _typewriter.GetVisibleText(visible);
            }

            private static string WrapText(string text, Font font, float scale, float maximumWidth)
            {
                if (string.IsNullOrEmpty(text) || maximumWidth <= 0f)
                    return text;

                var result = new StringBuilder(text.Length + Math.Max(4, text.Length / 24));
                var word = new List<string>();
                var whitespace = new StringBuilder();
                var lineWidth = 0f;
                var whitespaceWidth = 0f;
                var lineHasContent = false;

                void AppendNewLine()
                {
                    result.Append('\n');
                    lineWidth = 0f;
                    lineHasContent = false;
                }

                void AppendWord()
                {
                    if (word.Count == 0)
                        return;

                    var wordWidth = 0f;
                    foreach (var element in word)
                    {
                        wordWidth += GetElementWidth(element, font, scale);
                    }

                    if (lineHasContent && lineWidth + whitespaceWidth + wordWidth > maximumWidth)
                    {
                        AppendNewLine();
                    }
                    else if (lineHasContent && whitespace.Length > 0)
                    {
                        result.Append(whitespace);
                        lineWidth += whitespaceWidth;
                    }

                    foreach (var element in word)
                    {
                        var elementWidth = GetElementWidth(element, font, scale);
                        if (lineHasContent && lineWidth + elementWidth > maximumWidth)
                            AppendNewLine();

                        result.Append(element);
                        lineWidth += elementWidth;
                        lineHasContent = true;
                    }

                    word.Clear();
                    whitespace.Clear();
                    whitespaceWidth = 0f;
                }

                foreach (var element in TypewriterText.EnumerateTextElements(text))
                {
                    if (element.Contains('\n') || element.Contains('\r'))
                    {
                        AppendWord();
                        whitespace.Clear();
                        whitespaceWidth = 0f;
                        AppendNewLine();
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(element))
                    {
                        AppendWord();
                        if (lineHasContent)
                        {
                            whitespace.Append(element);
                            whitespaceWidth += GetElementWidth(element, font, scale);
                        }

                        continue;
                    }

                    word.Add(element);
                }

                AppendWord();
                return result.ToString();
            }

            private static float GetElementWidth(string element, Font font, float scale)
            {
                var width = 0f;
                foreach (var rune in element.EnumerateRunes())
                {
                    if (font.GetCharMetrics(rune, scale) is { } metrics)
                        width += metrics.Advance;
                }

                return width;
            }
        }

        private bool ShouldAnimateTypewriter()
            => _configManager.GetCVar(CCVars.TypewriterTextEnabled) &&
               !_configManager.GetCVar(CCVars.ReducedMotion);

        private float GetTypewriterSpeed()
            => _configManager.GetCVar(CCVars.TypewriterTextSpeed);

        public sealed class CursorPopupLabel(ScreenCoordinates screenCoords) : PopupLabel
        {
            public ScreenCoordinates InitialPos = screenCoords;
        }

        [UsedImplicitly]
        private record struct WorldPopupData(
            string Message,
            PopupType Type,
            EntityCoordinates Coordinates,
            EntityUid? Entity);

        [UsedImplicitly]
        private record struct CursorPopupData(
            string Message,
            PopupType Type);
    }
}
