using Content.Server.Cargo.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Physics;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using System.Reflection;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Deletes entities eligible for deletion.
/// </summary>
public sealed partial class SpaceCleanupSystem : BaseCleanupSystem<PhysicsComponent>
{
    [Dependency] private CleanupHelperSystem _cleanup = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    private object? _manifold;
    private MethodInfo? _testOverlap;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private float _maxDistance;
    private float _maxGridDistance;
    private float _maxPrice;
    private TimeSpan _cleanupDuration;

    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<BuckleComponent> _buckleQuery;
    private EntityQuery<CleanupPlayerProtectedComponent> _playerProtectedQuery;
    private EntityQuery<ContainerManagerComponent> _containerQuery;
    private EntityQuery<CleanupImmuneComponent> _immuneQuery;
    private EntityQuery<FixturesComponent> _fixQuery;
    private EntityQuery<HTNComponent> _htnQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<PhysicsComponent> _physQuery;
    private EntityQuery<PullableComponent> _pullableQuery;

    private List<(EntityCoordinates Coord, TimeSpan Time, float Radius, float Aggression)> _sweepQueue = new();
    private HashSet<Entity<PhysicsComponent>> _sweepEnts = new();
    private readonly Queue<(EntityUid Uid, float Aggression)> _sweepCandidates = new();
    private readonly HashSet<EntityUid> _queuedSweepEntities = new();
    private readonly System.Diagnostics.Stopwatch _sweepStopwatch = new();
    private const int MaxSweepCandidates = 16_384;
    private long _droppedSweepCandidates;

    public override void Initialize()
    {
        base.Initialize();

        // this queries over literally everything with PhysicsComponent so has to have big interval
        _cleanupInterval = TimeSpan.FromSeconds(600);

        _actorQuery = GetEntityQuery<ActorComponent>();
        _buckleQuery = GetEntityQuery<BuckleComponent>();
        _playerProtectedQuery = GetEntityQuery<CleanupPlayerProtectedComponent>();
        _containerQuery = GetEntityQuery<ContainerManagerComponent>();
        _immuneQuery = GetEntityQuery<CleanupImmuneComponent>();
        _fixQuery = GetEntityQuery<FixturesComponent>();
        _htnQuery = GetEntityQuery<HTNComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _physQuery = GetEntityQuery<PhysicsComponent>();
        _pullableQuery = GetEntityQuery<PullableComponent>();

        Subs.CVar(_cfg, MonoCVars.CleanupMaxGridDistance, val => _maxGridDistance = val, true);
        Subs.CVar(_cfg, MonoCVars.SpaceCleanupDistance, val => _maxDistance = val, true);
        Subs.CVar(_cfg, MonoCVars.SpaceCleanupMaxValue, val => _maxPrice = val, true);
        Subs.CVar(_cfg, MonoCVars.SpaceCleanupDuration,
            val => _cleanupDuration = TimeSpan.FromSeconds(Math.Max(0f, val)), true);

        var manifoldType = typeof(SharedMapSystem).Assembly.GetType("Robust.Shared.Physics.Collision.IManifoldManager");
        if (manifoldType != null)
        {
            _manifold = IoCManager.ResolveType(manifoldType);
            var testOverlapMethod = manifoldType.GetMethod("TestOverlap");
            if (testOverlapMethod != null)
                _testOverlap = testOverlapMethod.MakeGenericMethod(typeof(IPhysShape), typeof(PhysShapeCircle));
        }
    }

    protected override bool ShouldEntityCleanup(EntityUid uid)
    {
        return ShouldEntityCleanup(uid, 1f, true);
    }

