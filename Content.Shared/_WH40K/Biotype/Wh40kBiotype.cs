using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Biotype;

/// <summary>
/// Selects whether a character spawns with an organic human body or a Mechanicus-augmented
/// cybernetic chassis fitted with cheap WH40K prosthetics and an organic human brain.
/// </summary>
[Serializable, NetSerializable]
public enum Wh40kBiotype : byte
{
    Organic = 0,
    Cybernetic = 1,
}

public static class Wh40kBiotypeRules
{
    /// <summary>
    /// Base layer overrides applied to the client character-creator preview mannequin
    /// when Cybernetic (Mechanicus) is selected, matching the in-game cheap WH40K prosthetics.
    /// </summary>
    public static readonly IReadOnlyDictionary<HumanoidVisualLayers, string> CheapProstheticPreviewLayers =
        new Dictionary<HumanoidVisualLayers, string>
        {
            [HumanoidVisualLayers.LArm] = "WH40KProstheticCheapLeftArmLayer",
            [HumanoidVisualLayers.LHand] = "WH40KProstheticCheapLeftHandLayer",
            [HumanoidVisualLayers.RArm] = "WH40KProstheticCheapRightArmLayer",
            [HumanoidVisualLayers.RHand] = "WH40KProstheticCheapRightHandLayer",
            [HumanoidVisualLayers.LLeg] = "WH40KProstheticCheapLeftLegLayer",
            [HumanoidVisualLayers.LFoot] = "WH40KProstheticCheapLeftFootLayer",
            [HumanoidVisualLayers.RLeg] = "WH40KProstheticCheapRightLegLayer",
            [HumanoidVisualLayers.RFoot] = "WH40KProstheticCheapRightFootLayer",
        };

    public static bool CanChoose(SpeciesPrototype species)
    {
        if (species.ID == "IPC")
            return false;

        return species.CanChooseBiotype;
    }

    public static Wh40kBiotype DefaultFor(SpeciesPrototype species)
    {
        if (species.ID == "IPC")
            return Wh40kBiotype.Cybernetic;

        return Enum.IsDefined(species.DefaultBiotype)
            ? species.DefaultBiotype
            : Wh40kBiotype.Organic;
    }

    public static Wh40kBiotype Resolve(Wh40kBiotype requested, SpeciesPrototype species)
    {
        if (!CanChoose(species))
            return DefaultFor(species);

        return Enum.IsDefined(requested) ? requested : DefaultFor(species);
    }

    public static Wh40kBiotype Sanitize(Wh40kBiotype requested)
    {
        return Enum.IsDefined(requested) ? requested : Wh40kBiotype.Organic;
    }
}
