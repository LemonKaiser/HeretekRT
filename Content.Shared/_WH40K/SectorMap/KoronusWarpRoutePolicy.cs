namespace Content.Shared._WH40K.SectorMap;

public enum KoronusWarpRisk : byte
{
    Stable,
    Dangerous,
    Unstable,
}

public enum KoronusWarpIncident : byte
{
    None,
    Turbulence,
    Rift,
    Boarders,
    Vertigo,
    Blackout,
    PsychicStorm,
    Hunt,
    CorrosiveBreach,
}

public static class KoronusWarpRoutePolicy
{
    public static KoronusWarpRisk GetRisk(string routeClass, int modifier = 0)
    {
        var risk = routeClass switch
        {
            "Dangerous" => 1,
            "Unstable" or "Forbidden" => 2,
            _ => 0,
        };
        return (KoronusWarpRisk) Math.Clamp(risk + modifier, 0, 2);
    }

    public static float GetIncidentChance(KoronusWarpRisk risk) => risk switch
    {
        KoronusWarpRisk.Dangerous => 0.45f,
        KoronusWarpRisk.Unstable => 0.8f,
        _ => 0.1f,
    };

    public static KoronusWarpIncident SelectIncident(KoronusWarpRisk risk, float chanceRoll, float kindRoll,
        string destination)
    {
        if (destination == "Footfall" || chanceRoll >= GetIncidentChance(risk))
            return KoronusWarpIncident.None;
        return risk switch
        {
            KoronusWarpRisk.Unstable when kindRoll < 0.2f => KoronusWarpIncident.Boarders,
            KoronusWarpRisk.Unstable when kindRoll < 0.35f => KoronusWarpIncident.Hunt,
            KoronusWarpRisk.Unstable when kindRoll < 0.5f => KoronusWarpIncident.CorrosiveBreach,
            KoronusWarpRisk.Unstable when kindRoll < 0.7f => KoronusWarpIncident.Rift,
            KoronusWarpRisk.Unstable when kindRoll < 0.85f => KoronusWarpIncident.PsychicStorm,
            KoronusWarpRisk.Unstable => KoronusWarpIncident.Blackout,
            KoronusWarpRisk.Dangerous when kindRoll < 0.35f => KoronusWarpIncident.Rift,
            KoronusWarpRisk.Dangerous when kindRoll < 0.6f => KoronusWarpIncident.Blackout,
            KoronusWarpRisk.Dangerous when kindRoll < 0.8f => KoronusWarpIncident.PsychicStorm,
            KoronusWarpRisk.Dangerous => KoronusWarpIncident.Vertigo,
            _ when kindRoll < 0.5f => KoronusWarpIncident.Turbulence,
            _ => KoronusWarpIncident.Vertigo,
        };
    }

    public static bool HasBreach(KoronusWarpIncident incident) => incident is
        KoronusWarpIncident.Rift or KoronusWarpIncident.Boarders or
        KoronusWarpIncident.Hunt or KoronusWarpIncident.CorrosiveBreach;

    public static IReadOnlyList<string> GetBoarders(KoronusWarpIncident incident) => incident switch
    {
        KoronusWarpIncident.Rift => Doppelganger,
        KoronusWarpIncident.Boarders => Runners,
        KoronusWarpIncident.Hunt => Hunters,
        KoronusWarpIncident.CorrosiveBreach => Spitters,
        _ => Array.Empty<string>(),
    };

    private static readonly string[] Doppelganger = ["MobWH40KWarpDoppelganger"];
    private static readonly string[] Runners = ["MobWH40KWarpRunner", "MobWH40KWarpRunner", "MobWH40KWarpRunner"];
    private static readonly string[] Hunters = ["MobWH40KWarpRunnerAlpha", "MobWH40KWarpRunner"];
    private static readonly string[] Spitters = ["MobWH40KWarpSpitter", "MobWH40KWarpDoppelganger"];

    public static string GetRouteClass(KoronusWarpRisk risk) => risk.ToString();
}
