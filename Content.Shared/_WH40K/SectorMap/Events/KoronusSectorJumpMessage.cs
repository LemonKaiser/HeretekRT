using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.Events;

/// <summary>
/// Requests a graph-validated FTL jump to another authored Koronus system.
/// </summary>
[Serializable, NetSerializable]
public sealed class KoronusSectorJumpMessage : BoundUserInterfaceMessage
{
    public string TargetSystemId;

    public KoronusSectorJumpMessage(string targetSystemId)
    {
        TargetSystemId = targetSystemId;
    }
}
