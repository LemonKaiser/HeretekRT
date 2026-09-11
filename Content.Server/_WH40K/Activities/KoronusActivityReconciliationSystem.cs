using Robust.Shared.Timing;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Low-frequency idempotent sweep for entities explicitly marked as activity-owned after an
/// interrupted materialization, cold-map teardown, or round cleanup. It does not restore maps or
/// instances; an unknown instance is always removed through the director's exact ownership path.
/// </summary>
public sealed class KoronusActivityReconciliationSystem : EntitySystem
{
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromSeconds(10);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private KoronusActivityDirectorSystem _director = default!;

    private TimeSpan _nextReconciliation;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextReconciliation)
            return;

        _nextReconciliation = _timing.CurTime + ReconciliationInterval;
        _director.ReconcileOrphanedOwnedEntities();
    }
}
