namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    /// <summary>
    /// Returns queued and active explosion work for diagnostics only.
    /// </summary>
    public (int Queued, int UniqueQueued, bool Active) GetLongRunStatus()
    {
        return (_explosionQueue.Count, _queuedExplosions.Count, _activeExplosion != null);
    }
}
