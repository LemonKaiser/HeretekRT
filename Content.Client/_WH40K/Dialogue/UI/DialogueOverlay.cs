using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Client._WH40K.Dialogue;
using Content.Shared._WH40K.Dialogue;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._WH40K.Dialogue.UI;

public sealed class DialogueOverlay : LayoutContainer
{
    private const string BodyLabelStyleId = "dialogueBodyLabel";
    private const string SpeakerFontPath = "/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf";
    private const string BodyFontPath = "/Fonts/NotoSansDisplay/NotoSansDisplay-Regular.ttf";
    private const string BodyItalicFontPath = "/Fonts/NotoSansDisplay/NotoSansDisplay-Italic.ttf";
    private const string ContinueFontPath = "/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf";
    private const float DialogueContentMargin = 30f;
    private const int DialogueContentSeparation = 14;
    private const float SpeakerPlateHeight = 38f;
    private const float ContinueRowMinHeight = 36f;
    // This keeps the continue glyph optically above the bottom border without
    // making the footer's measured height disagree with its arranged height.
    private const float ContinueBottomInset = 7f;
    private const float ContinueRowHeight = ContinueRowMinHeight + ContinueBottomInset;
    private const float ChoiceAreaPadding = 4f;
    private const float ChoiceButtonHeight = 46f;
    private const float ChoiceButtonGap = 12f;
    private const float ChoiceRowGap = 10f;
    private const int MaxChoiceButtonsPerRow = 3;
    private const float ChoiceButtonMinWidth = 164f;
    private const float ChoiceButtonMaxWidth = 320f;
    private const float PanelEntranceDuration = 0.24f;
    private const float ActorEntranceDuration = 0.34f;
    private const float ActorEntranceDistance = 120f;
    private const int MinimumBodyFontSize = 14;
    private const float MinimumBodyFontScale = 0.75f;
    private const string Ellipsis = "...";
    private static readonly Color DialogueTextColor = new(232, 224, 210);
    private static readonly Color ThoughtTextColor = new(170, 176, 184);
    private static readonly Color NarrationTextColor = new(198, 194, 186);
    private static readonly Color AccentGold = new(215, 190, 132);
    private static readonly Color AccentGoldBright = new(246, 221, 159);
    private static readonly Color PanelBackground = new(8, 12, 16, 242);
    private static readonly Color PanelInnerBackground = new(17, 24, 31, 236);
    private static readonly Color ChoiceTextColor = new(255, 247, 225);
    private static readonly Color ChoiceDisabledTextColor = new(218, 204, 176);
    private static readonly Color CancelTextColor = new(178, 62, 52);
    private static readonly Color CancelTextHoverColor = new(232, 93, 76);
    private static readonly DialogueCutCornerStyleBox DialoguePanelStyle = new()
    {
        BackgroundColor = PanelBackground,
        BorderColor = AccentGold,
        BorderThickness = new Thickness(1.5f),
        CornerCut = 18f
    };
    // Interactive controls deliberately use the engine's regular StyleBoxFlat path. The custom
    // primitive-based cut-corner box is suitable for dark decoration, but its vertex colour path
    // makes authored button palettes much darker than ordinary UI colours.
    private static readonly StyleBoxFlat ChoiceButtonNormalStyle = CreateChoiceButtonStyle(
        new Color(5, 7, 10, 245),
        new Color(152, 126, 63));
    private static readonly StyleBoxFlat ChoiceButtonHoverStyle = CreateChoiceButtonStyle(
        new Color(31, 24, 6, 245),
        new Color(255, 204, 64),
        borderThickness: 2f);
    private static readonly StyleBoxFlat ChoiceButtonPressedStyle = CreateChoiceButtonStyle(
        new Color(19, 15, 4, 245),
        new Color(220, 167, 47),
        borderThickness: 2f);
    private static readonly StyleBoxFlat ChoiceButtonDisabledStyle = CreateChoiceButtonStyle(
        new Color(8, 10, 12, 220),
        new Color(73, 61, 35));
    private static readonly StyleBoxFlat ContinueIndicatorStyle = new()
    {
        BackgroundColor = Color.Transparent,
        BorderColor = Color.Transparent,
        BorderThickness = new Thickness(0f),
        ContentMarginLeftOverride = 0f,
        ContentMarginRightOverride = 0f,
        ContentMarginTopOverride = 0f,
        ContentMarginBottomOverride = 0f
    };
    private static readonly StyleBoxFlat CancelButtonStyle = new()
    {
        BackgroundColor = Color.Transparent,
        BorderColor = Color.Transparent,
        BorderThickness = new Thickness(0f),
        ContentMarginLeftOverride = 0f,
        ContentMarginRightOverride = 0f,
        ContentMarginTopOverride = 0f,
        ContentMarginBottomOverride = 0f
    };

    private readonly IResourceCache _resourceCache;
    private readonly StyleBoxFlat _dimStyle;
    private readonly PanelContainer _dimPanel;
    private readonly DialogueVignetteControl _vignette;
    private readonly LayoutContainer _actorStage;
    private readonly LayoutContainer _leftActorPanel;
    private readonly LayoutContainer _rightActorPanel;
    private readonly DialoguePortraitView _leftActorView;
    private readonly DialoguePortraitView _rightActorView;
    private readonly PanelContainer _dialoguePanel;
    private readonly BoxContainer _dialogueContent;
    private readonly BoxContainer _header;
    private readonly Control _headerSpacer;
    private readonly PanelContainer _speakerPlate;
    private readonly Label _speakerLabel;
    private readonly RichTextLabel _bodyLabel;
    private readonly LayoutContainer _choiceArea;
    private readonly DialogueCloseButton _cancelButton;
    private readonly Button _continueButton;
    private readonly List<Button> _choiceButtons = new();
    private DialogueActorSide _initiatorSide = DialogueActorSide.Left;
    private DialogueActorSide _npcSide = DialogueActorSide.Right;
    private bool _showActors = true;
    private bool _dimInactiveActors = true;
    private float _inactiveActorOpacity = 0.76f;
    private bool _sceneWindowVisible = true;
    private bool _sceneActorsVisible = true;
    private bool _sceneDimVisible = true;
    private Font _speakerFont = default!;
    private Font _continueFont = default!;
    private int _bodyBaseFontSize = 18;
    private int _bodyMinFontSize = MinimumBodyFontSize;
    private float _bodyMeasureWidth = 1f;
    private float _bodyMeasureHeight = 1f;
    private string _currentBodyFontPath = BodyFontPath;
    private Color _currentBodyTextColor = DialogueTextColor;
    private float _panelEntranceElapsed = PanelEntranceDuration;
    private float _continueShimmerTime;
    private float _leftActorTargetOffsetX;
    private float _leftActorTargetOffsetY;
    private float _rightActorTargetOffsetX;
    private float _rightActorTargetOffsetY;
    private float _leftActorEntranceDelay;
    private float _rightActorEntranceDelay;
    private float _leftActorEntranceElapsed = ActorEntranceDuration;
    private float _rightActorEntranceElapsed = ActorEntranceDuration;

