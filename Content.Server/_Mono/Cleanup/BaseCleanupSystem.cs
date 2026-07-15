using Content.Shared._Mono.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;

namespace Content.Server._Mono.Cleanup;

public abstract partial class BaseCleanupSystem<TComp> : EntitySystem
    where TComp : IComponent
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    protected TimeSpan _cleanupInterval = TimeSpan.FromSeconds(300);
    protected TimeSpan _debugCleanupInterval = TimeSpan.FromSeconds(15);
    protected bool _doDebug;
    protected bool _doLog;
    protected bool _cleanupEnabled;
    protected bool _dryRun;
    protected int _maxChecksPerTick = 64;
    protected int _maxDeletesPerTick = 8;
    protected TimeSpan _maximumProcessTime = TimeSpan.FromMilliseconds(1);

    private readonly Queue<EntityUid> _checkQueue = new();
    private readonly HashSet<EntityUid> _queued = new();
    private readonly HashSet<EntityUid> _tracked = new();
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();

    private int _cycleRemaining;
    private int _batchChecked;
    private int _batchEligible;
    private int _batchDeleted;
    private long _totalChecked;
    private long _totalEligible;
    private long _totalDeleted;
    // used to track when we should be cleaning up the next entry in our queue
    private TimeSpan _cleanupAccumulator = TimeSpan.Zero;
    private TimeSpan _cleanupDeferDuration;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<TComp, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<TComp, ComponentShutdown>(OnComponentShutdown);

        Subs.CVar(_cfg, MonoCVars.CleanupEnabled, val => _cleanupEnabled = val, true);
        Subs.CVar(_cfg, MonoCVars.CleanupDryRun, val => _dryRun = val, true);
        Subs.CVar(_cfg, MonoCVars.CleanupDebug, val => _doDebug = val, true);
        Subs.CVar(_cfg, MonoCVars.CleanupLog, val => _doLog = val, true);
        Subs.CVar(_cfg, MonoCVars.CleanupMaxChecksPerTick, val => _maxChecksPerTick = Math.Max(1, val), true);
        Subs.CVar(_cfg, MonoCVars.CleanupMaxDeletesPerTick, val => _maxDeletesPerTick = Math.Max(1, val), true);
        Subs.CVar(_cfg, MonoCVars.CleanupMaximumProcessTimeMs,
            val => _maximumProcessTime = TimeSpan.FromMilliseconds(Math.Max(0.05f, val)), true);

        // Normally systems initialize before map entities. This also makes hot reload and tests deterministic.
        var query = EntityQueryEnumerator<TComp>();
        while (query.MoveNext(out var uid, out _))
            Track(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_cleanupEnabled)
        {
            _cleanupAccumulator = TimeSpan.Zero;
            return;
        }

        if (_tracked.Count == 0)
        {
            _checkQueue.Clear();
            _queued.Clear();
            _cleanupAccumulator = TimeSpan.Zero;
            _cycleRemaining = 0;
            return;
        }

        var interval = !_doDebug ? _cleanupInterval : _debugCleanupInterval;
        _cleanupDeferDuration = interval * 0.9 / Math.Max(_tracked.Count, 1);
        _cleanupAccumulator += TimeSpan.FromSeconds(frameTime);

        if (_cycleRemaining <= 0)
            StartCycle();

        var checkedThisTick = 0;
        var deletedThisTick = 0;
        _stopwatch.Restart();

        while (_checkQueue.Count != 0 &&
               (_cleanupDeferDuration <= TimeSpan.Zero || _cleanupAccumulator >= _cleanupDeferDuration) &&
               checkedThisTick < _maxChecksPerTick &&
               deletedThisTick < _maxDeletesPerTick &&
               _stopwatch.Elapsed < _maximumProcessTime)
        {
            if (_cleanupDeferDuration > TimeSpan.Zero)
                _cleanupAccumulator -= _cleanupDeferDuration;
            else
                _cleanupAccumulator = TimeSpan.Zero;

            var uid = _checkQueue.Dequeue();
            _queued.Remove(uid);
            _cycleRemaining--;

            if (!_tracked.Contains(uid) || TerminatingOrDeleted(uid) || !HasComp<TComp>(uid))
            {
                _tracked.Remove(uid);
                CompleteCycleIfNeeded();
                continue;
            }

            checkedThisTick++;
            _batchChecked++;
            _totalChecked++;

            var deleted = ShouldEntityCleanup(uid) && CleanupEnt(uid);
            if (deleted)
            {
                deletedThisTick++;
                _tracked.Remove(uid);
            }
            else if (_tracked.Contains(uid) && !TerminatingOrDeleted(uid) && HasComp<TComp>(uid))
            {
                Enqueue(uid);
            }

            CompleteCycleIfNeeded();
        }
    }

    /// <summary>
    ///     Records an eligible candidate and either deletes it or reports it in dry-run mode.
    /// </summary>
    protected bool CleanupEnt(EntityUid uid)
    {
        _batchEligible++;
        _totalEligible++;

        if (_dryRun)
            return false;

        var coord = Transform(uid).Coordinates;
        var world = _transform.ToMapCoordinates(coord);
        if (_doLog)
            Log.Info($"Cleanup deleting entity {ToPrettyString(uid)} at {coord} (world {world})");

        _batchDeleted++;
        _totalDeleted++;
        QueueDel(uid);
        return true;
    }

    public string GetCleanupStatus()
    {
        return $"{GetType().Name}: tracked={_tracked.Count}, pending={_checkQueue.Count}, checked={_totalChecked}, " +
               $"eligible={_totalEligible}, deleted={_totalDeleted}, enabled={_cleanupEnabled}, dryRun={_dryRun}";
    }

    private void Track(EntityUid uid)
    {
        if (_tracked.Add(uid))
            Enqueue(uid);
    }

    private void Enqueue(EntityUid uid)
    {
        if (_queued.Add(uid))
            _checkQueue.Enqueue(uid);
    }

    private void StartCycle()
    {
        _cycleRemaining = Math.Max(_tracked.Count, 1);
        _batchChecked = 0;
        _batchEligible = 0;
        _batchDeleted = 0;
    }

    private void CompleteCycleIfNeeded()
    {
        if (_cycleRemaining > 0)
            return;

        FinishBatch();
        StartCycle();
    }

    private void FinishBatch()
    {
        if (_batchEligible == 0)
            return;

        Log.Info($"{GetType().Name} cleanup batch: checked={_batchChecked}, eligible={_batchEligible}, " +
                 $"deleted={_batchDeleted}, dryRun={_dryRun}.");
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _checkQueue.Clear();
        _queued.Clear();
        _tracked.Clear();
        _cycleRemaining = 0;
        _cleanupAccumulator = TimeSpan.Zero;
        _cleanupDeferDuration = TimeSpan.Zero;
        _batchChecked = 0;
        _batchEligible = 0;
        _batchDeleted = 0;
        _totalChecked = 0;
        _totalEligible = 0;
        _totalDeleted = 0;
    }

    private void OnComponentStartup(Entity<TComp> entity, ref ComponentStartup args)
    {
        Track(entity);
    }

    private void OnComponentShutdown(Entity<TComp> entity, ref ComponentShutdown args)
    {
        _tracked.Remove(entity);
    }

    protected abstract bool ShouldEntityCleanup(EntityUid uid);
}
