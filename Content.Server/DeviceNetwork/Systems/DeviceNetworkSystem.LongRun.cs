namespace Content.Server.DeviceNetwork.Systems;

public sealed partial class DeviceNetworkSystem
{
    /// <summary>
    /// Returns queue sizes for diagnostics only. It does not drain or otherwise alter device traffic.
    /// </summary>
    public (int ActiveQueue, int NextQueue, int Networks) GetLongRunStatus()
    {
        return (_activeQueue.Count, _nextQueue.Count, _networks.Count);
    }

    /// <summary>
    /// Number of packets rejected after the safety cap was reached.
    /// </summary>
    public long GetLongRunDroppedPacketCount() => _droppedPacketCount;
}