    public event Action? AdvancePressed;
    public event Action? TextAreaPressed;
    public event Action<int>? ChoiceSelected;
    public event Action? CancelPressed;

    public DialogueOverlay()
    {
        _resourceCache = IoCManager.Resolve<IResourceCache>();
        MouseFilter = MouseFilterMode.Stop;
        Visible = false;

        _dimStyle = new StyleBoxFlat
        {
            BackgroundColor = Color.Black.WithAlpha(0.38f)
        };

        _dimPanel = new PanelContainer
        {
            PanelOverride = _dimStyle,
            HorizontalExpand = true,
            VerticalExpand = true
        };

        AddChild(_dimPanel);
        SetAnchorPreset(_dimPanel, LayoutPreset.Wide);

        _vignette = new DialogueVignetteControl();
        AddChild(_vignette);
        SetAnchorPreset(_vignette, LayoutPreset.Wide);
        _vignette.SetPositionInParent(1);

        _actorStage = new LayoutContainer
        {
            HorizontalExpand = true,
            MinSize = new Vector2(0f, 400f)
        };

        _leftActorView = CreateActorView(Direction.East);
        _rightActorView = CreateActorView(Direction.West);
        _leftActorPanel = CreateActorPanel(_leftActorView);
        _rightActorPanel = CreateActorPanel(_rightActorView);
        _actorStage.AddChild(_leftActorPanel);
        _actorStage.AddChild(_rightActorPanel);

        AddChild(_actorStage);
        SetAnchorPreset(_actorStage, LayoutPreset.BottomWide);
        _actorStage.SetPositionInParent(2);

        _dialoguePanel = new PanelContainer
        {
            HorizontalExpand = false,
            // Make the panel itself the click target whenever the pointer is not
            // over a real button.  PanelContainer defaults to Ignore, which was
            // why clicks in the text area previously reached only the overlay.
            MouseFilter = MouseFilterMode.Stop,
            MinSize = new Vector2(540f, 180f),
            MaxSize = new Vector2(980f, 230f),
            PanelOverride = DialoguePanelStyle
        };

        AddChild(_dialoguePanel);
        SetAnchorPreset(_dialoguePanel, LayoutPreset.CenterBottom);
        _dialoguePanel.SetPositionInParent(3);

        _dialogueContent = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(DialogueContentMargin),
            SeparationOverride = DialogueContentSeparation
        };

        _dialoguePanel.AddChild(_dialogueContent);
        _dialoguePanel.OnKeyBindDown += HandleDialoguePanelKeyBindDown;
        _dialogueContent.OnKeyBindDown += HandleDialoguePanelKeyBindDown;

        _speakerPlate = new PanelContainer
        {
            HorizontalExpand = true,
            Visible = false,
            MinSize = new Vector2(0f, SpeakerPlateHeight),
            PanelOverride = new DialogueCutCornerStyleBox
            {
                BackgroundColor = PanelInnerBackground,
                BorderColor = new Color(118, 102, 70),
                BorderThickness = new Thickness(1f),
                CornerCut = 10f
            }
        };

        _speakerLabel = new Label
        {
            FontColorOverride = AccentGoldBright,
            HorizontalExpand = true,
            VAlign = Label.VAlignMode.Center,
            Margin = new Thickness(14f, 4f, 14f, 4f)
        };

        _speakerPlate.AddChild(_speakerLabel);
        _speakerPlate.OnKeyBindDown += HandleDialoguePanelKeyBindDown;

        _cancelButton = new DialogueCloseButton
        {
            Visible = false,
            // The transparent button retains a header-sized click target and
            // reserves room for the close glyph without adding its own frame.
            MinSize = new Vector2(SpeakerPlateHeight, SpeakerPlateHeight),
            MaxSize = new Vector2(SpeakerPlateHeight, SpeakerPlateHeight),
            StyleBoxOverride = CancelButtonStyle,
            GlyphColor = CancelTextColor
        };

        _cancelButton.OnMouseEntered += _ => ApplyCancelButtonColor(hovered: true);
        _cancelButton.OnMouseExited += _ => ApplyCancelButtonColor(hovered: false);
        _cancelButton.OnPressed += _ => CancelPressed?.Invoke();

