using Content.Client._WH40K.CharacterCreation;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Factions;

internal static class FactionSocialUi
{
    public static PanelContainer Card(bool accent = false) => new()
    {
        HorizontalExpand = true,
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(accent ? "#302A23" : "#202021"),
            ContentMarginLeftOverride = 16, ContentMarginRightOverride = 16,
            ContentMarginTopOverride = 12, ContentMarginBottomOverride = 12,
        },
    };

    public static Label Heading(string text, int size = 18) => new()
    {
        Text = text, ToolTip = text, ClipText = true, FontColorOverride = FactionUiPresentation.Gold,
        FontOverride = Wh40kOnboardingTypography.Text(IoCManager.Resolve<IResourceCache>(), size),
    };

    public static RichTextLabel Body(string text, bool muted = false)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.AddStyleClass("Wh40kOnboardingBodyCompact");
        label.SetMessage(text, muted ? FactionUiPresentation.Muted : FactionUiPresentation.Text);
        return label;
    }

    public static LobbyFactionButton Button(string key, bool primary = false) => new() { Text = Loc.GetString(key), Primary = primary };
}
