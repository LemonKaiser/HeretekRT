namespace Content.Shared._WH40K.Activities;

/// <summary>
/// Curated expedition payloads. The director selects a scenario; its layout is selected once
/// from this catalogue, independently of YAML weights and of subsequent executor updates.
/// </summary>
public static class KoronusExpeditionCatalog
{
    public static readonly IReadOnlyList<KoronusExpeditionProfile> Profiles =
    [
        new(KoronusActivityExecutionKind.ExpeditionMonolith, "KoronusExpeditionRelicCasket",
            "MobKoronusExpeditionEcho", KoronusActivitySalvageProfile.Archeotech,
            KoronusActivityTrophyConsumer.AstropathicRelay,
            Paths("/Maps/_Mono/Monolithic/", "monolithicasteroidsmallarena",
                "monolithicasteroidsmallspindle", "monolithicasteroidsmallship")),
        new(KoronusActivityExecutionKind.ExpeditionWeaponsCache, "KoronusExpeditionContrabandLedger",
            "MobKoronusExpeditionHeretic", KoronusActivitySalvageProfile.Engineering,
            KoronusActivityTrophyConsumer.CargoOfficio, Paths("/Maps/_NF/Bluespace/", "cache")),
        new(KoronusActivityExecutionKind.ExpeditionVault, "KoronusExpeditionSealedBonds",
            "MobKoronusExpeditionRaider", KoronusActivitySalvageProfile.Engineering,
            KoronusActivityTrophyConsumer.CargoOfficio, Paths("/Maps/_NF/Bluespace/", "vault", "vaultsmall")),
        new(KoronusActivityExecutionKind.ExpeditionRaiderBoarding, "KoronusExpeditionContrabandLedger",
            "MobKoronusExpeditionRaider", KoronusActivitySalvageProfile.Engineering,
            KoronusActivityTrophyConsumer.CargoOfficio,
            Paths("/Maps/_NF/Bluespace/", "syndieftlintercept")),
        new(KoronusActivityExecutionKind.ExpeditionWreckSalvage, "KoronusExpeditionDataCore",
            null, KoronusActivitySalvageProfile.Engineering, KoronusActivityTrophyConsumer.AivoriusArchive,
            Paths("/Maps/_NF/Shuttles/Scrap/", "bison", "disciple", "nugget", "orange", "point",
                "scrap-investigator", "scrap-hauler", "scrap-crescent", "scrap-barge", "scrap-courserx",
                "scrap-comet", "scrap-sprinter")),
        new(KoronusActivityExecutionKind.ExpeditionWreckRescue, "KoronusExpeditionSurvivor",
            null, KoronusActivitySalvageProfile.Medical, KoronusActivityTrophyConsumer.Medicae,
            Paths("/Maps/_Mono/ShuttleEvent/Scrap/", "scrap-medicus")),
        new(KoronusActivityExecutionKind.ExpeditionInfestedShip, "KoronusExpeditionXenoSample",
            "MobHormagaunt", KoronusActivitySalvageProfile.Xenology,
            KoronusActivityTrophyConsumer.AstropathicRelay,
            Paths("/Maps/_Mono/ShuttleEvent/", "sakuratsu_chimera", "olympus_chimera", "tethys_chimera",
                "legionnaire_chimera", "yukikaze_chimera", "pebble_chimera", "neptune_chimera",
                "kite_chimera", "cinqueda_chimera", "mudskipper_chimera", "gadfly_chimera", "fracture_chimera")),
        new(KoronusActivityExecutionKind.ExpeditionMachineSalvage, "KoronusExpeditionDataCore",
            "MobWH40KNecronSpirit", KoronusActivitySalvageProfile.Archeotech,
            KoronusActivityTrophyConsumer.AivoriusArchive, Paths("/Maps/_Mono/Bluespace/", "prefracture")),
        Dungeon(KoronusActivityExecutionKind.ExpeditionBasaltSurvey, "Basalt", "KoronusExpeditionOreCore",
            null, KoronusActivitySalvageProfile.Minerals, KoronusActivityTrophyConsumer.MinersGuild),
        Dungeon(KoronusActivityExecutionKind.ExpeditionSnowRescue, "Snow", "KoronusExpeditionSurvivor",
            "MobKoronusExpeditionEcho", KoronusActivitySalvageProfile.Medical, KoronusActivityTrophyConsumer.Medicae),
        Dungeon(KoronusActivityExecutionKind.ExpeditionCaveSurvey, "Cave", "KoronusExpeditionXenoSample",
            "MobHormagaunt", KoronusActivitySalvageProfile.Xenology, KoronusActivityTrophyConsumer.AstropathicRelay),
        Dungeon(KoronusActivityExecutionKind.ExpeditionChromiteSurvey, "Chromite", "KoronusExpeditionOreCore",
            "MobKoronusExpeditionRaider", KoronusActivitySalvageProfile.Minerals, KoronusActivityTrophyConsumer.MinersGuild),
        Dungeon(KoronusActivityExecutionKind.ExpeditionScrapSurvey, "Scrap", "KoronusExpeditionDataCore",
            "MobKoronusExpeditionRaider", KoronusActivitySalvageProfile.Engineering, KoronusActivityTrophyConsumer.AivoriusArchive),
    ];

    public static bool TryGet(KoronusActivityExecutionKind execution, out KoronusExpeditionProfile profile)
    {
        foreach (var candidate in Profiles)
        {
            if (candidate.Execution != execution)
                continue;

            profile = candidate;
            return true;
        }

        profile = default!;
        return false;
    }

    public static bool IsExpedition(KoronusActivityExecutionKind execution) => TryGet(execution, out _);

    private static string[] Paths(string directory, params string[] names)
    {
        var paths = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            paths[i] = $"{directory}{names[i]}.yml";
        }
        return paths;
    }

    private static KoronusExpeditionProfile Dungeon(KoronusActivityExecutionKind execution, string biome,
        string objective, string? guardian, KoronusActivitySalvageProfile salvage, KoronusActivityTrophyConsumer consumer) =>
        new(execution, objective, guardian, salvage, consumer, Array.Empty<string>(), $"KoronusExpedition{biome}");
}

public sealed record KoronusExpeditionProfile(
    KoronusActivityExecutionKind Execution,
    string Objective,
    string? Guardian,
    KoronusActivitySalvageProfile Salvage,
    KoronusActivityTrophyConsumer Consumer,
    IReadOnlyList<string> MapPaths,
    string? DungeonConfig = null)
{
    public string? SelectMap(uint seed) => MapPaths.Count == 0 ? null : MapPaths[(int) (seed % (uint) MapPaths.Count)];
}
