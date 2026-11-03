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
            KoronusActivityTrophyConsumer.AstropathicRelay, Array.Empty<string>(), "WH40KChaosDungeon"),
        new(KoronusActivityExecutionKind.ExpeditionMachineSalvage, "KoronusExpeditionDataCore",
            "MobWH40KNecronSpirit", KoronusActivitySalvageProfile.Archeotech,
            KoronusActivityTrophyConsumer.AivoriusArchive, Array.Empty<string>(), "WH40KNecronTomb"),
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
