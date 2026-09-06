using System.Numerics;
using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.RichText;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared._Forge.Barks;
using Content.Shared._Forge.Text;
using Content.Shared.Speech;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI
{
    public abstract partial class SpeechBubble : Control
    {
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IEyeManager _eyeManager = default!;
        [Dependency] private IEntityManager _entityManager = default!;
        [Dependency] private ChatEmojiCatalog _emojiCatalog = default!;
        [Dependency] protected IConfigurationManager ConfigManager = default!;
        private readonly SharedTransformSystem _transformSystem;

        public enum SpeechType : byte
        {
            Emote,
            Say,
            Whisper,
            Looc
        }

        /// <summary>
        ///     The total time a speech bubble stays on screen.
        /// </summary>
        private static readonly TimeSpan TotalTime = TimeSpan.FromSeconds(4);

        /// <summary>
        ///     The amount of time at the end of the bubble's life at which it starts fading.
        /// </summary>
        private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(0.25f);

        /// <summary>
        ///     The distance in world space to offset the speech bubble from the center of the entity.
        ///     i.e. greater -> higher above the mob's head.
        /// </summary>
        private const float EntityVerticalOffset = 0.5f;

        /// <summary>
        ///     The default maximum width for speech bubbles.
        /// </summary>
        public const float SpeechMaxWidth = 256;

        private readonly EntityUid _senderEntity;
        private Control? _bubble;
        private RichTextLabel? _typewriterLabel;
        private FormattedMessageTypewriter? _typewriter;
        private TimeSpan _createdTime;
        private int _visibleTextElements = -1;

        /// <summary>
        /// The time at which this bubble will die.
        /// </summary>
        private TimeSpan _deathTime;

        public float VerticalOffset { get; set; }
        private float _verticalOffsetAchieved;

        public Vector2 ContentSize { get; private set; }

        // man down
        public event Action<EntityUid, SpeechBubble>? OnDied;

        /// <summary>
        ///     Raised if the measured bubble size changes. Typewriter content reserves its final
        ///     size before it starts, so it does not move the active bubble stack every frame.
        /// </summary>
        public event Action<EntityUid, SpeechBubble>? OnContentSizeChanged;

        public static TimeSpan GetRevealDuration(string message, float speedMultiplier, float speakerPlaybackSpeed = 1f)
        {
            var speed = TextRevealTiming.ClampSpeedMultiplier(
                TextRevealTiming.ClampSpeedMultiplier(speedMultiplier) *
                TextRevealTiming.ClampSpeedMultiplier(speakerPlaybackSpeed));
            return TypewriterText.Create(message, TextRevealTiming.MaxWorldTextElements, TimeSpan.Zero, speed)
                .RevealPlan.Duration;
        }

        public static SpeechBubble CreateSpeechBubble(SpeechType type, ChatMessage message, EntityUid senderEntity)
        {
            switch (type)
            {
                case SpeechType.Emote:
                    return new TextSpeechBubble(message, senderEntity, "emoteBox");

                case SpeechType.Say:
                    return new FancyTextSpeechBubble(message, senderEntity, "sayBox");

                case SpeechType.Whisper:
                    return new FancyTextSpeechBubble(message, senderEntity, "whisperBox");

                case SpeechType.Looc:
                    return new TextSpeechBubble(message, senderEntity, "emoteBox", Color.FromHex("#48d1cc"));

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public SpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
        {
            IoCManager.InjectDependencies(this);
            _senderEntity = senderEntity;
            _transformSystem = _entityManager.System<SharedTransformSystem>();

            // Use text clipping so new messages don't overlap old ones being pushed up.
            RectClipContent = true;

            var bubble = BuildBubble(message, speechStyleClass, fontColor);
            _bubble = bubble;

            AddChild(bubble);

            ForceRunStyleUpdate();
            ReserveTypewriterContentSize();
            RefreshContentSize();
            _verticalOffsetAchieved = -ContentSize.Y;
            _createdTime = _timing.RealTime;
            UpdateTypewriter(TimeSpan.Zero);
            _deathTime = _createdTime + GetLifetime();
        }

        protected abstract Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null);

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            UpdateTypewriter(_timing.RealTime - _createdTime);

            var timeLeft = (float)(_deathTime - _timing.RealTime).TotalSeconds;
            if (_entityManager.Deleted(_senderEntity) || timeLeft <= 0)
            {
                // Timer spawn to prevent concurrent modification exception.
                Timer.Spawn(0, Die);
                return;
            }

            // Lerp to our new vertical offset if it's been modified.
            if (MathHelper.CloseToPercent(_verticalOffsetAchieved - VerticalOffset, 0, 0.1))
            {
                _verticalOffsetAchieved = VerticalOffset;
            }
            else
            {
                _verticalOffsetAchieved = MathHelper.Lerp(_verticalOffsetAchieved, VerticalOffset, 10 * args.DeltaSeconds);
            }

            if (!_entityManager.TryGetComponent<TransformComponent>(_senderEntity, out var xform) || xform.MapID != _eyeManager.CurrentEye.Position.MapId)
            {
                Modulate = Color.White.WithAlpha(0);
                return;
            }

            if (timeLeft <= FadeTime.TotalSeconds)
            {
                // Update alpha if we're fading.
                Modulate = Color.White.WithAlpha(timeLeft / (float)FadeTime.TotalSeconds);
            }
            else
            {
                // Make opaque otherwise, because it might have been hidden before
                Modulate = Color.White;
            }

            var baseOffset = 0f;

            if (_entityManager.TryGetComponent<SpeechComponent>(_senderEntity, out var speech))
                baseOffset = speech.SpeechBubbleOffset;

            var offset = (-_eyeManager.CurrentEye.Rotation).ToWorldVec() * -(EntityVerticalOffset + baseOffset);
            var worldPos = _transformSystem.GetWorldPosition(xform) + offset;

            var lowerCenter = _eyeManager.WorldToScreen(worldPos) / UIScale;
            var screenPos = lowerCenter - new Vector2(ContentSize.X / 2, ContentSize.Y + _verticalOffsetAchieved);
            // Round to nearest 0.5
            screenPos = (screenPos * 2).Rounded() / 2;
            LayoutContainer.SetPosition(this, screenPos);

            var height = MathF.Ceiling(MathHelper.Clamp(lowerCenter.Y - screenPos.Y, 0, ContentSize.Y));
            SetHeight = height;
        }

        /// <summary>
        ///     Assigns the label which should reveal itself. Speaker names intentionally remain immediate.
        /// </summary>
        protected void SetTypewriterContent(RichTextLabel label, FormattedMessage message)
        {
            _typewriterLabel = label;
            var typewriter = FormattedMessageTypewriter.Create(
                message,
                TextRevealTiming.MaxWorldTextElements,
                TimeSpan.Zero,
                GetTypewriterSpeed());

            // Measure the styled label at its final content before replacing it with a
            // prefix. This gives the background and the whole bubble stack their final geometry
            // from the first frame instead of resizing on every character.
            var fullElementCount = typewriter.RevealPlan.ElementCount;
            label.SetMessage(typewriter.GetVisibleMessage(fullElementCount), tagsAllowed: null);
            _visibleTextElements = fullElementCount;

            if (!ShouldAnimateTypewriter())
            {
                return;
            }

            _typewriter = typewriter;
            _visibleTextElements = -1;
        }

        private TimeSpan GetLifetime()
        {
            return TotalTime + (_typewriter?.RevealPlan.Duration ?? TimeSpan.Zero);
        }

        private void UpdateTypewriter(TimeSpan elapsed)
        {
            if (_typewriter == null || _typewriterLabel == null)
                return;

            if (!ShouldAnimateTypewriter())
            {
                _visibleTextElements = _typewriter.RevealPlan.ElementCount;
                _typewriterLabel.SetMessage(_typewriter.GetVisibleMessage(_visibleTextElements), tagsAllowed: null);
                _typewriter = null;
                return;
            }

            var visible = _typewriter.RevealPlan.GetVisibleElementCount(elapsed);
            if (visible == _visibleTextElements)
                return;

            _visibleTextElements = visible;
            // Keep the unprinted suffix transparent in this same label. The line breaker therefore
            // always works against the final phrase and cannot briefly put the start of a word past
            // the right edge before moving it to the next line on a later typewriter frame.
            _typewriterLabel.SetMessage(_typewriter.GetLayoutMessage(visible), tagsAllowed: null);
        }

        private bool ShouldAnimateTypewriter()
            => ConfigManager.GetCVar(CCVars.TypewriterTextEnabled) &&
               !ConfigManager.GetCVar(CCVars.ReducedMotion);

        private float GetTypewriterSpeed()
        {
            var speakerSpeed = _entityManager.TryGetComponent<SpeechSynthesisComponent>(_senderEntity, out var speech)
                ? speech.PlaybackSpeed
                : 1f;
            return TextRevealTiming.ClampSpeedMultiplier(
                TextRevealTiming.ClampSpeedMultiplier(ConfigManager.GetCVar(CCVars.TypewriterTextSpeed)) *
                TextRevealTiming.ClampSpeedMultiplier(speakerSpeed));
        }

        private void ReserveTypewriterContentSize()
        {
            if (_typewriterLabel == null)
                return;

            _typewriterLabel.Measure(Vector2Helpers.Infinity);
            // DesiredSize includes the control's external margin. MinSize is applied before that
            // margin is added by the layout engine, so storing DesiredSize directly would add the
            // padding twice and leave a visibly oversized speech background.
            var margin = _typewriterLabel.Margin;
            var contentSize = Vector2.Max(
                Vector2.Zero,
                _typewriterLabel.DesiredSize - new Vector2(margin.Left + margin.Right, margin.Top + margin.Bottom));
            _typewriterLabel.MinSize = Vector2.Max(_typewriterLabel.MinSize, contentSize);
        }

        private void RefreshContentSize()
        {
            if (_bubble == null)
                return;

            _bubble.Measure(Vector2Helpers.Infinity);
            var contentSize = _bubble.DesiredSize;
            if (contentSize == ContentSize)
                return;

            ContentSize = contentSize;
            OnContentSizeChanged?.Invoke(_senderEntity, this);
        }

        private void Die()
        {
            if (Disposed)
            {
                return;
            }

            OnDied?.Invoke(_senderEntity, this);
        }

        /// <summary>
        ///     Causes the speech bubble to start fading IMMEDIATELY.
        /// </summary>
        public void FadeNow()
        {
            if (_deathTime > _timing.RealTime)
            {
                _deathTime = _timing.RealTime + FadeTime;
            }
        }

        protected FormattedMessage FormatSpeech(string message, Color? fontColor = null)
        {
            var msg = new FormattedMessage();
            if (fontColor != null)
                msg.PushColor(fontColor.Value);
            msg.AddMarkupOrThrow(message);
            return msg;
        }

        protected FormattedMessage ExtractAndFormatSpeechSubstring(ChatMessage message, string tag, Color? fontColor = null)
        {
            return FormatSpeech(SharedChatSystem.GetStringInsideTag(message, tag), fontColor);
        }

        protected bool ShouldRenderEmojiAliases(ChatMessage message)
        {
            return UserInterfaceManager.GetUIController<ChatUIController>().IsEmojiAllowed(message.Channel);
        }

        protected ChatEmojiCatalog EmojiCatalog => _emojiCatalog;
        protected int MaxEmojiPerMessage => UserInterfaceManager.GetUIController<ChatUIController>().MaxEmojiPerMessage;

    }

    public sealed class TextSpeechBubble : SpeechBubble
    {
        public TextSpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
            : base(message, senderEntity, speechStyleClass, fontColor)
        {
        }

        protected override Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null)
        {
            var label = new RichTextLabel
            {
                MaxWidth = SpeechMaxWidth,
            };

            SetTypewriterContent(label, ChatEmojiRichText.ReplaceEmojiText(
                FormatSpeech(message.WrappedMessage, fontColor),
                ShouldRenderEmojiAliases(message),
                EmojiCatalog,
                MaxEmojiPerMessage));

            var panel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { label },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity))
            };

            return panel;
        }
    }

    public sealed class FancyTextSpeechBubble : SpeechBubble
    {

        public FancyTextSpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
            : base(message, senderEntity, speechStyleClass, fontColor)
        {
        }

        protected override Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null)
        {
            if (!ConfigManager.GetCVar(CCVars.ChatEnableFancyBubbles))
            {
                var label = new RichTextLabel
                {
                    MaxWidth = SpeechMaxWidth
                };

                SetTypewriterContent(label, ChatEmojiRichText.ReplaceEmojiText(
                    ExtractAndFormatSpeechSubstring(message, "BubbleContent", fontColor),
                    ShouldRenderEmojiAliases(message),
                    EmojiCatalog,
                    MaxEmojiPerMessage));

                var unfanciedPanel = new PanelContainer
                {
                    StyleClasses = { "speechBox", speechStyleClass },
                    Children = { label },
                    ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity)),
                };
                return unfanciedPanel;
            }

            var bubbleHeader = new RichTextLabel
            {
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleSpeakerOpacity)),
                Margin = new Thickness(1, 1, 1, 1),
            };

            var bubbleContent = new RichTextLabel
            {
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleTextOpacity)),
                MaxWidth = SpeechMaxWidth,
                Margin = new Thickness(2, 6, 2, 2),
                StyleClasses = { "bubbleContent" },
            };

            //We'll be honest. *Yes* this is hacky. Doing this in a cleaner way would require a bottom-up refactor of how saycode handles sending chat messages. -Myr
            bubbleHeader.SetMessage(ChatEmojiRichText.ReplaceEmojiText(
                ExtractAndFormatSpeechSubstring(message, "BubbleHeader", fontColor),
                ShouldRenderEmojiAliases(message),
                EmojiCatalog,
                MaxEmojiPerMessage), tagsAllowed: null);
            SetTypewriterContent(bubbleContent, ChatEmojiRichText.ReplaceEmojiText(
                ExtractAndFormatSpeechSubstring(message, "BubbleContent", fontColor),
                ShouldRenderEmojiAliases(message),
                EmojiCatalog,
                MaxEmojiPerMessage));

            //As for below: Some day this could probably be converted to xaml. But that is not today. -Myr
            var mainPanel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { bubbleContent },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity)),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Bottom,
                Margin = new Thickness(4, 14, 4, 2)
            };

            var headerPanel = new PanelContainer
            {
                StyleClasses = { "speechBox", speechStyleClass },
                Children = { bubbleHeader },
                ModulateSelfOverride = Color.White.WithAlpha(ConfigManager.GetCVar(CCVars.ChatFancyNameBackground) ? ConfigManager.GetCVar(CCVars.SpeechBubbleBackgroundOpacity) : 0f),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Top
            };

            var panel = new PanelContainer
            {
                Children = { mainPanel, headerPanel }
            };

            return panel;
        }
    }
}
