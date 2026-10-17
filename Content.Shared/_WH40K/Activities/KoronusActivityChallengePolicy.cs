namespace Content.Shared._WH40K.Activities;

public enum KoronusActivitySalvageProfile : byte
{
    None,
    Engineering,
    Medical,
    Minerals,
    Archeotech,
    Xenology,
}

public static class KoronusActivityChallengePolicy
{
    public static KoronusActivitySalvageProfile GetSalvageProfile(KoronusActivityExecutionKind execution)
    {
        if (KoronusExpeditionCatalog.TryGet(execution, out var expedition))
            return expedition.Salvage;

        return execution switch
        {
            KoronusActivityExecutionKind.None => KoronusActivitySalvageProfile.None,
            KoronusActivityExecutionKind.SurfaceRescueFlare or
            KoronusActivityExecutionKind.OrbitRescuePodGrid or
            KoronusActivityExecutionKind.SottosIceRescueSetPiece => KoronusActivitySalvageProfile.Medical,
            KoronusActivityExecutionKind.FoulstoneSurveySetPiece or
            KoronusActivityExecutionKind.FoulstoneMineshaftSetPiece or
            KoronusActivityExecutionKind.OrbitSalvageClusterGrid => KoronusActivitySalvageProfile.Minerals,
            KoronusActivityExecutionKind.HostileHormagauntProbe or
            KoronusActivityExecutionKind.HostileDoppelgangerEcho or
            KoronusActivityExecutionKind.HostileWarpRunnerPack or
            KoronusActivityExecutionKind.FoulstoneAssayAnnexSetPiece or
            KoronusActivityExecutionKind.SottosCryoArchiveSetPiece => KoronusActivitySalvageProfile.Xenology,
            KoronusActivityExecutionKind.OrbitAuspexProbe or
            KoronusActivityExecutionKind.AivoriusBlackBoxSetPiece or
            KoronusActivityExecutionKind.SottosColdRelaySetPiece or
            KoronusActivityExecutionKind.AivoriusSurveyCutterDockSetPiece => KoronusActivitySalvageProfile.Archeotech,
            _ => KoronusActivitySalvageProfile.Engineering,
        };
    }

    public static TimeSpan GetWorkDuration(KoronusActivityExecutionKind execution) => TimeSpan.FromSeconds(execution switch
    {
        KoronusActivityExecutionKind.HostileWarpRunnerPack => 12,
        KoronusActivityExecutionKind.HostileHormagauntProbe => 10,
        KoronusActivityExecutionKind.HostileDoppelgangerEcho => 8,
        KoronusActivityExecutionKind.OrbitSecureCacheGrid or KoronusActivityExecutionKind.RaiderCutterCacheGrid => 8,
        _ => 6,
    });
}
