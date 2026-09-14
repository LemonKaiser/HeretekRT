namespace Content.Shared._WH40K.Activities;

/// <summary>
/// Authoritative lifecycle states for one Koronus activity instance.
/// Terminal states never transition again.
/// </summary>
public enum KoronusActivityState : byte
{
    Candidate,
    Briefed,
    Available,
    Engaged,
    Resolving,
    CleanupPending,
    Archived,
}

public enum KoronusActivityTerminalReason : byte
{
    Completed,
    Defeated,
    Contained,
    Evaded,
    Expired,
    Abandoned,
    SafetyViolation,
    TargetUnreachable,
    ParticipantsLost,
    OwnedEntityMissing,
    ContentAuditFailed,
    RoundEnded,
}

public enum KoronusActivityDayFocus : byte
{
    Neutral,
    Trade,
    Exploration,
    Rescue,
    Warp,
    Raiders,
    TyranidTrace,
    AncientSignal,
}

public enum KoronusActivityMarkerLane : byte
{
    Ambient,
    Route,
    Vessel,
    Surface,
    Orbit,
    Grid,
    Threat,
    Arc,
}

public enum KoronusActivityFamily : byte
{
    State,
    Route,
    VesselSignal,
    Threat,
}

/// <summary>
/// Selects the server-only executor used after an otherwise ordinary activity marker is reached.
/// None preserves the stage-two data-only behaviour.
/// </summary>
public enum KoronusActivityExecutionKind : byte
{
    None,
    SurfaceVoxBeacon,
    SurfaceRescueFlare,
    OrbitAuspexProbe,
    HostileDoppelgangerEcho,
    HostileHormagauntProbe,
    HostileWarpRunnerPack,
    OrbitSecureCacheGrid,
    OrbitSalvageClusterGrid,
    OrbitRescuePodGrid,
    OrbitLostCargoGrid,
    OrbitDerelictWreckGrid,
    RaiderCutterCacheGrid,
    AivoriusBlackBoxSetPiece,
    FoulstoneSurveySetPiece,
    SottosIceRescueSetPiece,
    AivoriusCargoSetPiece,
    FoulstoneMineshaftSetPiece,
    SottosColdRelaySetPiece,
    FoulstoneAssayAnnexSetPiece,
    SottosCryoArchiveSetPiece,
    AivoriusSurveyCutterDockSetPiece,
}

/// <summary>
/// The six small, fixed Footfall consumers for recovered expedition property. This is deliberately
/// not a price table: each consumer accepts a short exact catalogue and never creates currency.
/// </summary>
public enum KoronusActivityTrophyConsumer : byte
{
    AivoriusArchive,
    FoulstoneSurvey,
    CargoOfficio,
    MinersGuild,
    Medicae,
    AstropathicRelay,
}

/// <summary>
/// Read-only escalation state of a hostile sector contact. It is kept separately from the
/// generic lifecycle so a threat can be shown as intelligence before it is materialized.
/// </summary>
public enum KoronusActivityThreatState : byte
{
    None,
    Intel,
    Active,
    Contained,
    Defeated,
    Evaded,
    SafetyViolation,
}

public enum KoronusActivityThreatLevel : byte
{
    None,
    Intel,
    Contained,
    Combat,
    Vessel,
}

public enum KoronusActivityThreatFamily : byte
{
    None,
    Warp,
    Tyranid,
    Raider,
}

public enum KoronusActivityRisk : byte
{
    Informational,
    Low,
    Moderate,
    High,
}

public enum KoronusActivityReliability : byte
{
    Verified,
    Partial,
    Suspicious,
    Stale,
}

public enum KoronusActivityRejectReason : byte
{
    None,
    DirectorInactive,
    UnknownSystem,
    DisabledSystem,
    NotActivityEligible,
    UnloadedSystem,
    MapMismatch,
    ProtectedArea,
    ProtectedGrid,
    ShuttleInFtl,
    PlanetaryTransit,
    NoLivingParticipants,
    GhostContent,
    UnsafeGridContent,
}