        _header = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            MinSize = new Vector2(0f, SpeakerPlateHeight),
            SeparationOverride = 8,
            Visible = false
        };

        _header.OnKeyBindDown += HandleDialoguePanelKeyBindDown;
        _header.AddChild(_speakerPlate);
        _headerSpacer = new Control
        {
            HorizontalExpand = true
        };
        _header.AddChild(_headerSpacer);
        _header.AddChild(_cancelButton);
        _dialogueContent.AddChild(_header);

        _bodyLabel = new RichTextLabel
        {
            StyleIdentifier = BodyLabelStyleId,
            HorizontalExpand = true,
            VerticalExpand = true,
            VerticalAlignment = VAlignment.Top,
            LineHeightScale = 1.08f
        };

        _bodyLabel.OnKeyBindDown += HandleDialoguePanelKeyBindDown;
        _dialogueContent.AddChild(_bodyLabel);

        _choiceArea = new LayoutContainer
        {
            HorizontalExpand = true,
            Visible = false
        };

        _choiceArea.OnKeyBindDown += HandleDialoguePanelKeyBindDown;
        _dialogueContent.AddChild(_choiceArea);

        var footer = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            MinSize = new Vector2(0f, ContinueRowHeight)
        };

        footer.OnKeyBindDown += HandleDialoguePanelKeyBindDown;

        footer.AddChild(new Control
        {
            HorizontalExpand = true
        });

        _continueButton = new Button
        {
            Text = Loc.GetString("heretek-dialogue-ui-continue"),
            Visible = false,
            MinSize = new Vector2(64f, ContinueRowMinHeight),
            MaxSize = new Vector2(64f, ContinueRowMinHeight),
            // The footer owns the bottom inset. Keeping this margin horizontal
            // prevents the button from changing the measured footer height.
            Margin = new Thickness(0f, 0f, 6f, 0f),
            StyleBoxOverride = ContinueIndicatorStyle
        };

        _continueButton.Label.FontColorOverride = AccentGoldBright;
        _continueButton.OnMouseEntered += _ => ApplyContinueButtonStyle(hovered: true);
        _continueButton.OnMouseExited += _ => ApplyContinueButtonStyle(hovered: false);
        _continueButton.OnPressed += _ => AdvancePressed?.Invoke();
        footer.AddChild(_continueButton);
        _dialogueContent.AddChild(footer);
    }

    private static DialoguePortraitView CreateActorView(Direction direction)
    {
        return new DialoguePortraitView
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Stretch = SpriteView.StretchMode.Fit,
            OverrideDirection = direction,
            Scale = new Vector2(10f, 10f)
        };
    }

    private static LayoutContainer CreateActorPanel(SpriteView actorView)
    {
        var actorPanel = new LayoutContainer
        {
            SetSize = new Vector2(280f, 320f),
            VerticalAlignment = VAlignment.Bottom,
            Visible = false
        };

        actorPanel.AddChild(actorView);
        SetAnchorPreset(actorPanel, LayoutPreset.CenterBottom);
        SetAnchorPreset(actorView, LayoutPreset.Wide);
        return actorPanel;
    }

    public void SetDimOpacity(float opacity)
    {
        _dimStyle.BackgroundColor = Color.Black.WithAlpha(Math.Clamp(opacity, 0f, 1f));
        _dimPanel.PanelOverride = _dimStyle;
    }

    public void ApplySceneState(DialogueSceneStateData state)
    {
        _sceneWindowVisible = state.ShowWindow;
        _sceneActorsVisible = state.ShowActors;
        _sceneDimVisible = state.ShowDim;
        UpdateSceneVisibility();
    }

    public void ApplyScene(DialogueSceneData scene, bool playEntrance = true)
    {
        SetDimOpacity(scene.DimOpacity);
        SetCancelVisible(scene.AllowCancel);

        var dialogueHeight = MathF.Max(scene.WindowMinHeight, scene.WindowMaxHeight);
        var actorWidth = MathF.Max(scene.ActorWidth, 1f);
        var actorHeight = MathF.Max(scene.ActorHeight, 1f);
        var actorGap = MathF.Max(scene.ActorGap, 0f);
        var actorOverlap = Math.Clamp(scene.ActorOverlap, 0f, actorHeight - 1f);
        var actorStageHeight = MathF.Max(1f, actorHeight - actorOverlap);
        var actorBottomOffset = MathF.Max(scene.WindowMargin + dialogueHeight - MathF.Max(scene.ActorWindowOverlap, 0f), 0f);
        var actorStageOffsetY = scene.ActorStageOffsetY;
        var leftActorOffsetX = scene.LeftActorOffsetX;
        var rightActorOffsetX = scene.RightActorOffsetX;
        var dialogueSize = new Vector2(scene.WindowWidth, dialogueHeight);

        if (Math.Abs(leftActorOffsetX) < 0.01f && Math.Abs(rightActorOffsetX) < 0.01f)
        {
            var legacyActorBaseOffsetX = (actorWidth + actorGap) * 0.5f;
            leftActorOffsetX = -legacyActorBaseOffsetX + ResolveActorAlignmentOffset(scene.LeftActorAlignmentX, actorWidth);
            rightActorOffsetX = legacyActorBaseOffsetX + ResolveActorAlignmentOffset(scene.RightActorAlignmentX, actorWidth);
        }

        _dialoguePanel.MinSize = dialogueSize;
        _dialoguePanel.MaxSize = dialogueSize;
        _dialoguePanel.SetSize = dialogueSize;
        ApplyDialoguePosition(scene.WindowAnchor, scene.WindowMargin, dialogueSize);
        _leftActorPanel.SetSize = new Vector2(actorWidth, actorHeight);
        _rightActorPanel.SetSize = new Vector2(actorWidth, actorHeight);
        _actorStage.MinSize = new Vector2(0f, actorStageHeight);
        SetMarginLeft(_actorStage, scene.WindowMargin);
        SetMarginRight(_actorStage, scene.WindowMargin);
        SetMarginTop(_actorStage, -(actorStageHeight + actorBottomOffset) + actorStageOffsetY);
        SetMarginBottom(_actorStage, -actorBottomOffset - actorStageOffsetY);
        _leftActorView.SetDialogueScale(scene.ActorScale);
        _rightActorView.SetDialogueScale(scene.ActorScale);
        _leftActorTargetOffsetX = leftActorOffsetX;
        _leftActorTargetOffsetY = scene.LeftActorOffsetY;
        _rightActorTargetOffsetX = rightActorOffsetX;
        _rightActorTargetOffsetY = scene.RightActorOffsetY;
        ApplyActorPosition(_leftActorPanel, _leftActorTargetOffsetX, _leftActorTargetOffsetY);
        ApplyActorPosition(_rightActorPanel, _rightActorTargetOffsetX, _rightActorTargetOffsetY);
        ApplyTypography(scene);
        UpdateBodyMeasureBounds();
        LayoutChoiceButtons();

        _showActors = scene.ShowActors;
        _initiatorSide = scene.InitiatorSide;
        _npcSide = scene.NpcSide;
        _dimInactiveActors = scene.DimInactiveActors;
        // Keep the inactive portrait readable.  This field used to be treated as
        // an opacity in content, but it is a colour multiplier in SpriteView.
        _inactiveActorOpacity = Math.Clamp(scene.InactiveActorOpacity, 0.72f, 0.95f);
        if (playEntrance)
        {
            _panelEntranceElapsed = 0f;
            _dialoguePanel.ModulateSelfOverride = Color.White.WithAlpha(0f);
        }
        UpdateSceneVisibility();
    }

    public void SetActors(EntityUid? initiator, EntityUid? target, bool playEntrance = true)
    {
        ClearActors();

        if (!_showActors)
            return;

        AssignActor(_initiatorSide, initiator);
        AssignActor(_npcSide, target);

        if (playEntrance && _leftActorPanel.Visible)
        {
            BeginActorEntrance(DialogueActorSide.Left, 0f);
            _leftActorView.PlayEntrance(0f);
        }

        if (playEntrance && _rightActorPanel.Visible)
        {
            BeginActorEntrance(DialogueActorSide.Right, 0.08f);
            _rightActorView.PlayEntrance(0.08f);
        }
    }

    public void ClearActors()
    {
        _leftActorView.SetEntity((EntityUid?) null);
        _rightActorView.SetEntity((EntityUid?) null);
        _leftActorView.ResetPresentation();
        _rightActorView.ResetPresentation();
        _leftActorEntranceDelay = 0f;
        _rightActorEntranceDelay = 0f;
        _leftActorEntranceElapsed = ActorEntranceDuration;
        _rightActorEntranceElapsed = ActorEntranceDuration;
        _leftActorPanel.Visible = false;
        _rightActorPanel.Visible = false;
        UpdateSceneVisibility();
    }

    public void SetActiveSpeaker(DialogueActorSide activeSide)
    {
        if (!_showActors)
            return;

        if (!_dimInactiveActors)
        {
            _leftActorView.SetPresentation(Color.White);
            _rightActorView.SetPresentation(Color.White);
            return;
        }

        var active = Color.White;
        var inactiveBrightness = Math.Clamp(_inactiveActorOpacity, 0.72f, 0.95f);
        // Slightly cool the inactive actor instead of driving their sprite toward
        // black; this preserves the player's body, clothes and silhouette.
        var inactive = new Color(
            inactiveBrightness,
            MathF.Min(inactiveBrightness + 0.025f, 1f),
            MathF.Min(inactiveBrightness + 0.06f, 1f),
            1f);
        _leftActorView.SetPresentation(activeSide == DialogueActorSide.Left ? active : inactive);
        _rightActorView.SetPresentation(activeSide == DialogueActorSide.Right ? active : inactive);
    }

    public void SetSpeakerName(string speakerName)
    {
        _speakerLabel.Text = speakerName;
        _speakerPlate.Visible = !string.IsNullOrWhiteSpace(speakerName);
        _speakerLabel.Visible = _speakerPlate.Visible;
        UpdateHeaderVisibility();
        UpdateBodyMeasureBounds();
    }

    public void SetLineType(DialogueLineType lineType)
    {
        switch (lineType)
        {
            case DialogueLineType.Thought:
                _currentBodyFontPath = BodyItalicFontPath;
                _currentBodyTextColor = ThoughtTextColor;
                break;
            case DialogueLineType.Narration:
                _currentBodyFontPath = BodyItalicFontPath;
                _currentBodyTextColor = NarrationTextColor;
                break;
            default:
                _currentBodyFontPath = BodyFontPath;
                _currentBodyTextColor = DialogueTextColor;
                break;
        }

        ApplyBodyFont(_bodyBaseFontSize);
    }

    public void SetChoices(
        IReadOnlyList<DialogueChoiceOptionData> choices,
        Func<DialogueTextData, string> localize)
    {
        ClearChoiceButtons();

        if (choices.Count == 0)
        {
            _choiceArea.Visible = false;
            UpdateBodyMeasureBounds();
            return;
        }

        _choiceArea.Visible = true;
        _choiceArea.MinSize = new Vector2(0f, GetChoiceAreaHeight(choices.Count));

        for (var i = 0; i < choices.Count; i++)
        {
            var choiceIndex = choices[i].ChoiceIndex;
            var button = new Button
            {
                Text = localize(choices[i].Text),
                Visible = false,
                // A button is created before the typewriter line finishes.
                // Reserve and arrange its final slot while hidden, otherwise a
                // just-revealed button can render one frame at (0, 0) before
                // LayoutContainer catches up on the following UI tick.
                ReservesSpace = true,
                StyleBoxOverride = ChoiceButtonNormalStyle
            };

            button.Label.FontColorOverride = ChoiceTextColor;
            var hovered = false;
            button.OnMouseEntered += _ =>
            {
                hovered = true;
                ApplyChoiceButtonStyle(button, hovered: true);
            };
            button.OnMouseExited += _ =>
            {
                hovered = false;
                ApplyChoiceButtonStyle(button, hovered: false);
            };
            button.OnButtonDown += _ => ApplyChoiceButtonStyle(button, hovered: true, pressed: true);
            button.OnButtonUp += _ => ApplyChoiceButtonStyle(button, hovered);
            button.OnPressed += _ => ChoiceSelected?.Invoke(choiceIndex);
            _choiceButtons.Add(button);
            _choiceArea.AddChild(button);
        }

        UpdateBodyMeasureBounds();
        LayoutChoiceButtons();
    }

    public void SetChoicesVisible(bool visible)
    {
        foreach (var button in _choiceButtons)
        {
            button.Visible = visible;
        }
    }

    public void SetChoicesDisabled(bool disabled)
    {
        foreach (var button in _choiceButtons)
        {
            button.Disabled = disabled;
            ApplyChoiceButtonStyle(button, hovered: false);
        }
    }

    public void ClearChoices()
    {
        ClearChoiceButtons();
        _choiceArea.Visible = false;
        _choiceArea.MinSize = Vector2.Zero;
        UpdateBodyMeasureBounds();
    }

    public string PrepareBodyText(string text)
    {
        UpdateBodyMeasureBounds();

        if (string.IsNullOrEmpty(text))
        {
            ApplyBodyFont(_bodyBaseFontSize);
            return text;
        }

        for (var fontSize = _bodyBaseFontSize; fontSize >= _bodyMinFontSize; fontSize--)
        {
            ApplyBodyFont(fontSize);

            if (DoesBodyTextFit(text))
                return text;
        }

        ApplyBodyFont(_bodyMinFontSize);
        return TruncateBodyText(text);
    }

    public void SetBodyText(string text, string? layoutText = null)
    {
        // Lay out against the complete line while only drawing the revealed
        // prefix. The old approach measured a different string on every
        // typewriter tick, so a word close to the right edge could repeatedly
        // jump between an unwrapped and wrapped line for one frame.
        layoutText ??= text;
        var message = new FormattedMessage();
        message.AddText(text);

        if (text.Length < layoutText.Length)
        {
            message.PushColor(Color.Transparent);
            message.AddText(layoutText[text.Length..]);
            message.Pop();
        }

        _bodyLabel.SetMessage(message, _currentBodyTextColor);
        _bodyLabel.Measure(new Vector2(_bodyMeasureWidth, _bodyMeasureHeight));
    }

    public void SetContinueVisible(bool visible)
    {
        _continueButton.Visible = visible;
        _continueButton.Disabled = false;

        if (!visible)
            _continueShimmerTime = 0f;
    }

    public void SetContinueText(string text)
    {
        _continueButton.Text = text;
    }

    public void SetCancelVisible(bool visible)
    {
        _cancelButton.Visible = visible;
        _cancelButton.Disabled = false;
        UpdateHeaderVisibility();
        UpdateBodyMeasureBounds();
    }

    public void SetCancelDisabled(bool disabled)
    {
        _cancelButton.Disabled = disabled;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (!args.Handled
            && args.Function == EngineKeyFunctions.EscapeMenu
            && _cancelButton.Visible
            && !_cancelButton.Disabled)
        {
            CancelPressed?.Invoke();
            args.Handle();
            return;
        }

        base.KeyBindDown(args);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateActorEntrances(args.DeltaSeconds);

        if (_panelEntranceElapsed < PanelEntranceDuration)
        {
            _panelEntranceElapsed = MathF.Min(PanelEntranceDuration, _panelEntranceElapsed + args.DeltaSeconds);
            var progress = SmoothStep(_panelEntranceElapsed / PanelEntranceDuration);
            _dialoguePanel.ModulateSelfOverride = Color.White.WithAlpha(progress);
        }

        if (!_continueButton.Visible || _continueButton.Disabled)
            return;

        _continueShimmerTime += args.DeltaSeconds;
        var shimmer = 0.5f + MathF.Sin(_continueShimmerTime * 4.2f) * 0.5f;
        _continueButton.Label.FontColorOverride = LerpColor(AccentGold, Color.White, shimmer * 0.65f);
    }

    private void AssignActor(DialogueActorSide side, EntityUid? entity)
    {
        if (entity == null || entity == EntityUid.Invalid)
            return;

        switch (side)
        {
            case DialogueActorSide.Left:
                _leftActorView.SetEntity(entity);
                _leftActorPanel.Visible = true;
                break;
            case DialogueActorSide.Right:
                _rightActorView.SetEntity(entity);
                _rightActorPanel.Visible = true;
                break;
        }
    }

    private void BeginActorEntrance(DialogueActorSide side, float delay)
    {
        switch (side)
        {
            case DialogueActorSide.Left:
                _leftActorEntranceDelay = MathF.Max(delay, 0f);
                _leftActorEntranceElapsed = 0f;
                ApplyActorPosition(
                    _leftActorPanel,
                    _leftActorTargetOffsetX - ActorEntranceDistance,
                    _leftActorTargetOffsetY);
                break;
            case DialogueActorSide.Right:
                _rightActorEntranceDelay = MathF.Max(delay, 0f);
                _rightActorEntranceElapsed = 0f;
                ApplyActorPosition(
                    _rightActorPanel,
                    _rightActorTargetOffsetX + ActorEntranceDistance,
                    _rightActorTargetOffsetY);
                break;
        }
    }

    private void UpdateActorEntrances(float frameTime)
    {
        UpdateActorEntrance(
            _leftActorPanel,
            _leftActorTargetOffsetX,
            _leftActorTargetOffsetY,
            -1f,
            ref _leftActorEntranceDelay,
            ref _leftActorEntranceElapsed,
            frameTime);
        UpdateActorEntrance(
            _rightActorPanel,
            _rightActorTargetOffsetX,
            _rightActorTargetOffsetY,
            1f,
            ref _rightActorEntranceDelay,
            ref _rightActorEntranceElapsed,
            frameTime);
    }

    private static void UpdateActorEntrance(
        Control actorPanel,
        float targetOffsetX,
        float targetOffsetY,
        float entryDirection,
        ref float delay,
        ref float elapsed,
        float frameTime)
    {
        if (!actorPanel.Visible || elapsed >= ActorEntranceDuration)
            return;

        if (delay > 0f)
        {
            delay = MathF.Max(0f, delay - frameTime);
            return;
        }

        elapsed = MathF.Min(ActorEntranceDuration, elapsed + frameTime);
        var progress = SmoothStep(elapsed / ActorEntranceDuration);
        var offsetX = targetOffsetX + entryDirection * ActorEntranceDistance * (1f - progress);
        ApplyActorPosition(actorPanel, offsetX, targetOffsetY);
    }

    private static void ApplyActorPosition(Control actorPanel, float offsetX, float offsetY)
    {
        var size = actorPanel.SetSize;
        SetMarginLeft(actorPanel, offsetX - size.X * 0.5f);
        SetMarginRight(actorPanel, offsetX + size.X * 0.5f);
        SetMarginTop(actorPanel, -offsetY - size.Y);
        SetMarginBottom(actorPanel, -offsetY);
    }

    private void ApplyDialoguePosition(DialogueWindowAnchor anchor, float windowMargin, Vector2 dialogueSize)
    {
        var halfWidth = dialogueSize.X * 0.5f;

        switch (anchor)
        {
            case DialogueWindowAnchor.BottomLeft:
                SetAnchorPreset(_dialoguePanel, LayoutPreset.BottomLeft);
                SetMarginLeft(_dialoguePanel, windowMargin);
                SetMarginRight(_dialoguePanel, windowMargin + dialogueSize.X);
                break;
            case DialogueWindowAnchor.BottomCenter:
                SetAnchorPreset(_dialoguePanel, LayoutPreset.CenterBottom);
                SetMarginLeft(_dialoguePanel, -halfWidth);
                SetMarginRight(_dialoguePanel, halfWidth);
                break;
            case DialogueWindowAnchor.BottomRight:
                SetAnchorPreset(_dialoguePanel, LayoutPreset.BottomRight);
                SetMarginLeft(_dialoguePanel, -(windowMargin + dialogueSize.X));
                SetMarginRight(_dialoguePanel, -windowMargin);
                break;
        }

        SetMarginTop(_dialoguePanel, -(windowMargin + dialogueSize.Y));
        SetMarginBottom(_dialoguePanel, -windowMargin);
    }

    private static float ResolveActorAlignmentOffset(float alignmentX, float actorWidth)
    {
        return (Math.Clamp(alignmentX, 0f, 1f) - 0.5f) * actorWidth;
    }

    private static float SmoothStep(float value)
    {
        var t = Math.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Color LerpColor(Color from, Color to, float progress)
    {
        var t = Math.Clamp(progress, 0f, 1f);
        return new Color(
            from.R + (to.R - from.R) * t,
            from.G + (to.G - from.G) * t,
            from.B + (to.B - from.B) * t,
            from.A + (to.A - from.A) * t);
    }

    private void UpdateSceneVisibility()
    {
        _dimPanel.Visible = _sceneDimVisible;
        _dialoguePanel.Visible = _sceneWindowVisible;
        _actorStage.Visible = _showActors && _sceneActorsVisible;
    }

    private void UpdateHeaderVisibility()
    {
        _header.Visible = _speakerPlate.Visible || _cancelButton.Visible;
        _headerSpacer.Visible = !_speakerPlate.Visible;
    }

    private void HandleDialoguePanelKeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Handled || args.Function != EngineKeyFunctions.UIClick)
            return;

        // Buttons stop mouse propagation themselves.  Every other part of the frame
        // completes the typewriter effect, without advancing the dialogue afterwards.
        TextAreaPressed?.Invoke();
        args.Handle();
    }

    /// <summary>
    /// Transparent click target with a large geometric close mark. It is drawn with regular UI
    /// lines rather than the cut-corner primitive, so its dark-red palette follows normal UI colour handling.
    /// </summary>
    private sealed class DialogueCloseButton : Button
    {
        public Color GlyphColor { get; set; }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            var size = PixelSize;
            var crossSize = MathF.Min(size.X, size.Y) * 0.84f;
            if (crossSize <= 0f)
                return;

            var center = size * 0.5f;
            var halfCrossSize = crossSize * 0.5f;
            var thickness = MathF.Max(4f, crossSize * 0.18f);
            var topLeft = center - new Vector2(halfCrossSize, halfCrossSize);
            var topRight = center + new Vector2(halfCrossSize, -halfCrossSize);
            var bottomLeft = center + new Vector2(-halfCrossSize, halfCrossSize);
            var bottomRight = center + new Vector2(halfCrossSize, halfCrossSize);

            DrawThickLine(handle, topLeft, bottomRight, thickness, GlyphColor);
            DrawThickLine(handle, topRight, bottomLeft, thickness, GlyphColor);
        }

        private static void DrawThickLine(
            DrawingHandleScreen handle,
            Vector2 start,
            Vector2 end,
            float thickness,
            Color color)
        {
            var direction = Vector2.Normalize(end - start);
            var perpendicular = new Vector2(-direction.Y, direction.X);
            var halfThickness = thickness * 0.5f;

            // A quarter-pixel spacing makes the diagonal bands overlap into one solid mark
            // instead of leaving the striped raster gaps visible at 45 degrees.
            for (var offset = -halfThickness; offset <= halfThickness; offset += 0.25f)
            {
                var lineOffset = perpendicular * offset;
                handle.DrawLine(start + lineOffset, end + lineOffset, color);
            }
        }
    }

    /// <summary>
    /// Adds a restrained cinema-like falloff without hiding the map entirely.
    /// </summary>
    private sealed class DialogueVignetteControl : Control
    {
        public DialogueVignetteControl()
        {
            MouseFilter = MouseFilterMode.Ignore;
            RectClipContent = true;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var size = PixelSize;
            if (size.X <= 0f || size.Y <= 0f)
                return;

            const int bands = 7;
            var bandHeight = MathF.Max(1f, size.Y * 0.055f);

            for (var i = 0; i < bands; i++)
            {
                var alpha = 0.028f + (bands - i) * 0.012f;
                var offset = i * bandHeight;
                handle.DrawRect(UIBox2.FromDimensions(new Vector2(0f, offset), new Vector2(size.X, bandHeight)), Color.Black.WithAlpha(alpha));
                handle.DrawRect(UIBox2.FromDimensions(new Vector2(0f, size.Y - offset - bandHeight), new Vector2(size.X, bandHeight)), Color.Black.WithAlpha(alpha));
            }
        }
    }

    private sealed class DialoguePortraitView : SpriteView
    {
        private const float EntranceDuration = 0.32f;
        private const float PresentationLerpSpeed = 10f;
        private const float EntranceScale = 0.9f;

        private float _dialogueScale = 10f;
        private float _entranceElapsed = EntranceDuration;
        private float _entranceDelay;
        private Color _currentTint = Color.White;
        private Color _targetTint = Color.White;
        private Color _entranceStartTint = Color.White;

        public void SetDialogueScale(float scale)
        {
            _dialogueScale = MathF.Max(scale, 0.01f);

            if (_entranceElapsed >= EntranceDuration && _entranceDelay <= 0f)
                Scale = new Vector2(_dialogueScale, _dialogueScale);
        }

        public void SetPresentation(Color tint)
        {
            _targetTint = tint;
        }

        public void PlayEntrance(float delay)
        {
            _entranceDelay = MathF.Max(delay, 0f);
            _entranceElapsed = 0f;
            _entranceStartTint = _targetTint.WithAlpha(0f);
            _currentTint = _entranceStartTint;
            Scale = new Vector2(_dialogueScale * EntranceScale, _dialogueScale * EntranceScale);
            ModulateSelfOverride = _currentTint;
        }

        public void ResetPresentation()
        {
            _entranceDelay = 0f;
            _entranceElapsed = EntranceDuration;
            _currentTint = Color.White;
            _targetTint = Color.White;
            _entranceStartTint = Color.White;
            Scale = new Vector2(_dialogueScale, _dialogueScale);
            ModulateSelfOverride = Color.White;
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            if (_entranceDelay > 0f)
            {
                _entranceDelay = MathF.Max(0f, _entranceDelay - args.DeltaSeconds);
                return;
            }

            if (_entranceElapsed < EntranceDuration)
            {
                _entranceElapsed = MathF.Min(EntranceDuration, _entranceElapsed + args.DeltaSeconds);
                var progress = SmoothStep(_entranceElapsed / EntranceDuration);
                _currentTint = LerpColor(_entranceStartTint, _targetTint, progress);
                var scale = _dialogueScale * (EntranceScale + (1f - EntranceScale) * progress);
                Scale = new Vector2(scale, scale);
            }
            else
            {
                _currentTint = LerpColor(
                    _currentTint,
                    _targetTint,
                    Math.Clamp(args.DeltaSeconds * PresentationLerpSpeed, 0f, 1f));
            }

            ModulateSelfOverride = _currentTint;
        }

        protected override void Draw(IRenderHandle renderHandle)
        {
            ForcePortraitLayersUnshaded();
            base.Draw(renderHandle);
        }

        private void ForcePortraitLayersUnshaded()
        {
            if (Entity?.Owner is not { } uid
                || !EntMan.HasComponent<DialoguePreviewMarkerComponent>(uid))
            {
                return;
            }

            var sprite = Sprite;
            if (sprite == null)
                return;

            List<int>? displacementLayers = null;

            for (var i = 0; i < sprite.AllLayers.Count(); i++)
            {
                if (!sprite.TryGetLayer(i, out var layer)
                    || layer.ShaderPrototype == "unshaded")
                {
                    continue;
                }

                if (layer.CopyToShaderParameters != null)
                {
                    displacementLayers ??= new List<int>();
                    displacementLayers.Add(i);
                    continue;
                }

                if (layer.ShaderPrototype == "DisplacedStencilDraw")
                {
                    sprite.LayerSetShader(i, "unshaded");
                    continue;
                }

                sprite.LayerSetShader(i, "unshaded");
            }

            if (displacementLayers == null)
                return;

            for (var i = displacementLayers.Count - 1; i >= 0; i--)
            {
                sprite.RemoveLayer(displacementLayers[i]);
            }
        }
    }

    private void ApplyTypography(DialogueSceneData scene)
    {
        _speakerFont = _resourceCache.GetFont(SpeakerFontPath, Math.Max(scene.SpeakerFontSize, 8));
        _continueFont = _resourceCache.GetFont(ContinueFontPath, Math.Max(scene.ContinueFontSize, 8));
        _bodyBaseFontSize = Math.Max(scene.BodyFontSize, 8);
        _bodyMinFontSize = Math.Min(
            _bodyBaseFontSize,
            Math.Max(MinimumBodyFontSize, (int) MathF.Floor(_bodyBaseFontSize * MinimumBodyFontScale)));

        _speakerLabel.FontOverride = _speakerFont;
        _continueButton.Label.FontOverride = _continueFont;
        ApplyBodyFont(_bodyBaseFontSize);
    }

    private void ApplyBodyFont(int fontSize)
    {
        var bodyFont = _resourceCache.GetFont(_currentBodyFontPath, Math.Max(fontSize, 8));

        _bodyLabel.Stylesheet = new Stylesheet(new[]
        {
            new StyleRule(
                new SelectorElement(typeof(RichTextLabel), null, BodyLabelStyleId, null),
                new[]
                {
                    new StyleProperty("font", bodyFont)
                })
        });

        _bodyLabel.ForceRunStyleUpdate();
    }

    private void UpdateBodyMeasureBounds()
    {
        var uiScale = MathF.Max(UIScale, 0.01f);
        var speakerHeight = _header.Visible
            ? MathF.Max(SpeakerPlateHeight, (_speakerFont?.GetLineHeight(uiScale) ?? 0f) / uiScale)
            : 0f;
        var continueHeight = MathF.Max(
            ContinueRowHeight,
            (_continueFont?.GetLineHeight(uiScale) ?? ContinueRowMinHeight * uiScale) / uiScale + ContinueBottomInset);
        var choiceHeight = _choiceArea.Visible
            ? _choiceArea.MinSize.Y
            : 0f;
        var dialogueWidth = _dialoguePanel.SetSize.X > 0f ? _dialoguePanel.SetSize.X : _dialoguePanel.MaxSize.X;
        var dialogueHeight = _dialoguePanel.SetSize.Y > 0f ? _dialoguePanel.SetSize.Y : _dialoguePanel.MaxSize.Y;
        var visibleChildren = 2; // body + footer

        if (_header.Visible)
            visibleChildren++;

        if (_choiceArea.Visible)
            visibleChildren++;

        // PanelContainer first removes its StyleBox's content margins, then
        // DialogueContent applies its own margin. Match that exact rectangle
        // here; a two/three-pixel disagreement is enough to destabilize a word
        // sitting on a wrap boundary.
        _bodyMeasureWidth = MathF.Max(
            1f,
            dialogueWidth - DialoguePanelStyle.MinimumSize.X - DialogueContentMargin * 2f);
        _bodyMeasureHeight = MathF.Max(
            1f,
            dialogueHeight - DialoguePanelStyle.MinimumSize.Y
            - DialogueContentMargin * 2f
            - speakerHeight
            - choiceHeight
            - continueHeight
            - DialogueContentSeparation * Math.Max(visibleChildren - 1, 0));
    }

    private bool DoesBodyTextFit(string text)
    {
        _bodyLabel.SetMessage(FormattedMessage.FromUnformatted(text), _currentBodyTextColor);
        _bodyLabel.Measure(new Vector2(_bodyMeasureWidth, _bodyMeasureHeight));
        return _bodyLabel.DesiredSize.Y <= _bodyMeasureHeight + 0.5f;
    }

    private void LayoutChoiceButtons()
    {
        if (_choiceButtons.Count == 0)
            return;

        var count = _choiceButtons.Count;
        var availableWidth = MathF.Max(1f, _bodyMeasureWidth);
        var rows = (count + MaxChoiceButtonsPerRow - 1) / MaxChoiceButtonsPerRow;
        _choiceArea.MinSize = new Vector2(0f, GetChoiceAreaHeight(count));

        for (var row = 0; row < rows; row++)
        {
            var firstIndex = row * MaxChoiceButtonsPerRow;
            var rowCount = Math.Min(MaxChoiceButtonsPerRow, count - firstIndex);
            var maxWidth = MathF.Max(64f, (availableWidth - ChoiceButtonGap * Math.Max(rowCount - 1, 0)) / rowCount);
            var minimumWidth = MathF.Min(ChoiceButtonMinWidth, maxWidth);
            var buttonWidth = MathF.Max(minimumWidth, MathF.Min(ChoiceButtonMaxWidth, maxWidth));
            var totalWidth = buttonWidth * rowCount + ChoiceButtonGap * Math.Max(rowCount - 1, 0);
            var firstCenter = -totalWidth * 0.5f + buttonWidth * 0.5f;
            var top = ChoiceAreaPadding + row * (ChoiceButtonHeight + ChoiceRowGap);

            for (var column = 0; column < rowCount; column++)
            {
                var button = _choiceButtons[firstIndex + column];
                var centerX = firstCenter + column * (buttonWidth + ChoiceButtonGap);
                var size = new Vector2(buttonWidth, ChoiceButtonHeight);

                button.MinSize = size;
                button.MaxSize = size;
                button.SetSize = size;
                SetAnchorPreset(button, LayoutPreset.CenterTop);
                SetMarginLeft(button, centerX - buttonWidth * 0.5f);
                SetMarginRight(button, centerX + buttonWidth * 0.5f);
                SetMarginTop(button, top);
                SetMarginBottom(button, top + ChoiceButtonHeight);
            }
        }
    }

    private static float GetChoiceAreaHeight(int count)
    {
        if (count <= 0)
            return 0f;

        var rows = (count + MaxChoiceButtonsPerRow - 1) / MaxChoiceButtonsPerRow;
        return ChoiceAreaPadding * 2f
               + rows * ChoiceButtonHeight
               + Math.Max(rows - 1, 0) * ChoiceRowGap;
    }

    private static StyleBoxFlat CreateChoiceButtonStyle(
        Color background,
        Color border,
        float borderThickness = 1f)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(borderThickness),
            ContentMarginLeftOverride = 14f,
            ContentMarginRightOverride = 14f,
            ContentMarginTopOverride = 4f,
            ContentMarginBottomOverride = 4f
        };
    }

    private static void ApplyChoiceButtonStyle(Button button, bool hovered, bool pressed = false)
    {
        button.StyleBoxOverride = button.Disabled
            ? ChoiceButtonDisabledStyle
            : pressed ? ChoiceButtonPressedStyle
            : hovered ? ChoiceButtonHoverStyle
            : ChoiceButtonNormalStyle;
        button.Label.FontColorOverride = button.Disabled ? ChoiceDisabledTextColor : ChoiceTextColor;
    }

    private void ApplyContinueButtonStyle(bool hovered)
    {
        _continueButton.StyleBoxOverride = ContinueIndicatorStyle;
    }

    private void ApplyCancelButtonColor(bool hovered)
    {
        _cancelButton.GlyphColor = hovered && !_cancelButton.Disabled
            ? CancelTextHoverColor
            : CancelTextColor;
    }

    private void ClearChoiceButtons()
    {
        foreach (var button in _choiceButtons)
        {
            button.Orphan();
        }

        _choiceButtons.Clear();
    }

    private string TruncateBodyText(string text)
    {
        if (DoesBodyTextFit(Ellipsis))
            return Ellipsis;

        var low = 0;
        var high = text.Length;
        var best = 0;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            var candidate = BuildEllipsizedText(text, mid);

            if (DoesBodyTextFit(candidate))
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return BuildEllipsizedText(text, best);
    }

    private static string BuildEllipsizedText(string text, int length)
    {
        if (length <= 0)
            return Ellipsis;

        var slice = text[..Math.Min(length, text.Length)].TrimEnd();
        var lastSpace = slice.LastIndexOf(' ');

        if (lastSpace > 0 && slice.Length - lastSpace <= 16)
            slice = slice[..lastSpace].TrimEnd();

        return string.IsNullOrEmpty(slice) ? Ellipsis : slice + Ellipsis;
    }
}
