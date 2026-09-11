using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Activities;

/// <summary>
/// Presentation-only activity information attached to the existing Koronus sector-map state.
/// The identifier does not grant permission to start, complete, or claim an activity.
/// </summary>
[Serializable, NetSerializable]
public sealed class KoronusActivityMarkerState
{
    public long InstanceId;
    public string SystemId;
    public KoronusActivityMarkerLane Lane;
    public KoronusActivityRisk Risk;
    public KoronusActivityReliability Reliability;
    public string TitleLocId;
    public TimeSpan ExpiresAt;

    public KoronusActivityMarkerState(
        long instanceId,
        string systemId,
        KoronusActivityMarkerLane lane,
        KoronusActivityRisk risk,
        KoronusActivityReliability reliability,
        string titleLocId,
        TimeSpan expiresAt)
    {
        InstanceId = instanceId;
        SystemId = systemId;
        Lane = lane;
        Risk = risk;
        Reliability = reliability;
        TitleLocId = titleLocId;
        ExpiresAt = expiresAt;
    }
}