public static class KoronusActivityRuntimePolicy
{
    public const int MinimumActiveActivities = 3;
    public const int MaximumActiveActivities = 6;

    public static bool IsSystemEligible(string systemId, bool enabled, bool activityEligible)
    {
        return enabled && activityEligible && systemId != "Footfall";
    }

    public static int GetInitialPopulation(int eligibleSystemCount, int templateCount)
    {
        return Math.Clamp(
            Math.Min(eligibleSystemCount, templateCount),
            0,
            MinimumActiveActivities);
    }

    public static bool IsPhysicalExecution(KoronusActivityExecutionKind execution)
    {
        return IsObjectiveExecution(execution) || IsHostileExecution(execution) || IsGridExecution(execution) ||
               IsSetPieceExecution(execution);
    }

    public static bool IsObjectiveExecution(KoronusActivityExecutionKind execution)
    {
        return execution is KoronusActivityExecutionKind.SurfaceVoxBeacon or
            KoronusActivityExecutionKind.SurfaceRescueFlare or
            KoronusActivityExecutionKind.OrbitAuspexProbe;
    }

    public static bool IsHostileExecution(KoronusActivityExecutionKind execution)
    {
        return execution is KoronusActivityExecutionKind.HostileDoppelgangerEcho or
            KoronusActivityExecutionKind.HostileHormagauntProbe or
            KoronusActivityExecutionKind.HostileWarpRunnerPack;
    }

    /// <summary>
    /// A grid activity always builds one new static grid on an already loaded system map. It never
    /// accepts a map path, a shuttle grid, a docking target or a player ship as input.
    /// </summary>
    public static bool IsGridExecution(KoronusActivityExecutionKind execution)
    {
        return execution is KoronusActivityExecutionKind.OrbitSecureCacheGrid or
            KoronusActivityExecutionKind.OrbitSalvageClusterGrid or
            KoronusActivityExecutionKind.OrbitRescuePodGrid or
            KoronusActivityExecutionKind.OrbitLostCargoGrid or
            KoronusActivityExecutionKind.OrbitDerelictWreckGrid or
            KoronusActivityExecutionKind.RaiderCutterCacheGrid;
    }

    /// <summary>
    /// Stages two through five admit exactly nine authored map set pieces. The stage-five profile
    /// is the sole exception that can create one receive-only docking port after its map is
    /// audited; no generic room generator can select arbitrary maps or content. Their location,
    /// map path and recoverable item are all closed by code, not supplied by activity YAML.
    /// </summary>
    public static bool IsSetPieceExecution(KoronusActivityExecutionKind execution)
    {
        return execution is KoronusActivityExecutionKind.AivoriusBlackBoxSetPiece or
            KoronusActivityExecutionKind.FoulstoneSurveySetPiece or
            KoronusActivityExecutionKind.SottosIceRescueSetPiece or
            KoronusActivityExecutionKind.AivoriusCargoSetPiece or
            KoronusActivityExecutionKind.FoulstoneMineshaftSetPiece or
            KoronusActivityExecutionKind.SottosColdRelaySetPiece or
            KoronusActivityExecutionKind.FoulstoneAssayAnnexSetPiece or
            KoronusActivityExecutionKind.SottosCryoArchiveSetPiece or
            KoronusActivityExecutionKind.AivoriusSurveyCutterDockSetPiece;
    }

    /// <summary>
    /// Only the Aivorius survey cutter may retain a real docking port. Keeping this separate from
    /// the broader set-piece predicate makes a future map profile opt in explicitly.
    /// </summary>
    public static bool IsDockableSetPieceExecution(KoronusActivityExecutionKind execution)
    {
        return execution == KoronusActivityExecutionKind.AivoriusSurveyCutterDockSetPiece;
    }

