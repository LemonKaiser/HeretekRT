using Content.Shared.Preferences;

namespace Content.Shared.DetailExaminable;

[RegisterComponent]
public sealed partial class DetailExaminableComponent : Component
{
    [DataField]
    public string Content = string.Empty;

    [DataField]
    public string CharacterContent { get; set; } = string.Empty;

    [DataField]
    public string OocContent { get; set; } = string.Empty;

    [DataField]
    public string TagsContent { get; set; } = string.Empty;

    [DataField]
    public string LinksContent { get; set; } = string.Empty;

    [DataField]
    public string GreenContent { get; set; } = string.Empty;

    [DataField]
    public string YellowContent { get; set; } = string.Empty;

    [DataField]
    public string RedContent { get; set; } = string.Empty;

    [DataField]
    public string? PortraitId { get; set; }

    [DataField]
    public bool ShareOocContent { get; set; } = true;

    [DataField]
    public bool ShareLinksContent { get; set; } = true;

    [DataField]
    public bool SharePreferencesContent { get; set; } = true;

    public void SetProfile(HumanoidCharacterProfile profile)
    {
        Content = profile.FlavorText;
        CharacterContent = profile.CharacterFlavorText;
        OocContent = profile.OocFlavorText;
        TagsContent = profile.TagsFlavorText;
        LinksContent = profile.LinksFlavorText;
        GreenContent = profile.GreenFlavorText;
        YellowContent = profile.YellowFlavorText;
        RedContent = profile.RedFlavorText;
        PortraitId = profile.Wh40kBuild.PortraitId;
        ShareOocContent = profile.ShareOocFlavorText;
        ShareLinksContent = profile.ShareLinksFlavorText;
        SharePreferencesContent = profile.SharePreferencesFlavorText;
    }
}
