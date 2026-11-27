using System.Numerics;
using Content.Client._WH40K.CharacterCreation;
using Content.Shared._WH40K.Biotype;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Biotype;

/// <summary>
/// Two-option segmented selector for switching between Organic and Cybernetic (Mechanicus) biotypes,
/// styled for both WH40K onboarding and profile personalization.
/// </summary>
public sealed class Wh40kBiotypePicker : BoxContainer
{
    private static readonly ResPath OrganicRsi = new("/Textures/Mobs/Species/Human/organs.rsi");
    private const string OrganicState = "heart-on";
    private static readonly UIBox2 OrganicCrop = UIBox2.FromDimensions(8, 7, 12, 12);

    private static readonly ResPath CyberneticRsi = new("/Textures/_WH40K/Objects/Augments/augments.rsi");
    private const string CyberneticState = "headimplants-icon";
    private static readonly UIBox2 CyberneticCrop = UIBox2.FromDimensions(8, 9, 15, 15);

    private readonly Button _organicButton;
    private readonly Button _cyberneticButton;
    private readonly TextureRect _organicIcon;
    private readonly TextureRect _cyberneticIcon;
    private readonly ButtonGroup _group = new();
    private bool _disabled;
    private bool _canChoose = true;
    private bool _updating;
    private bool _iconsLoaded;

    public event Action<Wh40kBiotype>? OnSelected;

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            _organicButton.Disabled = value || !_canChoose;
            _cyberneticButton.Disabled = value || !_canChoose;
        }
    }

    public Wh40kBiotypePicker()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 0;

        (_organicButton, _organicIcon) = CreateOptionButton(Wh40kBiotype.Organic, "OpenRight");
        (_cyberneticButton, _cyberneticIcon) = CreateOptionButton(Wh40kBiotype.Cybernetic, "OpenLeft");

        AddChild(_organicButton);
        AddChild(_cyberneticButton);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        EnsureIconsLoaded();
    }

    public void SetSelected(Wh40kBiotype selected, bool canChoose = true)
    {
        EnsureIconsLoaded();
        _canChoose = canChoose;
        Disabled = _disabled;
        _updating = true;
        _organicButton.Pressed = selected == Wh40kBiotype.Organic;
        _cyberneticButton.Pressed = selected == Wh40kBiotype.Cybernetic;
        _updating = false;
    }

    public void Select(Wh40kBiotype biotype)
    {
        var target = biotype == Wh40kBiotype.Cybernetic ? _cyberneticButton : _organicButton;
        if (target.Disabled)
            return;

        SetSelected(biotype);
        OnSelected?.Invoke(biotype);
    }

    private void EnsureIconsLoaded()
    {
        if (_iconsLoaded)
            return;

        _iconsLoaded = true;
        var cache = IoCManager.Resolve<IResourceCache>();
        _organicIcon.Texture = TryLoadIcon(cache, OrganicRsi, OrganicState, OrganicCrop);
        _cyberneticIcon.Texture = TryLoadIcon(cache, CyberneticRsi, CyberneticState, CyberneticCrop);
    }

    private static Texture? TryLoadIcon(IResourceCache cache, ResPath rsiPath, string stateName, UIBox2? crop = null)
    {
        if (!cache.TryGetResource<RSIResource>(rsiPath, out var resource) ||
            !resource.RSI.TryGetState(stateName, out var state))
        {
            return null;
        }

        var tex = state.Frame0;
        if (crop is not { } region)
            return tex;

        if (tex is AtlasTexture atlas)
        {
            var shifted = UIBox2.FromDimensions(atlas.SubRegion.TopLeft + region.TopLeft, region.Size);
            return new AtlasTexture(atlas.SourceTexture, shifted);
        }

        return new AtlasTexture(tex, region);
    }

    private (Button Button, TextureRect Icon) CreateOptionButton(Wh40kBiotype biotype, string styleClass)
    {
        var font = Wh40kOnboardingTypography.Text(IoCManager.Resolve<IResourceCache>(), 13);

        var button = new Button
        {
            ToggleMode = true,
            Group = _group,
            MinHeight = 40,
            ToolTip = Loc.GetString(Wh40kBiotypeGates.TooltipKey(biotype)),
            HorizontalExpand = true,
            StyleClasses = { styleClass },
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(10, 3),
        };

        var icon = new TextureRect
        {
            SetSize = new Vector2(28, 28),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            VerticalAlignment = VAlignment.Center,
        };

        var label = new Label
        {
            Text = Loc.GetString(Wh40kBiotypeGates.NameKey(biotype)),
            FontOverride = font,
            VerticalAlignment = VAlignment.Center,
        };

        row.AddChild(icon);
        row.AddChild(label);
        button.AddChild(row);

        button.OnToggled += args =>
        {
            if (_updating || !args.Pressed)
                return;

            OnSelected?.Invoke(biotype);
        };

        return (button, icon);
    }
}
