using Content.Shared._WH40K.Activities;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Server-only state for one activity. Physical identity remains transient and is only assigned
/// after a living player reaches the selected target.
/// </summary>
public sealed class KoronusActivityRuntimeInstance
{
    public long Id;
    public string TemplateId = string.Empty;
    public string SystemId = string.Empty;
    public string? RouteFromSystem;
    public string? RouteToSystem;
    public KoronusActivityState State;
    public TimeSpan CreatedAt;
    public TimeSpan ExpiresAt;
    public EntityUid? Objective;
    public bool HasSystemLease;
    public TimeSpan? LeaseStartedAt;
    public KoronusActivityThreatState ThreatState;
    public TimeSpan? LastThreatParticipantAt;
}

/// <summary>
/// Bounded immutable history replacing an active instance after its single terminal transition.
/// </summary>
public sealed class KoronusActivityHistoryEntry
{
    public long InstanceId;
    public string TemplateId = string.Empty;
    public string SystemId = string.Empty;
    public string? RouteFromSystem;
    public string? RouteToSystem;
    public KoronusActivityFamily Family;
    public KoronusActivityMarkerLane Lane;
    public KoronusActivityTerminalReason Reason;
    public TimeSpan OccurredAt;
}

/// <summary>
/// Read-only aggregation for the long-round monitor. It intentionally exposes counts and ages
/// only: diagnostics can never select, resolve, or otherwise mutate an activity.
/// </summary>
public readonly record struct KoronusActivityLongRunStatus(
    bool DirectorActive,
    int DayIndex,
    KoronusActivityDayFocus DayFocus,
    int AvailableInstances,
    int EngagedInstances,
    int ActivityLeases,
    int OwnedEntities,
    int OwnedGrids,
    int OwnedNpcs,
    int OrphanCandidates,
    TimeSpan OldestLeaseAge,
    int TerminalEntriesLast24Hours,
    int TerminalEntriesLast96Hours,
    int DistinctFamiliesLast24Hours);
