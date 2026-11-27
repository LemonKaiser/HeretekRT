using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Biotype;

public static class Wh40kBiotypeGates
{
    public static Wh40kBiotype Effective(HumanoidCharacterProfile profile, IPrototypeManager protoManager)
    {
        return protoManager.TryIndex<SpeciesPrototype>(profile.Species, out var species)
            ? Wh40kBiotypeRules.Resolve(profile.Wh40kBuild.Biotype, species)
            : Wh40kBiotypeRules.Sanitize(profile.Wh40kBuild.Biotype);
    }

    public static string NameKey(Wh40kBiotype biotype)
    {
        return biotype switch
        {
            Wh40kBiotype.Cybernetic => "wh40k-biotype-cybernetic",
            _ => "wh40k-biotype-organic",
        };
    }

    public static string TooltipKey(Wh40kBiotype biotype)
    {
        return biotype switch
        {
            Wh40kBiotype.Cybernetic => "wh40k-biotype-cybernetic-tooltip",
            _ => "wh40k-biotype-organic-tooltip",
        };
    }

    public static bool TraitBlocked(TraitPrototype trait, HumanoidCharacterProfile profile, IPrototypeManager protoManager)
    {
        return trait.BiotypeBlacklist.Count > 0 &&
               trait.BiotypeBlacklist.Contains(Effective(profile, protoManager));
    }
}
