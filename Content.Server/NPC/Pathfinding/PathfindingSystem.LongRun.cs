namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>
    /// Returns the number of path requests waiting for time-sliced processing.
    /// </summary>
    public int GetLongRunPendingRequestCount() => _pathRequests.Count;

    public long GetLongRunRejectedRequestCount() => _rejectedRequestCount;
}
