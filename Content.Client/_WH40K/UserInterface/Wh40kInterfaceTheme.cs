using Content.Client.ContextMenu.UI;
using Content.Client.Examine;
using Content.Client.Resources;
using System.Numerics;
using Content.Client.Lobby.UI;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Controls.FancyTree;
using Content.Client.Verbs.UI;
using Content.Client._WH40K.CharacterCreation;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WH40K.UserInterface;

/// <summary>The shared palette and control surfaces of the approved Heritage interface.</summary>
internal static class Wh40kInterfaceTheme
{
    public const string PrimaryButton = "Wh40kPrimaryButton";
    public const string ControlsStyleClass = "Wh40kHeritageControls";
    private static IResourceCache _resources = default!;
    internal static readonly Color Text = Color.FromHex("#E9E1D0");
    internal static readonly Color MutedText = Color.FromHex("#AD9F86");
    internal static readonly Color Gold = Color.FromHex("#E1C17E");
    internal static readonly Color Border = Color.FromHex("#56452D");
    internal static readonly Color TableRow = Color.FromHex("#100D0A");
    internal static readonly Color TableAlternateRow = Color.FromHex("#211A12");
    internal static readonly Color TableHeader = Color.FromHex("#2A2116");

    internal static StyleBox BitmapSurface(string name, int horizontal = 10, int vertical = 7)
    {
        var box = new StyleBoxTexture
        {
            Texture = _resources.GetResource<TextureResource>($"/Textures/_WH40K/Interface/{name}.svg.192dpi.png").Texture,
            TextureScale = new Vector2(0.5f),
        };
        box.SetPatchMargin(StyleBox.Margin.All, 8);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, vertical);
        return box;
    }

    internal static StyleBox Surface(string background, string edge = "#56452D", int horizontal = 4, int vertical = 2, float radius = 4)
    {
        var box = new LobbyRoundedStyleBox
        {
            BackgroundColor = Color.FromHex(background), BorderColor = Color.FromHex(edge),
            BorderThickness = new Thickness(1), CornerRadius = radius,
            HighlightColor = Color.FromHex("#E4C37C10"),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontal);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, vertical);
        return box;
    }

    internal static StyleBox Card(int padding = 12) => Surface("#15120F", horizontal: padding, vertical: padding, radius: 5);
    internal static StyleBox PreviewPanel() => Surface("#15120F", "#75613D", 12, 12, 5);
    internal static StyleBox PreviewStage(int padding = 6) => Surface("#090F0D", "#8E784D", padding, padding, 5);

    internal static void UsePrimaryButton(Button button)
    {
        button.StyleBoxOverride = null;
        button.Label.FontColorOverride = null;
        button.AddStyleClass(PrimaryButton);
    }

    /// <summary>Shared setup for controls added dynamically to either character editor.</summary>
    internal static void ApplyControls(Control control)
    {
        if (control.HasStyleClass(ControlsStyleClass))
            return;
        control.AddStyleClass(ControlsStyleClass);
        if (control is SpinBox number)
            number.LineEditControl.AddStyleClass("Wh40kNumberField");
        if (control is Slider nativeSlider)
            nativeSlider.AddChild(new Wh40kSliderAppearance(nativeSlider));
        if (control is LineEdit field)
        {
            field.OnFocusEnter += _ => field.AddStyleClass("Wh40kFocusedField");
            field.OnFocusExit += _ => field.RemoveStyleClass("Wh40kFocusedField");
        }
        if (control is ColorableSlider slider)
        {
            var gradient = slider.BackgroundStyleBoxOverride;
            if (gradient is ColorSelectorStyleBox colorGradient)
            {
                colorGradient.Texture = Texture.White;
                colorGradient.SetPatchMargin(StyleBox.Margin.All, 0);
            }
            slider.BackgroundStyleBoxOverride = new Wh40kRecessedSliderStyleBox(Wh40kRecessedSliderStyleBox.Part.Background, gradient);
            slider.FillStyleBoxOverride = new StyleBoxFlat();
            slider.ForegroundStyleBoxOverride = new Wh40kRecessedSliderStyleBox(Wh40kRecessedSliderStyleBox.Part.Foreground);
            slider.GrabberStyleBoxOverride = new Wh40kRecessedSliderStyleBox(Wh40kRecessedSliderStyleBox.Part.Grabber);
            slider.MinHeight = 28;
            slider.MaxHeight = 28;
            slider.VerticalAlignment = Control.VAlignment.Center;
        }
        foreach (var child in control.Children)
            ApplyControls(child);
        control.OnChildAdded += ApplyControls;
    }

    internal static void UseSkinToneGradient(Slider slider)
    {
        slider.BackgroundStyleBoxOverride = new Wh40kRecessedSliderStyleBox(
            Wh40kRecessedSliderStyleBox.Part.Background, skinTones: true);
        slider.FillStyleBoxOverride = new StyleBoxFlat();
    }

    public static IEnumerable<StyleRule> CreateRules(IResourceCache resources)
    {
        _resources = resources;
        var window = new Wh40kWindowStyleBox();
        var heading = BitmapSurface("header", 6, 1);
        var field = BitmapSurface("field");
        var title = Wh40kOnboardingTypography.Display(resources, 13);
        var section = Wh40kOnboardingTypography.Display(resources, 17);
        var white = Color.White;

        yield return Element().Prop(Label.StylePropertyFontColor, Text);
        yield return Element().Prop(Label.StylePropertyFont, resources.GetFont(new[]
        {
            "/Fonts/NotoSans/NotoSans-Regular.ttf", "/Fonts/NotoSans/NotoSansSC-Regular.ttf",
            "/Fonts/NotoSans/NotoSansSymbols-Regular.ttf", "/Fonts/NotoSans/NotoSansSymbols2-Regular.ttf",
        }, 11));
        yield return Element<RichTextLabel>().Prop(Label.StylePropertyFontColor, Text);
        foreach (var type in new[] { typeof(Label), typeof(RichTextLabel) })
            yield return new StyleRule(new SelectorElement(type, new[] { StyleBase.StyleClassLabelSubText }, null, null), new[]
            {
                new StyleProperty(Label.StylePropertyFontColor, MutedText),
            });
        yield return Element<Label>().Class("WindowFooterText").Prop(Label.StylePropertyFontColor, MutedText);
        yield return Element<PanelContainer>().Class(StyleNano.StyleClassInset)
            .Prop(PanelContainer.StylePropertyPanel, Surface("#100D0A", horizontal: 0, vertical: 0));
        yield return Element<PanelContainer>().Class("Wh40kPersonalizationField")
            .Prop(PanelContainer.StylePropertyPanel, Surface("#100D0AEB", horizontal: 0, vertical: 0, radius: 3));
        yield return Element<PanelContainer>().Class("Wh40kPersonalizationCard")
            .Prop(PanelContainer.StylePropertyPanel, Card());
        foreach (var name in new[] { DefaultWindow.StyleClassWindowPanel, StyleBase.ClassAngleRect,
                     StyleNano.StyleClassBorderedWindowPanel, StyleNano.StyleClassTransparentBorderedWindowPanel })
            yield return Element<PanelContainer>().Class(name).Prop(PanelContainer.StylePropertyPanel, window)
                .Prop(Control.StylePropertyModulateSelf, white);

        foreach (var name in new[] { DefaultWindow.StyleClassWindowHeader, "windowHeaderAlert", "WindowHeadingBackground", "WindowHeadingBackgroundLight" })
            yield return Element<PanelContainer>().Class(name).Prop(PanelContainer.StylePropertyPanel, heading)
                .Prop(Control.StylePropertyModulateSelf, white);
        foreach (var name in new[] { DefaultWindow.StyleClassWindowTitle, "windowTitleAlert", "FancyWindowTitle" })
            yield return Element<Label>().Class(name).Prop(Label.StylePropertyFont, title)
                .Prop(Label.StylePropertyFontColor, Gold).Prop(Label.StylePropertyAlignMode, Label.AlignMode.Center);
        yield return Element<PanelContainer>().Class("windowHeaderAlert")
            .Prop(PanelContainer.StylePropertyPanel, Surface("#41241FE8", "#A96553", 8, 6));
        yield return Element<Label>().Class("windowTitleAlert").Prop(Label.StylePropertyFont, title)
            .Prop(Label.StylePropertyFontColor, Color.FromHex("#F0C8AB"));
        yield return Element<Label>().Class(StyleBase.StyleClassLabelHeading).Prop(Label.StylePropertyFont, section)
            .Prop(Label.StylePropertyFontColor, Gold);
        yield return Element<Label>().Class("Wh40kSectionHeading").Prop(Label.StylePropertyFont, section)
            .Prop(Label.StylePropertyFontColor, Gold).Prop(nameof(Control.Margin), new Thickness(0, 14, 0, 8));
        yield return Element<Label>().Class(StyleNano.StyleClassConsoleHeading)
            .Prop(Label.StylePropertyFont, title).Prop(Label.StylePropertyFontColor, Gold);
        yield return Element<Label>().Class(StyleNano.StyleClassConsoleSubHeading)
            .Prop(Label.StylePropertyFont, Wh40kOnboardingTypography.TextBold(resources, 10)).Prop(Label.StylePropertyFontColor, Gold);
        yield return Element<Label>().Class(StyleNano.StyleClassConsoleText).Prop(Label.StylePropertyFontColor, Text);

        foreach (var shape in new string?[] { null, StyleBase.ButtonOpenRight, StyleBase.ButtonOpenLeft,
                     StyleBase.ButtonOpenBoth, StyleBase.ButtonSquare, StyleBase.ButtonOpenRightSelected,
                     StyleBase.ButtonOpenBothSelected, StyleBase.ButtonCaution })
        {
            var classes = shape == null ? new[] { ContainerButton.StyleClassButton }
                : new[] { ContainerButton.StyleClassButton, shape };
            foreach (var rule in ButtonRules(typeof(ContainerButton), classes, shape == StyleBase.ButtonCaution,
                         shape == StyleBase.ButtonSquare ? 2 : 8))
                yield return rule;
        }
        foreach (var name in new[] { StyleNano.StyleClassStorageButton, ListContainer.StyleClassListContainerButton })
            foreach (var rule in ButtonRules(typeof(ContainerButton), new[] { name }, false, 4))
                yield return rule;
        foreach (var name in new[] { SpinBox.LeftButtonStyle, SpinBox.RightButtonStyle, SpinBox.MiddleButtonStyle })
            foreach (var rule in ButtonRules(typeof(Button), new[] { ContainerButton.StyleClassButton, name }, false, 4, 24, 2))
                yield return rule;
        foreach (var shape in new string?[] { null, StyleBase.ButtonOpenLeft, StyleBase.ButtonOpenRight, StyleBase.ButtonOpenBoth })
            foreach (var rule in ButtonRules(typeof(MonotoneButton), shape == null ? new[] { ContainerButton.StyleClassButton } : new[] { ContainerButton.StyleClassButton, shape }, false, 10, 24, 2))
                yield return rule;
        foreach (var rule in ButtonRules(typeof(Button), new[] { ContainerButton.StyleClassButton, "Wh40kCompactButton" }, false, 4, 24, 2))
            yield return rule;
        foreach (var rule in ButtonRules(typeof(ContainerButton), new[] { ContainerButton.StyleClassButton, "Wh40kCompactButton" }, false, 4, 24, 2))
            yield return rule;
        foreach (var shape in new string?[] { null, StyleBase.ButtonSquare, StyleBase.ButtonOpenLeft, StyleBase.ButtonOpenRight })
            foreach (var rule in ButtonRules(typeof(MenuButton), shape == null ? Array.Empty<string>() : new[] { shape }, false, 4))
                yield return rule;
        foreach (var rule in ButtonRules(typeof(MenuButton), new[] { MenuButton.StyleClassRedTopButton }, true, 4))
            yield return rule;
        foreach (var shape in new string?[] { null, StyleBase.ButtonSquare, StyleBase.ButtonOpenLeft, StyleBase.ButtonOpenRight, MenuButton.StyleClassRedTopButton })
            foreach (var state in new[] { ContainerButton.StylePseudoClassNormal, ContainerButton.StylePseudoClassHover, ContainerButton.StylePseudoClassPressed, ContainerButton.StylePseudoClassDisabled })
                yield return new StyleRule(new SelectorElement(typeof(MenuButton), shape == null ? null : new[] { shape }, null, new[] { state }), new[]
                {
                    new StyleProperty(ContainerButton.StylePropertyStyleBox, new Wh40kHudButtonStyleBox(state == ContainerButton.StylePseudoClassHover, state == ContainerButton.StylePseudoClassPressed)),
                    new StyleProperty(Control.StylePropertyModulateSelf, state == ContainerButton.StylePseudoClassDisabled ? Color.White.WithAlpha(0.38f) : white),
                });
        yield return Element<MenuButton>().Prop(MenuButton.StylePropertyIconColor, Color.FromHex("#C4B28F"))
            .Prop(MenuButton.StylePropertyIconHoverColor, Color.FromHex("#F0D49B"))
            .Prop(MenuButton.StylePropertyIconPressedColor, Gold);
        foreach (var rule in ButtonRules(typeof(ContextMenuElement), new[] { ContextMenuElement.StyleClassContextMenuButton }, false, 6))
            yield return rule;
        foreach (var rule in ButtonRules(typeof(ContextMenuElement), new[] { ConfirmationMenuElement.StyleClassConfirmationContextMenuButton }, true, 6))
            yield return rule;
        foreach (var rule in ButtonRules(typeof(ExamineButton), new[] { ExamineButton.StyleClassExamineButton }, false, 4))
            yield return rule;
        foreach (var state in new[] { ContainerButton.StylePseudoClassNormal, ContainerButton.StylePseudoClassHover,
                     ContainerButton.StylePseudoClassPressed, ContainerButton.StylePseudoClassDisabled })
        {
            yield return Element<Button>().Class(ContainerButton.StyleClassButton).Class(PrimaryButton).Pseudo(state)
                .Prop(ContainerButton.StylePropertyStyleBox, BitmapSurface("button-primary", 13, 7))
                .Prop(Control.StylePropertyModulateSelf, state == ContainerButton.StylePseudoClassDisabled ? Color.White.WithAlpha(0.38f) : white);
            foreach (var shape in new[] { StyleBase.ButtonOpenLeft, StyleBase.ButtonOpenRight, StyleBase.ButtonOpenBoth, StyleBase.ButtonSquare })
                yield return Element<Button>().Class(ContainerButton.StyleClassButton).Class(PrimaryButton).Class(shape).Pseudo(state)
                    .Prop(ContainerButton.StylePropertyStyleBox, BitmapSurface("button-primary", 13, 7))
                    .Prop(Control.StylePropertyModulateSelf, state == ContainerButton.StylePseudoClassDisabled ? Color.White.WithAlpha(0.38f) : white);
            yield return new StyleRule(new SelectorChild(
                new SelectorElement(typeof(Button), new[] { PrimaryButton }, null, new[] { state }),
                new SelectorElement(typeof(Label), null, null, null)), new[]
            {
                new StyleProperty(Label.StylePropertyFontColor,
                    Color.FromHex(state == ContainerButton.StylePseudoClassDisabled ? "#C1AD8D" : "#20160B")),
            });
            yield return Element<ConfirmButton>().Pseudo(ConfirmButton.ConfirmPrefix + state)
                .Prop(ContainerButton.StylePropertyStyleBox, BitmapSurface("button-danger"))
                .Prop(Control.StylePropertyModulateSelf, state == ContainerButton.StylePseudoClassDisabled ? Color.FromHex("#FFFFFF70") : white);
        }

        yield return Element<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, field)
            .Prop(LineEdit.StylePropertyCursorColor, Gold)
            .Prop(LineEdit.StylePropertySelectionColor, Color.FromHex("#84643790"));
        yield return Element<LineEdit>().Class("Wh40kFocusedField").Prop(LineEdit.StylePropertyStyleBox, BitmapSurface("field-focus"));
        yield return Element<LineEdit>().Class("Wh40kInvalidField").Prop(LineEdit.StylePropertyStyleBox, BitmapSurface("field-invalid"));
        yield return Element<LineEdit>().Class("Wh40kNumberField").Prop(LineEdit.StylePropertyStyleBox, BitmapSurface("field", 4, 2));
        yield return Element<LineEdit>().Class("Wh40kNumberField").Class("Wh40kFocusedField").Prop(LineEdit.StylePropertyStyleBox, BitmapSurface("field-focus", 4, 2));
        yield return Element<LineEdit>().Class(LineEdit.StyleClassLineEditNotEditable)
            .Prop(LineEdit.StylePropertyStyleBox, Surface("#15120FD9", "#56452D70", 10, 8, 4))
            .Prop(Label.StylePropertyFontColor, Color.FromHex("#81796B"));
        yield return Element<TextureRect>().Class(OptionButton.StyleClassOptionTriangle)
            .Prop(TextureRect.StylePropertyTextureSizeTarget, new Vector2(8))
            .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#ADA391"))
            .Prop(nameof(Control.Margin), new Thickness(8, 0, 0, 0));
        yield return Element<Label>().Class(OptionButton.StyleClassOptionButton).Prop(Label.StylePropertyAlignMode, Label.AlignMode.Left);
        yield return Element<LineEdit>().Pseudo(LineEdit.StylePseudoClassPlaceholder)
            .Prop(Label.StylePropertyFontColor, Color.FromHex("#9B8D75"));
        yield return Element<LineEdit>().Class(StyleNano.StyleClassActionSearchBox)
            .Prop(LineEdit.StylePropertyStyleBox, field);
        yield return Element<TextEdit>().Prop(Label.StylePropertyFontColor, Text)
            .Prop(TextEdit.StylePropertyCursorColor, Gold).Prop(TextEdit.StylePropertySelectionColor, Color.FromHex("#84643790"));
        yield return Element<TextEdit>().Pseudo(TextEdit.StylePseudoClassPlaceholder)
            .Prop(Label.StylePropertyFontColor, Color.FromHex("#9B8D75"));
        yield return Element<TabContainer>()
            .Prop(TabContainer.StylePropertyPanelStyleBox, new StyleBoxEmpty())
            .Prop(TabContainer.stylePropertyTabFontColor, Gold)
            .Prop(TabContainer.StylePropertyTabFontColorInactive, Color.FromHex("#AD9F86"))
            .Prop(TabContainer.StylePropertyTabStyleBox, Surface("#392B1C", "#BE9E64", 9, 5))
            .Prop(TabContainer.StylePropertyTabStyleBoxInactive, Surface("#18140F", horizontal: 9, vertical: 5));
        yield return Element<ItemList>().Prop(ItemList.StylePropertyBackground, field)
            .Prop(ItemList.StylePropertyItemBackground, Surface("#100D0AEB", "#79603D80", 4, 2, 3))
            .Prop(ItemList.StylePropertyDisabledItemBackground, Surface("#15120F", "#56452D70", 4, 2, 3))
            .Prop(ItemList.StylePropertySelectedItemBackground, Surface("#342719F5", "#C6A466", 4, 2, 3));
        yield return Element<Tree>().Prop(Tree.StylePropertyBackground, field)
            .Prop(Tree.StylePropertyItemBoxSelected, Surface("#342719", "#C6A466", 4, 1, 2));
        yield return Element<Slider>().Prop(nameof(Control.MinHeight), 28f)
            .Prop(Slider.StylePropertyBackground, new Wh40kRecessedSliderStyleBox(Wh40kRecessedSliderStyleBox.Part.Background))
            .Prop(Slider.StylePropertyFill, new Wh40kRecessedSliderStyleBox(Wh40kRecessedSliderStyleBox.Part.Fill))
            .Prop(Slider.StylePropertyForeground, new Wh40kRecessedSliderStyleBox(Wh40kRecessedSliderStyleBox.Part.Foreground))
            .Prop(Slider.StylePropertyGrabber, new Wh40kRecessedSliderStyleBox(Wh40kRecessedSliderStyleBox.Part.Grabber));
        foreach (var (name, checkedClass, kind) in new[]
        {
            (CheckBox.StyleClassCheckBox, CheckBox.StyleClassCheckBoxChecked, "check"),
            (MonotoneCheckBox.StyleClassCheckBox, MonotoneCheckBox.StyleClassCheckBoxChecked, "check"),
        })
        {
            foreach (var (extra, asset) in new[] { ("",kind), ("Wh40kSwitch","switch"), ("Wh40kRadio","radio") })
                foreach (var on in new[] { false, true })
                {
                    var classes = new List<string> { name };
                    if (extra.Length > 0) classes.Add(extra);
                    if (on) classes.Add(checkedClass);
                    yield return new StyleRule(new SelectorElement(typeof(TextureRect), classes.ToArray(), null, null), new[]
                    {
                        new StyleProperty(TextureRect.StylePropertyTexture, resources.GetResource<TextureResource>($"/Textures/_WH40K/Interface/{asset}-{(on ? "on" : "off")}.svg.192dpi.png").Texture),
                        new StyleProperty(TextureRect.StylePropertyTextureScale, new Vector2(0.5f)),
                        new StyleProperty(TextureRect.StylePropertyTextureSizeTarget, asset == "switch" ? new Vector2(33, 18) : new Vector2(16)),
                        new StyleProperty(Control.StylePropertyModulateSelf, white),
                    });
                }
            yield return Element<BoxContainer>().Class(name).Prop(BoxContainer.StylePropertySeparation, 9);
        }
        yield return Element<CheckBox>().Prop(nameof(Control.MinHeight), 30f);
        yield return Element<PanelContainer>().Class("Wh40kWindowFooter")
            .Prop(PanelContainer.StylePropertyPanel, Surface("#2B211762", "#92734070", 16, 12, 4));
        foreach (var (name, edge) in new[] { ("Wh40kNoticeInfo", "#7EAABF"), ("Wh40kNoticeWarning", "#D7B56D") })
        {
            var notice = new LobbyRoundedStyleBox { BackgroundColor = Color.FromHex("#1D1811D9"), BorderColor = Color.FromHex(edge), BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = 4 };
            notice.SetContentMarginOverride(StyleBox.Margin.Horizontal, 12); notice.SetContentMarginOverride(StyleBox.Margin.Vertical, 11);
            yield return Element<PanelContainer>().Class(name).Prop(PanelContainer.StylePropertyPanel, notice);
        }
        yield return Element<ProgressBar>().Prop(ProgressBar.StylePropertyBackground, Surface("#0C0A08", horizontal: 0, vertical: 0, radius: 2))
            .Prop(ProgressBar.StylePropertyForeground, new StyleBoxFlat { BackgroundColor = Color.FromHex("#B6995D") });
        foreach (var bar in new[] { typeof(HScrollBar), typeof(VScrollBar) })
        {
            foreach (var state in new string?[] { null, ScrollBar.StylePseudoClassHover, ScrollBar.StylePseudoClassGrabbed })
            {
                var background = state == ScrollBar.StylePseudoClassGrabbed ? "#C6A466E0"
                    : state == ScrollBar.StylePseudoClassHover ? "#AD8C54C0" : "#8D7340A0";
                yield return new StyleRule(new SelectorElement(bar, null, null, state == null ? null : new[] { state }), new[]
                {
                    new StyleProperty(ScrollBar.StylePropertyGrabber, Surface(background, "#BE9E6440", 4, 4, 2)),
                });
            }
        }
        foreach (var name in new[] { StyleNano.StyleClassTooltipPanel, ContextMenuPopup.StyleClassContextMenuPopup,
                     OptionButton.StyleClassOptionsBackground, "BackgroundDark", "PanelBackgroundBaseDark", "PanelBackgroundBaseLight", "PanelBackgroundLight", "BackgroundOpenRight", "BackgroundOpenLeft" })
            yield return Element<PanelContainer>().Class(name).Prop(PanelContainer.StylePropertyPanel, Surface("#15120FF5", horizontal: 6, vertical: 4))
                .Prop(Control.StylePropertyModulateSelf, white);
        foreach (var name in new[] { OptionButton.StyleClassOptionsBackground })
            yield return Element<PanelContainer>().Class(name).Prop(PanelContainer.StylePropertyPanel, Surface("#25211CFA", "#BE9E64", 6, 6));
        var contextGlass = new Wh40kWindowStyleBox();
        contextGlass.SetContentMarginOverride(StyleBox.Margin.Horizontal, 10);
        contextGlass.SetContentMarginOverride(StyleBox.Margin.Vertical, 10);
        yield return Element<PanelContainer>().Class(ContextMenuPopup.StyleClassContextMenuPopup)
            .Prop(PanelContainer.StylePropertyPanel, contextGlass);
        yield return Element<Label>().Class(Wh40kContextMenuBody.SectionLabelStyle)
            .Prop(Label.StylePropertyFont, Wh40kOnboardingTypography.Technical(resources, Wh40kOnboardingTypography.TechnicalSmallSize))
            .Prop(Label.StylePropertyFontColor, Color.FromHex("#BEA472"));
        foreach (var state in new[] { ContainerButton.StylePseudoClassNormal, ContainerButton.StylePseudoClassHover, ContainerButton.StylePseudoClassPressed, ContainerButton.StylePseudoClassDisabled })
            foreach (var (type, name, danger, verticalPadding) in new[]
                     {
                         (typeof(ContextMenuElement), ContextMenuElement.StyleClassContextMenuButton, false, Wh40kMenuItemStyleBox.ContextVerticalPadding),
                         (typeof(ConfirmationMenuElement), ConfirmationMenuElement.StyleClassConfirmationContextMenuButton, true, Wh40kMenuItemStyleBox.ContextVerticalPadding),
                         (typeof(Button), "Wh40kMenuItem", false, 7),
                     })
                yield return new StyleRule(new SelectorElement(type, new[] { name }, null, new[] { state }), new[]
                {
                    new StyleProperty(ContainerButton.StylePropertyStyleBox, new Wh40kMenuItemStyleBox(state is "hover" or "pressed", danger, verticalPadding)),
                    new StyleProperty(nameof(Control.MinHeight), 34f),
                });
        yield return Element<Tooltip>().Prop(PanelContainer.StylePropertyPanel, Surface("#25211CFA", "#BE9E64", 12, 10));
        yield return Element<PanelContainer>().Class("PaperInputBorder")
            .Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat
            {
                BackgroundColor = Color.Transparent,
                BorderColor = Color.FromHex("#79603D80"), BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6, ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4, ContentMarginBottomOverride = 4,
            });
        yield return Element<TextEdit>().Class("PaperLineEdit").Prop(Label.StylePropertyFontColor, Color.FromHex("#191919"))
            .Prop(TextEdit.StylePropertyCursorColor, Color.FromHex("#191919"));
        yield return Element<PanelContainer>().Class(StyleNano.StyleClassChatPanel)
            .Prop(PanelContainer.StylePropertyPanel, Surface("#15120FE8", horizontal: 0, vertical: 0));
        yield return Element<PanelContainer>().Class(StyleNano.StyleClassChatSubPanel)
            .Prop(PanelContainer.StylePropertyPanel, Surface("#100D0AEB", horizontal: 2, vertical: 2));
        yield return Element<PanelContainer>().Class(StyleNano.StyleClassHotbarPanel)
            .Prop(PanelContainer.StylePropertyPanel, Surface("#15120FE8", horizontal: 0, vertical: 0));
        yield return Element<PanelContainer>().Class(StyleBase.ClassLowDivider)
            .Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat { BackgroundColor = Color.FromHex("#8F744A66"), ContentMarginBottomOverride = 1 });
        yield return Element<PanelContainer>().Class(StyleBase.ClassHighDivider)
            .Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat { BackgroundColor = Color.FromHex("#8F744A66"), ContentMarginBottomOverride = 2 });
        yield return Element<StripeBack>().Prop(StripeBack.StylePropertyBackground, new StyleBoxFlat { BackgroundColor = Color.FromHex("#1A150FE0") })
            .Prop(StripeBack.StylePropertyEdgeColor, Color.FromHex("#8F744A66"));
        yield return Element<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton).Class(TreeItem.StyleClassSelected)
            .Prop(ContainerButton.StylePropertyStyleBox, Surface("#342719", "#C6A466", 1, 0, 2));
        yield return Element<ContainerButton>().Identifier(TreeItem.StyleIdentifierTreeButton).Pseudo(ContainerButton.StylePseudoClassHover)
            .Prop(ContainerButton.StylePropertyStyleBox, Surface("#211A12", "#AD8C54", 1, 0, 2));
        foreach (var name in new[] { DefaultWindow.StyleClassWindowCloseButton, FancyWindow.StyleClassWindowHelpButton })
        {
            yield return Element<TextureButton>().Class(name)
                .Prop(TextureButton.StylePropertyTexture, resources.GetResource<TextureResource>($"/Textures/_WH40K/Interface/{(name == DefaultWindow.StyleClassWindowCloseButton ? "close" : "help")}.svg.192dpi.png").Texture)
                .Prop(Control.StylePropertyModulateSelf, white);
            yield return Element<TextureButton>().Class(name).Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#F0D49B"));
            yield return Element<TextureButton>().Class(name).Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(Control.StylePropertyModulateSelf, Color.FromHex("#BE9E64"));
        }
    }

    private static IEnumerable<StyleRule> ButtonRules(Type type, string[] classes, bool danger, int padding, int minHeight = 36, int verticalPadding = 7)
    {
        foreach (var state in new string?[] { null, ContainerButton.StylePseudoClassNormal, ContainerButton.StylePseudoClassHover,
                     ContainerButton.StylePseudoClassPressed, ContainerButton.StylePseudoClassDisabled })
        {
            var selected = state == ContainerButton.StylePseudoClassPressed ||
                Array.IndexOf(classes, StyleBase.ButtonOpenBothSelected) >= 0 ||
                Array.IndexOf(classes, StyleBase.ButtonOpenRightSelected) >= 0;
            var hover = state == ContainerButton.StylePseudoClassHover;
            var disabled = state == ContainerButton.StylePseudoClassDisabled;
            yield return new StyleRule(new SelectorElement(type, classes, null, state == null ? null : new[] { state }), new[]
            {
                new StyleProperty(ContainerButton.StylePropertyStyleBox, BitmapSurface(danger ? "button-danger" : selected ? "button-selected" : hover ? "button-hover" : "button", padding, verticalPadding)),
                new StyleProperty(Control.StylePropertyModulateSelf, disabled ? Color.White.WithAlpha(0.38f) : Color.White),
                new StyleProperty(nameof(Control.MinHeight), (float) minHeight),
            });
            yield return new StyleRule(new SelectorChild(new SelectorElement(type, classes, null, state == null ? null : new[] { state }),
                new SelectorElement(typeof(Label), null, null, null)), new[]
            {
                new StyleProperty(Label.StylePropertyFontColor, danger ? Color.FromHex("#EF8E72") : selected ? Gold : disabled ? Color.FromHex("#817865") : Text),
            });
        }
    }
}