    private bool ShouldEntityCleanup(EntityUid uid, float aggression, bool requireGracePeriod)
    {
        var xform = Transform(uid);

        var isStuck = false;

        var price = 0f;

        if (xform.MapID == MapId.Nullspace ||
            _gridQuery.HasComp(uid) ||
            _htnQuery.HasComp(uid) || // handled by MobCleanupSystem
            _immuneQuery.HasComp(uid) ||
            _playerProtectedQuery.HasComp(uid) ||
            _actorQuery.HasComp(uid) ||
            _mindQuery.HasComp(uid) ||
            _containerQuery.TryComp(uid, out var containers) && HasContents(containers) ||
            _pullableQuery.TryComp(uid, out var pullable) && pullable.BeingPulled ||
            _buckleQuery.TryComp(uid, out var buckle) && buckle.Buckled)
        {
            return RejectPeriodicCandidate(uid);
        }

        var inOpenSpace = xform.ParentUid == xform.MapUid;
        if (!inOpenSpace)
        {
            if (xform.GridUid is not { } grid || _cleanup.IsGridProtectedFromCleanup(grid))
                return RejectPeriodicCandidate(uid);

            isStuck = GetWallStuck((uid, xform));
            if (!isStuck)
                return RejectPeriodicCandidate(uid);
        }

        price = (float)_pricing.GetPrice(uid);
        if (price > _maxPrice)
            return RejectPeriodicCandidate(uid);

        var valueFactor = _maxPrice <= 0f
            ? 1f
            : Math.Clamp(MathF.Sqrt(MathF.Max(price, 0f) / _maxPrice), 0.1f, 1f);
        var playerDistance = MathF.Max(30f, _maxDistance * aggression * valueFactor);

        if (_cleanup.HasNearbyPlayers(xform.Coordinates, playerDistance))
            return RejectPeriodicCandidate(uid);

        if (!isStuck)
        {
            var gridDistance = MathF.Max(10f, _maxGridDistance * aggression * valueFactor);
            if (_cleanup.HasNearbyGrids(xform.Coordinates, gridDistance))
                return RejectPeriodicCandidate(uid);
        }

        if (!requireGracePeriod)
            return true;

        var state = EnsureComp<SpaceCleanupStateComponent>(uid);
        var now = _timing.CurTime;
        var elapsed = state.LastEvaluation == TimeSpan.Zero
            ? TimeSpan.Zero
            : now - state.LastEvaluation;
        state.LastEvaluation = now;

        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        else if (elapsed > _cleanupInterval * 2)
            elapsed = _cleanupInterval;

        state.EligibleFor += elapsed;
        return state.EligibleFor >= _cleanupDuration;
    }

    private bool RejectPeriodicCandidate(EntityUid uid)
    {
        if (HasComp<SpaceCleanupStateComponent>(uid))
            RemCompDeferred<SpaceCleanupStateComponent>(uid);

        return false;
    }

    private static bool HasContents(ContainerManagerComponent manager)
    {
        foreach (var container in manager.Containers.Values)
        {
            if (container.ContainedEntities.Count != 0)
                return true;
        }

        return false;
    }

    private bool GetWallStuck(Entity<TransformComponent> ent)
    {
        // This compatibility path reaches a Robust physics implementation detail. Fail closed if it changes.
        if (_manifold == null || _testOverlap == null)
            return false;

        if (ent.Comp.GridUid is not { } gridUid
            || ent.Comp.Anchored
            || ent.Comp.ParentUid != gridUid // ignore if not directly parented to grid
        )
            return false;

        var xfB = new Transform(ent.Comp.LocalPosition, 0);
        var shapeB = new PhysShapeCircle(0.001f);

        var contacts = _physics.GetContacts(ent.Owner);
        // it dies without this for some reason
        if (contacts == ContactEnumerator.Empty)
            return false;

        while (contacts.MoveNext(out var contact))
        {
            if (contact.FixtureA == null
                || contact.FixtureB == null
                || contact.BodyA == null
                || contact.BodyB == null
                || !contact.FixtureA.Hard
                || !contact.FixtureB.Hard
                || !contact.IsTouching
            )
                continue;

            var isA = contact.EntityB == ent.Owner;

            var body = isA ? contact.BodyA : contact.BodyB;
            // only trigger when the other entity is static
            if ((body.BodyType & BodyType.Static) == 0)
                continue;

            var fix = isA ? contact.FixtureA : contact.FixtureB;
            var xform = isA ? contact.XformA : contact.XformB;
            var anch = isA ? contact.EntityA : contact.EntityB;

            var xf = _physics.GetLocalPhysicsTransform(anch, xform);
            var shape = fix.Shape;

            try
            {
                if ((bool?)_testOverlap.Invoke(_manifold, [shape, 0, shapeB, 0, xf, xfB]) ?? false)
                    return true;
            }
            catch (TargetInvocationException exception)
            {
                Log.Error($"Wall-stuck cleanup overlap check failed closed: {exception.InnerException ?? exception}");
                _manifold = null;
                _testOverlap = null;
                return false;
            }
        }

        return false;
    }