    public static bool RequiresSurface(KoronusActivityExecutionKind execution)
    {
        return execution is KoronusActivityExecutionKind.SurfaceVoxBeacon or
            KoronusActivityExecutionKind.SurfaceRescueFlare or
            KoronusActivityExecutionKind.HostileDoppelgangerEcho or
            KoronusActivityExecutionKind.HostileHormagauntProbe;
    }

    public static bool RequiresSystemLease(KoronusActivityExecutionKind execution)
    {
        return execution is KoronusActivityExecutionKind.OrbitAuspexProbe or
            KoronusActivityExecutionKind.HostileWarpRunnerPack or
            KoronusActivityExecutionKind.OrbitSecureCacheGrid or
            KoronusActivityExecutionKind.OrbitSalvageClusterGrid or
            KoronusActivityExecutionKind.OrbitRescuePodGrid or
            KoronusActivityExecutionKind.OrbitLostCargoGrid or
            KoronusActivityExecutionKind.OrbitDerelictWreckGrid or
            KoronusActivityExecutionKind.RaiderCutterCacheGrid or
            KoronusActivityExecutionKind.AivoriusBlackBoxSetPiece or
            KoronusActivityExecutionKind.FoulstoneSurveySetPiece or
            KoronusActivityExecutionKind.SottosIceRescueSetPiece or
            KoronusActivityExecutionKind.AivoriusCargoSetPiece or
            KoronusActivityExecutionKind.FoulstoneMineshaftSetPiece or
            KoronusActivityExecutionKind.SottosColdRelaySetPiece or
            KoronusActivityExecutionKind.FoulstoneAssayAnnexSetPiece or
            KoronusActivityExecutionKind.SottosCryoArchiveSetPiece or
            KoronusActivityExecutionKind.AivoriusSurveyCutterDockSetPiece;
    }

    public static bool IsThreatTerminalReason(KoronusActivityTerminalReason reason)
    {
        return reason is KoronusActivityTerminalReason.Defeated or
            KoronusActivityTerminalReason.Contained or
            KoronusActivityTerminalReason.Evaded or
            KoronusActivityTerminalReason.TargetUnreachable or
            KoronusActivityTerminalReason.OwnedEntityMissing or
            KoronusActivityTerminalReason.ContentAuditFailed or
            KoronusActivityTerminalReason.SafetyViolation or
            KoronusActivityTerminalReason.ParticipantsLost;
    }

    /// <summary>
    /// Stage four has no generic hostile prototype pool. Both ids are checked together so a
    /// future YAML weight change cannot turn a known marker into a ghost-capable or structural
    /// damage NPC encounter.
    /// </summary>
    public static bool IsApprovedHostilePrototype(
        KoronusActivityExecutionKind execution,
        string anchorPrototypeId,
        string hostilePrototypeId)
    {
        return execution switch
        {
            KoronusActivityExecutionKind.HostileDoppelgangerEcho =>
                anchorPrototypeId == "KoronusActivityWarpThreatBeacon" &&
                hostilePrototypeId == "MobWH40KWarpDoppelganger",
            KoronusActivityExecutionKind.HostileHormagauntProbe =>
                anchorPrototypeId == "KoronusActivityBiosignalNode" &&
                hostilePrototypeId == "MobHormagaunt",
            KoronusActivityExecutionKind.HostileWarpRunnerPack =>
                anchorPrototypeId == "KoronusActivityRiftProbe" &&
                hostilePrototypeId == "MobWH40KWarpRunner",
            _ => false,
        };
    }

