using Content.Client._WH40K.UserInterface;
using Content.Client.Humanoid;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared.Chat.TypingIndicator;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.CharacterCreation;

public sealed class Wh40kSpeechStylePicker : BoxContainer
{
    private const int CardGap = 16;
    private static readonly Type[] PreviewTags = [typeof(FontTag)];
    private readonly OptionButton _font = new() { HorizontalExpand = true };
    internal readonly RichTextLabel Preview = new() { HorizontalAlignment = HAlignment.Left };
    public EyeColorPicker Eyes { get; } = new();
    public CheckBox EyeGlowCheckBox { get; } = new()
    {
        Text = Loc.GetString("wh40k-marking-glow-eyes"),
        ToolTip = Loc.GetString("wh40k-marking-glow-eyes-tooltip"),
        Visible = false,
    };
    private readonly BoxContainer _settings = new() { SeparationOverride = CardGap };
    private readonly PanelContainer _fontCard;
    private readonly PanelContainer _eyesCard;
    private readonly Wh40kPreviewGrid _bubbles = new();
    private readonly Dictionary<string, ContainerButton> _bubbleTiles = new();
    private readonly IPrototypeManager _prototypes = IoCManager.Resolve<IPrototypeManager>();
    private readonly IResourceCache _resources = IoCManager.Resolve<IResourceCache>();
    private Wh40kSpeechStyle? _style;
    private bool _disabled;

    public event Action<Wh40kSpeechStyle>? StyleSelected;

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            _font.Disabled = value;
            Eyes.Disabled = value;
            EyeGlowCheckBox.Disabled = value;
            _bubbles.SetDisabled(value);
        }
    }

    public Wh40kSpeechStylePicker()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = CardGap;
        _fontCard = Card("wh40k-personalization-chat-font", _font, Preview);
        _eyesCard = Card("wh40k-personalization-eye-color", Eyes, EyeGlowCheckBox);
        _settings.AddChild(_fontCard);
        _settings.AddChild(_eyesCard);
        AddChild(_settings);
        AddChild(Card("wh40k-personalization-typing-bubble", _bubbles));
        OnResized += LayoutSettings;

        _font.AddItem(Loc.GetString("wh40k-personalization-speech-default"), 0);
        for (var index = 0; index < Wh40kSpeechStyle.Fonts.Count; index++)
            _font.AddItem(Loc.GetString($"wh40k-personalization-font-{Wh40kSpeechStyle.Fonts[index]}"), index + 1);

        _font.OnItemSelected += args =>
        {
            var font = args.Id == 0 ? null : Wh40kSpeechStyle.Fonts[args.Id - 1];
            Pick(new Wh40kSpeechStyle(font, _style?.TypingBubble));
        };
        AddBubble(null);
        foreach (var bubble in Wh40kSpeechStyle.Bubbles)
            AddBubble(bubble);
        SetData(default);
    }

    private static PanelContainer Card(string label, params Control[] controls)
    {
        var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        content.AddChild(new Label { Text = Loc.GetString(label), StyleClasses = { "LabelHeading" } });
        foreach (var control in controls)
            content.AddChild(control);

        return new PanelContainer { PanelOverride = Wh40kInterfaceTheme.Card(14), Children = { content } };
    }

    private void LayoutSettings()
    {
        var sideBySide = Width >= 640;
        _settings.Orientation = sideBySide ? LayoutOrientation.Horizontal : LayoutOrientation.Vertical;
        _fontCard.SetWidth = _eyesCard.SetWidth = sideBySide ? (Width - CardGap) / 2 : float.NaN;
    }

    public void SetData(Wh40kSpeechStyle style)
    {
        style = style.Validated();
        if (_style == style)
            return;

        _style = style;
        var selection = 0;
        for (var index = 0; index < Wh40kSpeechStyle.Fonts.Count; index++)
        {
            if (Wh40kSpeechStyle.Fonts[index] == style.Font)
                selection = index + 1;
        }
        _font.SelectId(selection);
        var preview = FormattedMessage.EscapeText(Loc.GetString("wh40k-personalization-speech-preview"));
        Preview.SetMessage(FormattedMessage.FromMarkupOrThrow($"[font=\"{style.Font ?? FontTag.DefaultFont}\" size=14]{preview}[/font]"),
            PreviewTags);

        if (_bubbleTiles.TryGetValue(style.TypingBubble ?? string.Empty, out var selected))
            selected.Pressed = true;
    }

    private void AddBubble(string? id)
    {
        if (!_prototypes.TryIndex<TypingIndicatorPrototype>(id ?? "default", out var proto))
            return;

        var icon = _resources.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / proto.SpritePath, out var resource) &&
                   resource.RSI.TryGetState(proto.TypingState, out var state)
            ? state : null;
        var title = Loc.GetString(id == null ? "wh40k-personalization-speech-default" : $"wh40k-personalization-bubble-{id}");
        var tile = _bubbles.AddBubbleTile(title, icon, () => Pick(new Wh40kSpeechStyle(_style?.Font, id)));
        _bubbleTiles.Add(id ?? string.Empty, tile);
    }

    internal void Pick(Wh40kSpeechStyle style)
    {
        if (Disabled)
            return;

        style = style.Validated();
        SetData(style);
        StyleSelected?.Invoke(style);
    }
}