    public void QueueSweep(EntityCoordinates coordinates, TimeSpan time, float radius, float aggression)
    {
        if (!_cleanupEnabled)
            return;

        const int maxPendingSweeps = 128;
        if (_sweepQueue.Count >= maxPendingSweeps)
            _sweepQueue.RemoveAt(0);

        _sweepQueue.Add((coordinates, time, radius, aggression));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_cleanupEnabled)
        {
            _sweepQueue.Clear();
            _sweepCandidates.Clear();
            _queuedSweepEntities.Clear();
            return;
        }

        var sweepsThisTick = 0;
        for (var i = _sweepQueue.Count - 1; i >= 0 && sweepsThisTick < 2; i--)
        {
            var (coord, time, radius, aggression) = _sweepQueue[i];

            if (_timing.CurTime < time)
                continue;

            _sweepQueue.RemoveAt(i);
            sweepsThisTick++;
            if (!coord.IsValid(EntityManager))
                continue;

            _sweepEnts.Clear();
            _lookup.GetEntitiesInRange(_transform.ToMapCoordinates(coord), radius, _sweepEnts, LookupFlags.Dynamic | LookupFlags.Approximate | LookupFlags.Sundries);

            foreach (var (uid, _) in _sweepEnts)
            {
                if (_queuedSweepEntities.Count >= MaxSweepCandidates)
                {
                    // Impact sweeps are optional cleanup work. Do not allow a single
                    // huge lookup to retain an unbounded candidate list.
                    _droppedSweepCandidates++;
                    continue;
                }

                if (_queuedSweepEntities.Add(uid))
                    _sweepCandidates.Enqueue((uid, aggression));
            }
        }

        var checkedThisTick = 0;
        var deletedThisTick = 0;
        _sweepStopwatch.Restart();

        while (_sweepCandidates.Count != 0 &&
               checkedThisTick < _maxChecksPerTick &&
               deletedThisTick < _maxDeletesPerTick &&
               _sweepStopwatch.Elapsed < _maximumProcessTime)
        {
            var (uid, aggression) = _sweepCandidates.Dequeue();
            _queuedSweepEntities.Remove(uid);
            if (TerminatingOrDeleted(uid))
                continue;

            checkedThisTick++;
            if (ShouldEntityCleanup(uid, aggression, false) && CleanupEnt(uid))
                deletedThisTick++;
        }
    }

    protected override void OnCleanupRoundRestart()
    {
        _sweepQueue.Clear();
        _sweepCandidates.Clear();
        _queuedSweepEntities.Clear();
        _droppedSweepCandidates = 0;
    }

    public string GetSweepStatus()
    {
        return $"{nameof(SpaceCleanupSystem)}: sweeps={_sweepQueue.Count}, candidates={_sweepCandidates.Count}, " +
               $"droppedCandidates={_droppedSweepCandidates}, maxCandidates={MaxSweepCandidates}";
    }
}