    /// <summary>
    /// The grid executor has a deliberately closed objective catalogue. A template cannot redirect
    /// it to arbitrary map content merely by changing a prototype id in YAML.
    /// </summary>
    public static bool IsApprovedGridObjective(
        KoronusActivityExecutionKind execution,
        string objectivePrototypeId)
    {
        return execution switch
        {
            KoronusActivityExecutionKind.OrbitSecureCacheGrid =>
                objectivePrototypeId == "KoronusActivitySecureCache",
            KoronusActivityExecutionKind.OrbitSalvageClusterGrid =>
                objectivePrototypeId == "KoronusActivitySalvageBlackBox",
            KoronusActivityExecutionKind.OrbitRescuePodGrid =>
                objectivePrototypeId == "KoronusActivityRescuePodLog",
            KoronusActivityExecutionKind.OrbitLostCargoGrid =>
                objectivePrototypeId == "KoronusActivityLostCargoManifest",
            KoronusActivityExecutionKind.OrbitDerelictWreckGrid =>
                objectivePrototypeId == "KoronusActivityDerelictCogitator",
            KoronusActivityExecutionKind.RaiderCutterCacheGrid =>
                objectivePrototypeId == "KoronusActivityRaiderCutterCache",
            _ => false,
        };
    }

    /// <summary>
    /// Returns the only sector system in which an authored set piece can be advertised. This
    /// prevents a broad template weight or a future selector change from moving a surface ruin
    /// into Footfall or an unrelated system.
    /// </summary>
    public static bool TryGetSetPieceSystem(
        KoronusActivityExecutionKind execution,
        out string systemId)
    {
        switch (execution)
        {
            case KoronusActivityExecutionKind.AivoriusBlackBoxSetPiece:
                systemId = "Aivorius";
                return true;
            case KoronusActivityExecutionKind.FoulstoneSurveySetPiece:
                systemId = "Trinnitos";
                return true;
            case KoronusActivityExecutionKind.SottosIceRescueSetPiece:
                systemId = "SottosTomb";
                return true;
            case KoronusActivityExecutionKind.AivoriusCargoSetPiece:
                systemId = "Aivorius";
                return true;
            case KoronusActivityExecutionKind.FoulstoneMineshaftSetPiece:
                systemId = "Trinnitos";
                return true;
            case KoronusActivityExecutionKind.SottosColdRelaySetPiece:
                systemId = "SottosTomb";
                return true;
            case KoronusActivityExecutionKind.FoulstoneAssayAnnexSetPiece:
                systemId = "Trinnitos";
                return true;
            case KoronusActivityExecutionKind.SottosCryoArchiveSetPiece:
                systemId = "SottosTomb";
                return true;
            case KoronusActivityExecutionKind.AivoriusSurveyCutterDockSetPiece:
                systemId = "Aivorius";
                return true;
            default:
                systemId = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// Surface-only set pieces have a second fixed boundary. Aivorius is intentionally orbit-only
    /// so the black box cannot cause an unrelated planetary surface to load or receive content.
    /// </summary>
    public static bool IsApprovedSetPieceSurface(
        KoronusActivityExecutionKind execution,
        string? surfaceId)
    {
        return execution switch
        {
            KoronusActivityExecutionKind.AivoriusBlackBoxSetPiece => surfaceId == null,
            KoronusActivityExecutionKind.FoulstoneSurveySetPiece => surfaceId == "FoulstoneSurface",
            KoronusActivityExecutionKind.SottosIceRescueSetPiece => surfaceId == "SottosTombIceSurface",
            KoronusActivityExecutionKind.AivoriusCargoSetPiece => surfaceId == null,
            KoronusActivityExecutionKind.FoulstoneMineshaftSetPiece => surfaceId == "FoulstoneSurface",
            KoronusActivityExecutionKind.SottosColdRelaySetPiece => surfaceId == "SottosTombIceSurface",
            KoronusActivityExecutionKind.FoulstoneAssayAnnexSetPiece => surfaceId == "FoulstoneSurface",
            KoronusActivityExecutionKind.SottosCryoArchiveSetPiece => surfaceId == "SottosTombIceSurface",
            KoronusActivityExecutionKind.AivoriusSurveyCutterDockSetPiece => surfaceId == null,
            _ => false,
        };
    }

    /// <summary>
    /// The recovered item is an objective, not a loot table. Pairing execution and prototype here
    /// ensures a YAML edit cannot turn a set-piece marker into an arbitrary item spawn.
    /// </summary>
    public static bool IsApprovedSetPieceObjective(
        KoronusActivityExecutionKind execution,
        string objectivePrototypeId)
    {
        return execution switch
        {
            KoronusActivityExecutionKind.AivoriusBlackBoxSetPiece =>
                objectivePrototypeId == "KoronusMissionBlackBox",
            KoronusActivityExecutionKind.FoulstoneSurveySetPiece =>
                objectivePrototypeId == "KoronusFoulstoneSurveyCase",
            KoronusActivityExecutionKind.SottosIceRescueSetPiece =>
                objectivePrototypeId == "KoronusSottosRescuePatient",
            KoronusActivityExecutionKind.AivoriusCargoSetPiece =>
                objectivePrototypeId == "KoronusAivoriusCargoManifest",
            KoronusActivityExecutionKind.FoulstoneMineshaftSetPiece =>
                objectivePrototypeId == "KoronusFoulstoneMineCore",
            KoronusActivityExecutionKind.SottosColdRelaySetPiece =>
                objectivePrototypeId == "KoronusSottosRelayCipher",
            KoronusActivityExecutionKind.FoulstoneAssayAnnexSetPiece =>
                objectivePrototypeId == "KoronusFoulstoneAssaySample",
            KoronusActivityExecutionKind.SottosCryoArchiveSetPiece =>
                objectivePrototypeId == "KoronusSottosCryoSample",
            KoronusActivityExecutionKind.AivoriusSurveyCutterDockSetPiece =>
                objectivePrototypeId == "KoronusAivoriusCutterFlightLog",
            _ => false,
        };
    }

    /// <summary>
    /// Footfall never grants an automatic payout. A hand-off is accepted only by its designated
    /// counter, and only after the recovered root has been detached from its activity instance.
    /// </summary>
    public static bool IsApprovedFootfallTrophy(
        KoronusActivityTrophyConsumer consumer,
        string prototypeId,
        bool isRecoveryPatient)
    {
        return consumer switch
        {
            KoronusActivityTrophyConsumer.AivoriusArchive =>
                prototypeId is "KoronusMissionBlackBox" or "KoronusAivoriusCutterFlightLog",
            KoronusActivityTrophyConsumer.FoulstoneSurvey =>
                prototypeId is "KoronusFoulstoneSurveyCase" or "KoronusFoulstoneAssaySample",
            KoronusActivityTrophyConsumer.CargoOfficio =>
                prototypeId == "KoronusAivoriusCargoManifest",
            KoronusActivityTrophyConsumer.MinersGuild =>
                prototypeId == "KoronusFoulstoneMineCore",
            KoronusActivityTrophyConsumer.Medicae =>
                isRecoveryPatient && prototypeId == "KoronusSottosRescuePatient",
            KoronusActivityTrophyConsumer.AstropathicRelay =>
                prototypeId is "KoronusSottosRescuePod" or "KoronusSottosRelayCipher" or "KoronusSottosCryoSample",
            _ => false,
        };
    }
}

public static class KoronusActivityLifecycle
{
    public static bool IsTerminal(KoronusActivityState state)
    {
        return state == KoronusActivityState.Archived;
    }

    public static bool CanTransition(KoronusActivityState from, KoronusActivityState to)
    {
        if (from == to || IsTerminal(from))
            return false;

        return from switch
        {
            KoronusActivityState.Candidate => to is KoronusActivityState.Briefed or KoronusActivityState.CleanupPending,
            KoronusActivityState.Briefed => to is KoronusActivityState.Available or KoronusActivityState.CleanupPending,
            KoronusActivityState.Available => to is KoronusActivityState.Engaged or KoronusActivityState.CleanupPending,
            KoronusActivityState.Engaged => to is KoronusActivityState.Resolving or KoronusActivityState.CleanupPending,
            KoronusActivityState.Resolving => to == KoronusActivityState.CleanupPending,
            KoronusActivityState.CleanupPending => to == KoronusActivityState.Archived,
            _ => false,
        };
    }
}
