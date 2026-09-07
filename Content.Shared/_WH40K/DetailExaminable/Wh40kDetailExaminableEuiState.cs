using Content.Shared.Eui;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.DetailExaminable;

[Serializable, NetSerializable]
public sealed class Wh40kDetailExaminableEuiState : EuiStateBase
{
    public NetEntity Target;
    public string Name;
    public ProtoId<SpeciesPrototype> Species;
    public Sex Sex;
    public Gender Gender;
    public bool ShowTraits;
    public bool ShowOoc;
    public bool ShowLinks;
    public bool ShowPreferences;
    public bool ShowTags;
    public string FlavorText;
    public string OocFlavorText;
    public string CharacterFlavorText;
    public string GreenFlavorText;
    public string YellowFlavorText;
    public string RedFlavorText;
    public string TagsFlavorText;
    public string LinksFlavorText;
    public string? PortraitId;

    public Wh40kDetailExaminableEuiState(
        NetEntity target,
        string name,
        ProtoId<SpeciesPrototype> species,
        Sex sex,
        Gender gender,
        bool showTraits,
        bool showOoc,
        bool showLinks,
        bool showPreferences,
        bool showTags,
        string flavorText,
        string oocFlavorText,
        string characterFlavorText,
        string greenFlavorText,
        string yellowFlavorText,
        string redFlavorText,
        string tagsFlavorText,
        string linksFlavorText,
        string? portraitId)
    {
        Target = target;
        Name = name;
        Species = species;
        Sex = sex;
        Gender = gender;
        ShowTraits = showTraits;
        ShowOoc = showOoc;
        ShowLinks = showLinks;
        ShowPreferences = showPreferences;
        ShowTags = showTags;
        FlavorText = flavorText;
        OocFlavorText = oocFlavorText;
        CharacterFlavorText = characterFlavorText;
        GreenFlavorText = greenFlavorText;
        YellowFlavorText = yellowFlavorText;
        RedFlavorText = redFlavorText;
        TagsFlavorText = tagsFlavorText;
        LinksFlavorText = linksFlavorText;
        PortraitId = portraitId;
    }
}
