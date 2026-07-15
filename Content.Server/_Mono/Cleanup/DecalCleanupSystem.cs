using Content.Server.Decals;
using Content.Shared._Mono.CCVar;
using Content.Shared.Decals;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Applies a pressure cap to cleanable decals. Non-cleanable map art is never touched.
/// </summary>
public sealed partial class DecalCleanupSystem : EntitySystem
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Queue<EntityUid> _gridScanQueue = new();
    private readonly Queue<(EntityUid Grid, uint Decal)> _deleteQueue = new();
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();

    private bool _enabled;
    private bool _dryRun;
    private int _maxPerGrid;
    private int _maxChecksPerTick;
    private int _maxDeletesPerTick;
    private TimeSpan _maximumProcessTime;
    private TimeSpan _nextScan;
    private long _totalEligible;
    private long _totalDeleted;
    private int _batchEligible;
    private int _batchDeleted;
    private bool _batchActive;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        Subs.CVar(_cfg, MonoCVars.CleanupEnabled, value => _enabled = value, true);
        Subs.CVar(_cfg, MonoCVars.CleanupDryRun, value => _dryRun = value, true);
        Subs.CVar(_cfg, MonoCVars.DecalCleanupMaxPerGrid, value =>
        {
            _maxPerGrid = Math.Max(0, value);
            _deleteQueue.Clear();
        }, true);
        Subs.CVar(_cfg, MonoCVars.CleanupMaxChecksPerTick, value => _maxChecksPerTick = Math.Max(1, value), true);
        Subs.CVar(_cfg, MonoCVars.CleanupMaxDeletesPerTick, value => _maxDeletesPerTick = Math.Max(1, value), true);
        Subs.CVar(_cfg, MonoCVars.CleanupMaximumProcessTimeMs,
            value => _maximumProcessTime = TimeSpan.FromMilliseconds(Math.Max(0.05f, value)), true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
        {
            _gridScanQueue.Clear();
            _deleteQueue.Clear();
            _batchActive = false;
            return;
        }

        if (_dryRun && _deleteQueue.Count != 0)
            _deleteQueue.Clear();

        _stopwatch.Restart();
        var deletedThisTick = 0;
        while (_deleteQueue.Count != 0 &&
               deletedThisTick < _maxDeletesPerTick &&
               _stopwatch.Elapsed < _maximumProcessTime)
        {
            var (grid, decalId) = _deleteQueue.Dequeue();
            if (!TryComp<DecalGridComponent>(grid, out var component) ||
                !IsStillCleanable(component, decalId))
            {
                continue;
            }

            if (_decals.RemoveDecal(grid, decalId, component))
            {
                deletedThisTick++;
                _batchDeleted++;
                _totalDeleted++;
            }
        }

        var checkedThisTick = 0;
        while (_deleteQueue.Count == 0 &&
               _gridScanQueue.Count != 0 &&
               checkedThisTick < _maxChecksPerTick &&
               _stopwatch.Elapsed < _maximumProcessTime)
        {
            var grid = _gridScanQueue.Dequeue();
            checkedThisTick++;
            if (TryComp<DecalGridComponent>(grid, out var component))
                QueueExcessDecals(grid, component);
        }

        if (_deleteQueue.Count != 0 || _gridScanQueue.Count != 0)
            return;

        if (_batchActive)
            FinishBatch();

        if (_timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + CleanupInterval;
        _batchEligible = 0;
        _batchDeleted = 0;
        _batchActive = true;

        var query = EntityQueryEnumerator<DecalGridComponent>();
        while (query.MoveNext(out var grid, out _))
            _gridScanQueue.Enqueue(grid);
    }

    public string GetCleanupStatus()
    {
        return $"{nameof(DecalCleanupSystem)}: grids={_gridScanQueue.Count}, pending={_deleteQueue.Count}, " +
               $"eligible={_totalEligible}, deleted={_totalDeleted}, enabled={_enabled}, dryRun={_dryRun}";
    }

    private void QueueExcessDecals(EntityUid grid, DecalGridComponent component)
    {
        var cleanable = new List<uint>();
        foreach (var chunk in component.ChunkCollection.ChunkCollection.Values)
        {
            foreach (var (id, decal) in chunk.Decals)
            {
                if (decal.Cleanable)
                    cleanable.Add(id);
            }
        }

        var excess = cleanable.Count - _maxPerGrid;
        if (excess <= 0)
            return;

        cleanable.Sort();
        _batchEligible += excess;
        _totalEligible += excess;

        if (_dryRun)
            return;

        for (var i = 0; i < excess; i++)
            _deleteQueue.Enqueue((grid, cleanable[i]));
    }

    private static bool IsStillCleanable(DecalGridComponent component, uint decalId)
    {
        foreach (var chunk in component.ChunkCollection.ChunkCollection.Values)
        {
            if (chunk.Decals.TryGetValue(decalId, out var decal))
                return decal.Cleanable;
        }

        return false;
    }

    private void FinishBatch()
    {
        _batchActive = false;
        if (_batchEligible == 0)
            return;

        Log.Info($"Decal cleanup batch: eligible={_batchEligible}, deleted={_batchDeleted}, dryRun={_dryRun}.");
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _gridScanQueue.Clear();
        _deleteQueue.Clear();
        _nextScan = TimeSpan.Zero;
        _totalEligible = 0;
        _totalDeleted = 0;
        _batchEligible = 0;
        _batchDeleted = 0;
        _batchActive = false;
    }
}
