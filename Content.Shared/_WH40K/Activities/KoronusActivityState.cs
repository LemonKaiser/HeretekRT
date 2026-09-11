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
        return IsObjectiveExecution(execution) || IsHostileExecution(execution) || IsGridExecution(execution);
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
            KoronusActivityExecutionKind.RaiderCutterCacheGrid;
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
    /// Stage five has a deliberately closed grid-objective catalogue. A template cannot redirect
    /// the grid executor to arbitrary map content merely by changing a prototype id in YAML.
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
