using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Restricts IC character names to alphanumeric chars.
    /// </summary>
    public static readonly CVarDef<bool> RestrictedNames =
        CVarDef.Create("ic.restricted_names", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Allows flavor text (character descriptions)
    /// </summary>
    public static readonly CVarDef<bool> FlavorText =
        CVarDef.Create("ic.flavor_text", true, CVar.SERVER | CVar.REPLICATED); // Frontier: true

    /// <summary>
    ///     Enables the character-traits section of extended character descriptions.
    /// </summary>
    public static readonly CVarDef<bool> FlavorTraitsEnabled =
        CVarDef.Create("ic.flavor_traits_enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Enables the OOC notes, tags and links section of extended character descriptions.
    /// </summary>
    public static readonly CVarDef<bool> FlavorOocEnabled =
        CVarDef.Create("ic.flavor_ooc_enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Enables the links field of extended character descriptions.
    /// </summary>
    public static readonly CVarDef<bool> FlavorLinksEnabled =
        CVarDef.Create("ic.flavor_links_enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Enables green, yellow and red roleplay preferences.
    /// </summary>
    public static readonly CVarDef<bool> FlavorGyrEnabled =
        CVarDef.Create("ic.flavor_gyr_enabled", true, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> OocFlavorTextLength =
        CVarDef.Create("ic.oocflavor_text_length", 4500, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> CharacterDescriptionLength =
        CVarDef.Create("ic.character_description_length", 4500, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> GreenPreferencesLength =
        CVarDef.Create("ic.green_preferences_length", 4500, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> YellowPreferencesLength =
        CVarDef.Create("ic.yellow_preferences_length", 4500, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> RedPreferencesLength =
        CVarDef.Create("ic.red_preferences_length", 4500, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> FlavorTagsLength =
        CVarDef.Create("ic.tags_length", 256, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> FlavorLinksLength =
        CVarDef.Create("ic.links_length", 512, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Adds a period at the end of a sentence if the sentence ends in a letter.
    /// </summary>
    public static readonly CVarDef<bool> ChatPunctuation =
        CVarDef.Create("ic.punctuation", true, CVar.SERVER); // Frontier: true

    /// <summary>
    ///     Enables automatically forcing IC name rules. Uppercases the first letter of the first and last words of the name
    /// </summary>
    public static readonly CVarDef<bool> ICNameCase =
        CVarDef.Create("ic.name_case", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Whether or not players' characters are randomly generated rather than using their selected characters in the creator.
    /// </summary>
    public static readonly CVarDef<bool> ICRandomCharacters =
        CVarDef.Create("ic.random_characters", false, CVar.SERVER);

    /// <summary>
    ///     A weighted random prototype used to determine the species selected for random characters.
    /// </summary>
    public static readonly CVarDef<string> ICRandomSpeciesWeights =
        CVarDef.Create("ic.random_species_weights", "SpeciesWeights", CVar.SERVER);

    /// <summary>
    ///     Control displaying SSD indicators near players
    /// </summary>
    public static readonly CVarDef<bool> ICShowSSDIndicator =
        CVarDef.Create("ic.show_ssd_indicator", true, CVar.CLIENTONLY);
}
