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
    private const float ChoiceButtonMinHeight = 46f;
    private const int ChoiceButtonGap = 8;
    private const float ConversationColumnGap = 22f;
    private const float ChoiceColumnsMinWidth = 900f;
    private const float ChoiceRevealDuration = 0.18f;
    private const float PanelEntranceDuration = 0.24f;
    private const float PanelResizeDuration = 0.26f;
    private const float ActorEntranceDuration = 0.34f;
    private const float ActorEntranceDistance = 120f;
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
        new Color(14, 19, 24, 150),
        new Color(89, 83, 65, 170));
    private static readonly StyleBoxFlat ChoiceButtonHoverStyle = CreateChoiceButtonStyle(
        new Color(54, 45, 25, 220),
        AccentGoldBright);
    private static readonly StyleBoxFlat ChoiceButtonPressedStyle = CreateChoiceButtonStyle(
        new Color(37, 30, 18, 240),
        AccentGold);
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
    private readonly LayoutContainer _conversationColumns;
    private readonly PanelContainer _choicePanel;
    private readonly Label _choiceHint;
    private readonly BoxContainer _header;
    private readonly Control _headerSpacer;
    private readonly PanelContainer _speakerPlate;
    private readonly Label _speakerLabel;
    private readonly Label _operationLabel = new() { Visible = false, ClipText = true };
    public void SetOperationStatus(string text) { _operationLabel.Text = text; _operationLabel.Visible = text.Length > 0; }
    private readonly DialogueTypewriterLabel _bodyLabel;
    private readonly ScrollContainer _bodyScroll;
    private readonly ScrollContainer _choiceArea;
    private readonly BoxContainer _choiceList;
    private readonly BoxContainer _footer;
    private readonly DialogueCloseButton _cancelButton;
    private readonly Button _continueButton;
    private readonly List<DialogueChoiceButton> _choiceButtons = new();
    private DialogueSceneData? _scene;
    private Vector2? _layoutViewport;
    private bool _layoutHasChoices;
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
    private Font? _choiceFont;
    private string _currentBodyFontPath = BodyFontPath;
    private Color _currentBodyTextColor = DialogueTextColor;
    private float _panelEntranceElapsed = PanelEntranceDuration;
    private float _panelResizeElapsed = PanelResizeDuration;
    private float _panelResizeStartHeight;
    private float _panelResizeTargetHeight;
    private float _continueShimmerTime;
    private float _choiceRevealElapsed = ChoiceRevealDuration;
    private bool _choicesVisible;
    private bool _choicesDisabled;
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
            Name = "DialogueActorStage",
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
            Name = "DialoguePanel",
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
            ClipText = true,
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

        _bodyLabel = new DialogueTypewriterLabel
        {
            StyleIdentifier = BodyLabelStyleId,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Top,
            LineHeightScale = 1.08f
        };

        _bodyLabel.OnKeyBindDown += HandleDialoguePanelKeyBindDown;
        _bodyScroll = new ScrollContainer
        {
            Name = "DialogueBodyScroll",
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            ReserveScrollbarSpace = true
        };
        _bodyScroll.AddChild(_bodyLabel);
        _conversationColumns = new LayoutContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            InheritChildMeasure = false
        };
        _conversationColumns.AddChild(_bodyScroll);
        _dialogueContent.AddChild(_conversationColumns);

        _choiceArea = new ScrollContainer
        {
            Name = "DialogueChoicesScroll",
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            ReserveScrollbarSpace = true,
            Visible = false
        };

        _choiceList = new BoxContainer
        {
            Name = "DialogueChoicesList",
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = ChoiceButtonGap,
            Margin = new Thickness(0f, 2f, 6f, 2f)
        };
        _choiceArea.AddChild(_choiceList);
        _choiceArea.OnKeyBindDown += HandleDialoguePanelKeyBindDown;
        var choiceContent = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 8
        };
        choiceContent.AddChild(new Label
        {
            Text = Loc.GetString("heretek-dialogue-ui-your-response"),
            FontColorOverride = AccentGold,
            Margin = new Thickness(4f, 0f, 0f, 0f)
        });
        choiceContent.AddChild(_choiceArea);
        _choiceHint = new Label
        {
            Name = "DialogueChoicesHint",
            SetHeight = 20f,
            ClipText = true,
            HorizontalExpand = true,
            FontColorOverride = AccentGold,
            HorizontalAlignment = HAlignment.Right,
            MouseFilter = MouseFilterMode.Ignore
        };
        choiceContent.AddChild(_choiceHint);
        _choicePanel = new PanelContainer
        {
            Name = "DialogueChoicesPanel",
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.Transparent,
                BorderColor = new Color(118, 102, 70, 140),
                BorderThickness = new Thickness(1f, 0f, 0f, 0f),
                ContentMarginLeftOverride = 18f
            }
        };
        _choicePanel.AddChild(choiceContent);
        _conversationColumns.AddChild(_choicePanel);
        // Constrain both measurement and arrangement to the same anchored slots.
        // BoxContainer measures expanding children sequentially at wider sizes,
        // which can replace a rich label's wrapping between arrange passes.
        SetGrowHorizontal(_bodyScroll, GrowDirection.Constrain);
        SetGrowVertical(_bodyScroll, GrowDirection.Constrain);
        SetGrowHorizontal(_choicePanel, GrowDirection.Constrain);
        SetGrowVertical(_choicePanel, GrowDirection.Constrain);
        _dialogueContent.AddChild(_operationLabel);

        _footer = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Visible = false,
            MinSize = new Vector2(0f, ContinueRowHeight)
        };

        _footer.OnKeyBindDown += HandleDialoguePanelKeyBindDown;

        _footer.AddChild(new Control
        {
            HorizontalExpand = true
        });

        _continueButton = new Button
        {
            Text = Loc.GetString("heretek-dialogue-ui-continue"),
            Visible = false,
            ReservesSpace = true,
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
        _footer.AddChild(_continueButton);
        _dialogueContent.AddChild(_footer);
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
        _scene = scene;
        _layoutViewport = null;
        SetDimOpacity(scene.DimOpacity);
        SetCancelVisible(scene.AllowCancel);

        var actorWidth = MathF.Max(scene.ActorWidth, 1f);
        var actorHeight = MathF.Max(scene.ActorHeight, 1f);
        var actorGap = MathF.Max(scene.ActorGap, 0f);
        var actorOverlap = Math.Clamp(scene.ActorOverlap, 0f, actorHeight - 1f);
        var actorStageHeight = MathF.Max(1f, actorHeight - actorOverlap);
        var leftActorOffsetX = scene.LeftActorOffsetX;
        var rightActorOffsetX = scene.RightActorOffsetX;

        if (Math.Abs(leftActorOffsetX) < 0.01f && Math.Abs(rightActorOffsetX) < 0.01f)
        {
            var legacyActorBaseOffsetX = (actorWidth + actorGap) * 0.5f;
            leftActorOffsetX = -legacyActorBaseOffsetX + ResolveActorAlignmentOffset(scene.LeftActorAlignmentX, actorWidth);
            rightActorOffsetX = legacyActorBaseOffsetX + ResolveActorAlignmentOffset(scene.RightActorAlignmentX, actorWidth);
        }

        _leftActorPanel.SetSize = new Vector2(actorWidth, actorHeight);
        _rightActorPanel.SetSize = new Vector2(actorWidth, actorHeight);
        _actorStage.MinSize = new Vector2(0f, actorStageHeight);
        SetMarginLeft(_actorStage, scene.WindowMargin);
        SetMarginRight(_actorStage, scene.WindowMargin);
        _leftActorView.SetDialogueScale(scene.ActorScale);
        _rightActorView.SetDialogueScale(scene.ActorScale);
        _leftActorTargetOffsetX = leftActorOffsetX;
        _leftActorTargetOffsetY = scene.LeftActorOffsetY;
        _rightActorTargetOffsetX = rightActorOffsetX;
        _rightActorTargetOffsetY = scene.RightActorOffsetY;
        ApplyActorPosition(_leftActorPanel, _leftActorTargetOffsetX, _leftActorTargetOffsetY);
        ApplyActorPosition(_rightActorPanel, _rightActorTargetOffsetX, _rightActorTargetOffsetY);
        ApplyTypography(scene);
        InvalidateMeasure();

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
        _choicesVisible = false;
        _choicesDisabled = false;
        _choiceRevealElapsed = 0f;
        _choiceArea.Modulate = Color.Transparent;

        if (choices.Count == 0)
        {
            _choiceArea.Visible = false;
            _choicePanel.Visible = false;
            InvalidateMeasure();
            return;
        }

        _choiceArea.Visible = true;
        _choicePanel.Visible = true;
        _choiceArea.VScroll = 0f;
        _choiceArea.VScrollTarget = 0f;

        for (var i = 0; i < choices.Count; i++)
        {
            var choiceIndex = choices[i].ChoiceIndex;
            var text = localize(choices[i].Text);
            var button = new DialogueChoiceButton(text, _choiceFont, i + 1)
            {
                Disabled = true,
                HorizontalExpand = true,
                MinHeight = ChoiceButtonMinHeight,
                StyleBoxOverride = ChoiceButtonNormalStyle
            };

            button.TextColor = ChoiceTextColor;
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
            _choiceList.AddChild(button);
        }

        InvalidateMeasure();
    }

    public void SetChoicesVisible(bool visible)
    {
        if (visible && !_choicesVisible)
            _choiceRevealElapsed = 0f;

        _choicesVisible = visible;
        _choiceArea.Modulate = Color.White.WithAlpha(visible ? SmoothStep(_choiceRevealElapsed / ChoiceRevealDuration) : 0f);
        foreach (var button in _choiceButtons)
        {
            // Keep the complete list measured during speech, while preventing
            // interaction until its reveal. This also avoids a layout jump.
            button.Disabled = !visible || _choicesDisabled;
            button.ToolTip = visible ? button.Text : null;
            ApplyChoiceButtonStyle(button, hovered: false);
        }
    }

    public void SetChoicesDisabled(bool disabled)
    {
        _choicesDisabled = disabled;
        foreach (var button in _choiceButtons)
        {
            button.Disabled = disabled || !_choicesVisible;
            ApplyChoiceButtonStyle(button, hovered: false);
        }
    }

    public void ClearChoices()
    {
        ClearChoiceButtons();
        _choicesVisible = false;
        _choicesDisabled = false;
        _choiceArea.Visible = false;
        _choicePanel.Visible = false;
        _choiceHint.Text = string.Empty;
        InvalidateMeasure();
    }

    public string PrepareBodyText(string text)
    {
        // Preserve the entire line, including during typewriter playback. Long
        // speeches scroll instead of being shrunk or permanently truncated.
        ApplyBodyFont(_bodyBaseFontSize);
        _bodyScroll.VScroll = 0f;
        _bodyScroll.VScrollTarget = 0f;
        return text;
    }

    public void SetBodyText(string text, string? layoutText = null)
    {
        _bodyLabel.SetRevealedText(layoutText ?? text, text.Length, _currentBodyTextColor);
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
        if (_layoutViewport is { } viewport)
            UpdateDialogueBounds(viewport);
        UpdatePanelResize(args.DeltaSeconds);
        UpdateActorEntrances(args.DeltaSeconds);
        if (_choicesVisible)
        {
            _choiceRevealElapsed = MathF.Min(ChoiceRevealDuration, _choiceRevealElapsed + args.DeltaSeconds);
            _choiceArea.Modulate = Color.White.WithAlpha(SmoothStep(_choiceRevealElapsed / ChoiceRevealDuration));
        }
        UpdateChoiceHint();

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

            // Apply speaker dimming immediately once the entrance animation has
            // completed. Otherwise a new line briefly renders the previous
            // tint before the presentation lerp catches up.
            if (_entranceElapsed >= EntranceDuration && _entranceDelay <= 0f)
            {
                _currentTint = tint;
                ModulateSelfOverride = tint;
            }
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
        _choiceFont = _resourceCache.GetFont(BodyFontPath, Math.Max(scene.BodyFontSize - 2, 14));

        _speakerLabel.FontOverride = _speakerFont;
        _continueButton.Label.FontOverride = _continueFont;
        ApplyBodyFont(_bodyBaseFontSize);
    }

    private void ApplyBodyFont(int fontSize)
    {
        var bodyFont = _resourceCache.GetFont(_currentBodyFontPath, Math.Max(fontSize, 8));
        _bodyLabel.SetFont(bodyFont);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        UpdateDialogueBounds(availableSize);
        return base.MeasureOverride(availableSize);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        // Size is assigned after ArrangeOverride. Use the incoming viewport so
        // children receive their final bounds on the same layout pass.
        UpdateDialogueBounds(finalSize);
        var result = base.ArrangeOverride(finalSize);
        UpdateChoiceHint();
        return result;
    }

    private void UpdateDialogueBounds(Vector2 viewport)
    {
        if (_scene is not { } scene)
            return;

        var hasChoices = _choicePanel.Visible;
        // UI-scale conversion can introduce sub-pixel roundoff between measure
        // and arrange. It must not cancel or restart a height transition.
        var sameViewport = _layoutViewport is { } previousViewport && previousViewport.EqualsApprox(viewport, 0.01f);
        if (sameViewport && _layoutHasChoices == hasChoices)
            return;

        // A new scene or a viewport resize must fit immediately. Only changes
        // within an already visible dialogue animate from its current height.
        var animateResize = sameViewport && _dialoguePanel.VisibleInTree;
        _layoutViewport = viewport;
        _layoutHasChoices = hasChoices;
        var baseHeight = MathF.Max(scene.WindowMinHeight, scene.WindowMaxHeight);
        var width = MathF.Min(scene.WindowWidth, MathF.Max(1f, viewport.X - scene.WindowMargin * 2f));
        var stacked = width < ChoiceColumnsMinWidth;
        SetAnchorAndMarginPreset(_bodyScroll, LayoutPreset.Wide);
        SetAnchorAndMarginPreset(_choicePanel, LayoutPreset.Wide);
        if (hasChoices)
        {
            if (stacked)
            {
                SetAnchorBottom(_bodyScroll, 1f / 3f);
                SetMarginBottom(_bodyScroll, -ConversationColumnGap / 2f);
                SetAnchorTop(_choicePanel, 1f / 3f);
                SetMarginTop(_choicePanel, ConversationColumnGap / 2f);
            }
            else
            {
                const float bodyShare = 1.15f / 2.15f;
                SetAnchorRight(_bodyScroll, bodyShare);
                SetMarginRight(_bodyScroll, -ConversationColumnGap / 2f);
                SetAnchorLeft(_choicePanel, bodyShare);
                SetMarginLeft(_choicePanel, ConversationColumnGap / 2f);
            }
        }
        // Keep the footer's slot throughout ordinary speech, including while
        // its indicator is hidden, so finishing the typewriter cannot move text.
        _footer.Visible = !hasChoices;
        // Reserve a viewport-based space for a choice step before the answers
        // appear. Counts, wrapping and typewriter updates never resize it.
        // Ordinary speech retains the scene's compact authored dimensions.
        var choiceHeight = stacked
            ? Math.Clamp(viewport.Y * 0.65f, 340f, 440f)
            : Math.Clamp(viewport.Y * 0.42f, 300f, 360f);
        var requestedHeight = hasChoices ? MathF.Max(baseHeight, choiceHeight) : baseHeight;
        var height = MathF.Min(requestedHeight, MathF.Max(1f, viewport.Y - scene.WindowMargin * 2f));
        _panelResizeStartHeight = _dialoguePanel.SetHeight;
        _panelResizeTargetHeight = height;
        _panelResizeElapsed = animateResize && float.IsFinite(_panelResizeStartHeight) && _panelResizeStartHeight != height
            ? 0f
            : PanelResizeDuration;
        ApplyDialogueSize(width, _panelResizeElapsed < PanelResizeDuration ? _panelResizeStartHeight : height);
    }

    private void UpdatePanelResize(float deltaSeconds)
    {
        if (_panelResizeElapsed >= PanelResizeDuration)
            return;

        _panelResizeElapsed = MathF.Min(PanelResizeDuration, _panelResizeElapsed + deltaSeconds);
        var height = _panelResizeElapsed >= PanelResizeDuration
            ? _panelResizeTargetHeight
            : MathHelper.Lerp(_panelResizeStartHeight, _panelResizeTargetHeight, SmoothStep(_panelResizeElapsed / PanelResizeDuration));
        ApplyDialogueSize(_dialoguePanel.SetWidth, height);
    }

    private void ApplyDialogueSize(float width, float height)
    {
        if (_scene is not { } scene)
            return;

        var size = new Vector2(width, height);
        _dialoguePanel.MinSize = size;
        _dialoguePanel.MaxSize = size;
        _dialoguePanel.SetSize = size;
        ApplyDialoguePosition(scene.WindowAnchor, scene.WindowMargin, size);

        // Keep portraits attached to the top of the dialogue as the frame grows.
        var actorStageHeight = _actorStage.MinSize.Y;
        var actorBottomOffset = MathF.Max(scene.WindowMargin + height - MathF.Max(scene.ActorWindowOverlap, 0f), 0f);
        SetMarginTop(_actorStage, -(actorStageHeight + actorBottomOffset) + scene.ActorStageOffsetY);
        SetMarginBottom(_actorStage, -actorBottomOffset - scene.ActorStageOffsetY);
    }

    private void UpdateChoiceHint()
    {
        var overflowing = _choiceButtons.Count > 0
                          && _choicesVisible
                          && _choiceList.Height > _choiceArea.Height + 1f;
        _choiceHint.Text = overflowing ? Loc.GetString("heretek-dialogue-ui-scroll-responses") : string.Empty;
    }

    private static StyleBoxFlat CreateChoiceButtonStyle(
        Color background,
        Color border)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(3f, 0f, 0f, 0f),
            ContentMarginLeftOverride = 14f,
            ContentMarginRightOverride = 14f,
            ContentMarginTopOverride = 4f,
            ContentMarginBottomOverride = 4f
        };
    }

    private static void ApplyChoiceButtonStyle(DialogueChoiceButton button, bool hovered, bool pressed = false)
    {
        button.StyleBoxOverride = button.Disabled
            ? ChoiceButtonDisabledStyle
            : pressed ? ChoiceButtonPressedStyle
            : hovered ? ChoiceButtonHoverStyle
            : ChoiceButtonNormalStyle;
        button.TextColor = button.Disabled ? ChoiceDisabledTextColor : ChoiceTextColor;
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
            button.Dispose();
        }

        _choiceButtons.Clear();
    }

}
