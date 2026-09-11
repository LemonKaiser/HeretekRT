using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Activities;

/// <summary>
/// Read-only dossier entry for the sector-map UI. It contains no target coordinates or authority
/// to start, resolve, or materialize an activity.
/// </summary>
[Serializable, NetSerializable]
public sealed class KoronusActivityDossierState
{
    public long InstanceId;
    public string SystemId;
    public string? RouteFromSystem;
    public string? RouteToSystem;
    public KoronusActivityFamily Family;
    public KoronusActivityMarkerLane Lane;
    public KoronusActivityState State;
    public KoronusActivityRisk Risk;
    public KoronusActivityReliability Reliability;
    public KoronusActivityThreatFamily ThreatFamily;
    public KoronusActivityThreatLevel ThreatLevel;
    public KoronusActivityThreatState ThreatState;
    public KoronusActivityTerminalReason? TerminalReason;
    public string TitleLocId;
    public string SummaryLocId;
    public TimeSpan ExpiresAt;

    public KoronusActivityDossierState(
        long instanceId,
        string systemId,
        string? routeFromSystem,
        string? routeToSystem,
        KoronusActivityFamily family,
        KoronusActivityMarkerLane lane,
        KoronusActivityState state,
        KoronusActivityRisk risk,
        KoronusActivityReliability reliability,
        KoronusActivityThreatFamily threatFamily,
        KoronusActivityThreatLevel threatLevel,
        KoronusActivityThreatState threatState,
        KoronusActivityTerminalReason? terminalReason,
        string titleLocId,
        string summaryLocId,
        TimeSpan expiresAt)
    {
        InstanceId = instanceId;
        SystemId = systemId;
        RouteFromSystem = routeFromSystem;
        RouteToSystem = routeToSystem;
        Family = family;
        Lane = lane;
        State = state;
        Risk = risk;
        Reliability = reliability;
        ThreatFamily = threatFamily;
        ThreatLevel = threatLevel;
        ThreatState = threatState;
        TerminalReason = terminalReason;
        TitleLocId = titleLocId;
        SummaryLocId = summaryLocId;
        ExpiresAt = expiresAt;
    }
}

public sealed class KoronusActivityPresentationChangedEvent : EntityEventArgs
{
}
