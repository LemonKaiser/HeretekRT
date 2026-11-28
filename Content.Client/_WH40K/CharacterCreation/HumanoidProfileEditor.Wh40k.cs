using System.Numerics;
using Content.Client._WH40K.CharacterCreation;
using Content.Client._WH40K.UserInterface;
using Robust.Client.ResourceManagement;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Content.Client.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private Vector2 _lastPersonalizationSize;
    private Vector2 _lastPersonalizationSurfaceSize;
    private bool _appearancePreviewPending;
    private float _appearancePreviewElapsed;
    private bool _personalizationLayoutPending;

    internal void RefreshCustomSpeciesNamePermission()
    {
        var whitelisted = _requirements.IsWhitelisted();
        CustomSpeciesNameEdit.Editable = whitelisted && !IsWh40kAppearanceEditingLocked;
        CustomSpeciesNameEdit.ToolTip = whitelisted ? null : Loc.GetString("humanoid-profile-editor-custom-species-whitelist");

        if (CustomSpeciesNameEdit.Editable || Profile is not { } profile)
            return;

        var savedName = CharacterSlot is { } slot &&
                        _preferencesManager.Preferences?.Characters.GetValueOrDefault(slot) is HumanoidCharacterProfile saved
            ? saved.CustomSpeciesName
            : string.Empty;

        if (profile.CustomSpeciesName == savedName)
            return;

        Profile = profile.WithCustomSpeciesName(savedName);
        UpdateCustomSpeciesNameEdit();
        SetDirty();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_personalizationLayoutPending)
        {
            _personalizationLayoutPending = false;
            UpdatePersonalizationLayout();
        }
        _appearancePreviewElapsed += args.DeltaSeconds;
        if (!_appearancePreviewPending || _appearancePreviewElapsed < 1f / 30f)
            return;
        FlushPersonalizationPreview();
    }

    internal void FlushPersonalizationPreview()
    {
        if (!_appearancePreviewPending)
            return;
        _appearancePreviewPending = false;
        _appearancePreviewElapsed = 0;
        if (Profile == null || !_entManager.EntityExists(PreviewDummy))
            return;
        _entManager.System<HumanoidAppearanceSystem>().LoadProfile(PreviewDummy, Profile);
        SetDirty();
    }

    private void UpdatePersonalizationPreviewScale()
    {
        if (Profile == null || !_entManager.TryGetComponent<SpriteComponent>(PreviewDummy, out var sprite))
            return;
        sprite.Scale = new Vector2(Profile.Appearance.Width, Profile.Appearance.Height);
    }

    private void InitializeWh40kLayout()
    {
        Wh40kInterfaceTheme.ApplyControls(this);
        Wh40kInterfaceTheme.UseSkinToneGradient(Skin);
        ProfileHighlight.PanelOverride = Wh40kInterfaceTheme.Card();
        EditorSurface.PanelOverride = new OnboardingPageFrameStyleBox
        {
            BackgroundColor = Color.FromHex("#15120FED"),
            BorderColor = Color.FromHex("#56452D"),
            ContentMarginLeftOverride = 16,
            ContentMarginRightOverride = 16,
            ContentMarginTopOverride = 16,
            ContentMarginBottomOverride = 16,
        };
        foreach (var card in new[] { IdentityCard, BodyCard, HairCard, BeardCard })
            card.PanelOverride = Wh40kInterfaceTheme.Card(14);
        PreviewPanel.PanelOverride = Wh40kInterfaceTheme.PreviewPanel();
        PreviewStage.PanelOverride = Wh40kInterfaceTheme.PreviewStage();
        PortraitButton.Bind(Wh40kPortraitFrame, Wh40kPortrait);
        ActionPanel.PanelOverride = Wh40kInterfaceTheme.Card(8);
        Wh40kInterfaceTheme.UsePrimaryButton(SaveButton);
        PreviewTitle.FontOverride = Wh40kOnboardingTypography.Display(
            IoCManager.Resolve<IResourceCache>(), Wh40kOnboardingTypography.PreviewTitleSize);
        BarkVoicePlayButton.Text = Loc.GetString("wh40k-personalization-listen");
        ProfileTabs.Bind(TabContainer);
        FilePopup.Orphan();
        FilePopup.OnPopupHide += FilePopup.Orphan;
        FilesButton.OnPressed += _ => TogglePersonalizationFiles();
        foreach (var button in new[] { ImportButton, ExportButton, ExportImageButton, OpenImagesButton })
            button.OnPressed += _ => FilePopup.Close();

        OnResized += () => _personalizationLayoutPending = true;
        EditorSurface.OnResized += () => _personalizationLayoutPending = true;
    }

    internal void TogglePersonalizationFiles() => Wh40kPersonalizationTheme.ToggleMenu(FilePopup, FilesButton, above: true);

    private void UpdatePersonalizationLayout()
    {
        if (Vector2.DistanceSquared(Size, _lastPersonalizationSize) < 0.25f &&
            Vector2.DistanceSquared(EditorSurface.Size, _lastPersonalizationSurfaceSize) < 0.25f)
            return;
        _lastPersonalizationSize = Size;
        _lastPersonalizationSurfaceSize = EditorSurface.Size;
        var narrow = Size.X < 1100;
        NameRow.Orientation = Size.X < 800 ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
        RandomizeRow.VerticalAlignment = VAlignment.Bottom;
        PreviewPanel.SetWidth = narrow ? 288 : Math.Clamp(Size.X * 0.285f, 340, 420);
        AppearanceColumns.Orientation = EditorSurface.Size.X < 960 ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
        HairColumns.Orientation = EditorSurface.Size.X < 900 ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
        UpdatePersonalizationPreviewLayout();
    }

    private void UpdatePersonalizationPreviewLayout()
    {
        // Use the viewport, rather than the card whose desired size these
        // visibility changes affect, to avoid a layout feedback loop.
        var viewportHeight = Root?.Size.Y ?? Size.Y;
        var shortPanel = viewportHeight < 800;
        HairStylePicker.SetCompactTiles(shortPanel);
        FacialHairPicker.SetCompactTiles(shortPanel);
        PreviewTitle.Visible = !shortPanel;
        PreviewName.Visible = !shortPanel;
        var spriteMargin = new Thickness(shortPanel ? 14 : 24);
        if (SpriteView.Margin != spriteMargin)
            SpriteView.Margin = spriteMargin;
    }

    private void UpdatePersonalizationStatus()
    {
        SaveStatus.Text = Loc.GetString(IsWh40kProfileEditingFullyLocked
            ? "wh40k-personalization-locked"
            : IsDirty ? "wh40k-personalization-unsaved" : "wh40k-personalization-saved");
        SaveStatus.ToolTip = SaveStatus.Text;
        SaveStatus.ClipText = true;
        PreviewName.ToolTip = Profile?.Name;
        SpriteView.ToolTip = Profile?.Name;
        if (Profile != null)
            PreviewName.SetMessage(FormattedMessage.FromUnformatted(Profile.Name));
    }
}
