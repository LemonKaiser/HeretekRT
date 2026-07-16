using System.Diagnostics;
using System.Globalization;
using System.IO;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.RoundEnd;
using Content.Server.Worldgen.Components;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.NPC;
using Content.Shared.Parallax.Biomes;
using Content.Shared._Mono.CCVar;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Mono.LongRun;

/// <summary>
/// Collects low-frequency, read-only health information for long-running rounds.
/// The system deliberately keeps a bounded sample buffer and never mutates gameplay state.
/// </summary>
public sealed class LongRunHealthSystem : EntitySystem
{
    private const int FrameSampleCapacity = 512;
    private const double BytesPerMebibyte = 1024d * 1024d;
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TrendSampleInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TrendWarningLogInterval = TimeSpan.FromHours(1);

    private static readonly Gauge ManagedHeapBytes = Metrics.CreateGauge(
        "longrun_health_managed_heap_bytes",
        "Managed heap size observed by the long-run health sampler.");
    private static readonly Gauge ProcessWorkingSetBytes = Metrics.CreateGauge(
        "longrun_health_process_working_set_bytes",
        "Process working set observed by the long-run health sampler.");
    private static readonly Gauge EntityCount = Metrics.CreateGauge(
        "longrun_health_entity_count",
        "Total metadata entities observed by the long-run health sampler.");
    private static readonly Gauge LoadedMapCount = Metrics.CreateGauge(
        "longrun_health_loaded_map_count",
        "Loaded map count observed by the long-run health sampler.");
    private static readonly Gauge PausedMapCount = Metrics.CreateGauge(
        "longrun_health_paused_map_count",
        "Paused loaded map count observed by the long-run health sampler.");
    private static readonly Gauge LoadedGridCount = Metrics.CreateGauge(
        "longrun_health_loaded_grid_count",
        "Loaded grid count observed by the long-run health sampler.");
    private static readonly Gauge PausedGridCount = Metrics.CreateGauge(
        "longrun_health_paused_grid_count",
        "Paused loaded grid count observed by the long-run health sampler.");
    private static readonly Gauge ColdSystemCount = Metrics.CreateGauge(
        "longrun_health_cold_system_count",
        "Cold-unloaded Koronus system count observed by the long-run health sampler.");
    private static readonly Gauge BiomeModifiedChunkCount = Metrics.CreateGauge(
        "longrun_health_biome_modified_chunk_count",
        "Biome modified chunk count observed by the long-run health sampler.");
    private static readonly Gauge BiomeLoadedChunkCount = Metrics.CreateGauge(
        "longrun_health_biome_loaded_chunk_count",
        "Biome loaded chunk count observed by the long-run health sampler.");
    private static readonly Gauge WorldgenChunkCount = Metrics.CreateGauge(
        "longrun_health_worldgen_chunk_count",
        "Worldgen controller chunk count observed by the long-run health sampler.");
    private static readonly Gauge ActiveNpcCount = Metrics.CreateGauge(
        "longrun_health_active_npc_count",
        "Active NPC count observed by the long-run health sampler.");
    private static readonly Gauge FrameTimeP95 = Metrics.CreateGauge(
        "longrun_health_frame_time_seconds_p95",
        "95th percentile simulation frame time from the bounded recent sample window.");
    private static readonly Gauge FrameTimeMax = Metrics.CreateGauge(
        "longrun_health_frame_time_seconds_max",
        "Maximum simulation frame time from the bounded recent sample window.");
    private static readonly Gauge Generation2Collections = Metrics.CreateGauge(
        "longrun_health_generation2_collections_total",
        "Total generation 2 collections observed by the long-run health sampler.");
    private static readonly Gauge DiskFreeBytes = Metrics.CreateGauge(
        "longrun_health_disk_free_bytes",
        "Available space on the drive containing the server working directory.");
    private static readonly Gauge WarningCount = Metrics.CreateGauge(
        "longrun_health_warning_count",
        "Number of active long-run configuration warnings.");
    private static readonly Gauge DeviceNetworkQueuedPackets = Metrics.CreateGauge(
        "longrun_health_device_network_queued_packets",
        "Queued device network packets observed by the long-run health sampler.");
    private static readonly Gauge DeviceNetworkDroppedPackets = Metrics.CreateGauge(
        "longrun_health_device_network_dropped_packets_total",
        "Device network packets rejected by the safety cap.");
    private static readonly Gauge PathfindingPendingRequests = Metrics.CreateGauge(
        "longrun_health_pathfinding_pending_requests",
        "Pending pathfinding requests observed by the long-run health sampler.");
    private static readonly Gauge PathfindingRejectedRequests = Metrics.CreateGauge(
        "longrun_health_pathfinding_rejected_requests_total",
        "Pathfinding requests rejected by the safety cap.");
    private static readonly Gauge ExplosionQueuedWork = Metrics.CreateGauge(
        "longrun_health_explosion_queued_work",
        "Queued explosion work observed by the long-run health sampler.");
    private static readonly Gauge TrendWindowHours = Metrics.CreateGauge(
        "longrun_health_trend_window_hours",
        "Time window covered by the bounded long-run trend history.");
    private static readonly Gauge ManagedHeapGrowthPerHour = Metrics.CreateGauge(
        "longrun_health_managed_heap_growth_bytes_per_hour",
        "Linear managed heap growth trend over the bounded history window.");
    private static readonly Gauge WorkingSetGrowthPerHour = Metrics.CreateGauge(
        "longrun_health_working_set_growth_bytes_per_hour",
        "Linear process working set growth trend over the bounded history window.");
    private static readonly Gauge EntityGrowthPerHour = Metrics.CreateGauge(
        "longrun_health_entity_growth_per_hour",
        "Linear entity count growth trend over the bounded history window.");
    private static readonly Gauge BiomeLoadedChunkGrowthPerHour = Metrics.CreateGauge(
        "longrun_health_biome_loaded_chunk_growth_per_hour",
        "Linear loaded biome chunk growth trend over the bounded history window.");
    private static readonly Gauge WorldgenChunkGrowthPerHour = Metrics.CreateGauge(
        "longrun_health_worldgen_chunk_growth_per_hour",
        "Linear world generation chunk growth trend over the bounded history window.");
    private static readonly Gauge LandingReservationCount = Metrics.CreateGauge(
        "longrun_health_landing_reservation_count",
        "Active Koronus planetary landing reservations.");
    private static readonly Gauge LandingSessionCount = Metrics.CreateGauge(
        "longrun_health_landing_session_count",
        "Active Koronus planetary parking sessions.");
    private static readonly Gauge PlanetaryTransitCount = Metrics.CreateGauge(
        "longrun_health_planetary_transit_count",
        "Active Koronus planetary atmospheric transits.");
    private static readonly Gauge BoundaryCleanupCandidateCount = Metrics.CreateGauge(
        "longrun_health_boundary_cleanup_candidate_count",
        "Koronus system boundary emergency cleanup candidates.");
    private static readonly Gauge BoundaryWarningHistoryCount = Metrics.CreateGauge(
        "longrun_health_boundary_warning_history_count",
        "Retained Koronus boundary warning cooldown entries.");

    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private ExplosionSystem _explosions = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private KoronusSectorBoundarySystem _sectorBoundaries = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;

    private readonly float[] _frameSamples = new float[FrameSampleCapacity];
    private readonly float[] _frameSortBuffer = new float[FrameSampleCapacity];
    private readonly object _snapshotLock = new();
    private readonly LongRunTrendTracker _trends = new();

    private LongRunHealthSnapshot _snapshot = LongRunHealthSnapshot.Empty;
    private TimeSpan _nextSnapshot;
    private TimeSpan _nextTrendSample;
    private TimeSpan _nextTrendWarningLog;
    private int _frameSampleCount;
    private int _frameSampleIndex;
    private int _roundId;
    private GameRunLevel _runLevel = GameRunLevel.PreRoundLobby;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (frameTime > 0f && !float.IsNaN(frameTime) && !float.IsInfinity(frameTime))
        {
            _frameSamples[_frameSampleIndex] = frameTime;
            _frameSampleIndex = (_frameSampleIndex + 1) % FrameSampleCapacity;
            _frameSampleCount = Math.Min(_frameSampleCount + 1, FrameSampleCapacity);
        }

        if (_timing.RealTime < _nextSnapshot)
            return;

        _nextSnapshot = _timing.RealTime + SnapshotInterval;
        CollectSnapshot();
    }

    /// <summary>
    /// Returns an immutable snapshot suitable for status endpoints and admin commands.
    /// </summary>
    public LongRunHealthSnapshot GetSnapshot()
    {
        lock (_snapshotLock)
        {
            return _snapshot;
        }
    }

    public string GetStatus()
    {
        var snapshot = GetSnapshot();
        var age = DateTime.UtcNow - snapshot.CapturedUtc;
        var warnings = snapshot.Warnings.Length == 0
            ? "нет"
            : string.Join("; ", snapshot.Warnings);

        return string.Join(", ",
            $"round={snapshot.RoundId}",
            $"level={snapshot.RunLevel}",
            $"age={age.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s",
            $"heap={FormatBytes(snapshot.ManagedHeapBytes)}",
            $"rss={FormatBytes(snapshot.ProcessWorkingSetBytes)}",
            $"entities={snapshot.EntityCount}",
            $"maps={snapshot.LoadedMapCount}/{snapshot.PausedMapCount} paused",
            $"grids={snapshot.LoadedGridCount}/{snapshot.PausedGridCount} paused",
            $"cold={snapshot.ColdSystemCount}",
            $"sector={snapshot.KnownSystemCount}/{snapshot.KnownSurfaceCount}",
            $"landing={snapshot.LandingReservationCount}/{snapshot.LandingSessionCount}/{snapshot.PlanetaryTransitCount}",
            $"boundary={snapshot.BoundaryCleanupCandidateCount}/{snapshot.BoundaryWarningHistoryCount}",
            $"biomeModified={snapshot.BiomeModifiedChunkCount}",
            $"biomeLoaded={snapshot.BiomeLoadedChunkCount}",
            $"worldgen={snapshot.WorldgenChunkCount}",
            $"npc={snapshot.ActiveNpcCount}",
            $"frameP95={(snapshot.FrameTimeP95 * 1000f).ToString("F2", CultureInfo.InvariantCulture)}ms",
            $"frameMax={(snapshot.FrameTimeMax * 1000f).ToString("F2", CultureInfo.InvariantCulture)}ms",
            $"gen2={snapshot.Generation2Collections}",
            $"trend={snapshot.TrendWindowHours.ToString("F1", CultureInfo.InvariantCulture)}h/{snapshot.TrendSampleCount}",
            $"heapTrend={FormatByteRate(snapshot.ManagedHeapGrowthBytesPerHour)}",
            $"rssTrend={FormatByteRate(snapshot.WorkingSetGrowthBytesPerHour)}",
            $"entityTrend={FormatCountRate(snapshot.EntityGrowthPerHour)}",
            $"biomeTrend={FormatCountRate(snapshot.BiomeLoadedChunkGrowthPerHour)}",
            $"worldgenTrend={FormatCountRate(snapshot.WorldgenChunkGrowthPerHour)}",
            $"deviceQueue={snapshot.DeviceNetworkQueuedPackets}",
            $"deviceDropped={snapshot.DeviceNetworkDroppedPackets}",
            $"pathQueue={snapshot.PathfindingPendingRequests}",
            $"pathRejected={snapshot.PathfindingRejectedRequests}",
            $"explosions={snapshot.ExplosionQueuedWork}/{snapshot.ExplosionUniqueQueuedWork}",
            $"diskFree={FormatBytes(snapshot.DiskFreeBytes)}",
            $"warnings={warnings}");
    }

    private void CollectSnapshot()
    {
        var frameStats = GetFrameStats();
        var entityCount = Count<MetaDataComponent>();
        var (loadedMaps, pausedMaps) = CountMaps();
        var (loadedGrids, pausedGrids) = CountGrids();
        var (coldSystems, knownSystems, knownSurfaces, landingReservations, landingSessions) = CountSectorMaps();
        var planetaryTransits = Count<KoronusPlanetaryTransitComponent>();
        var boundaryStatus = _sectorBoundaries.GetLongRunStatus();
        var (modifiedBiomeChunks, loadedBiomeChunks) = CountBiomeChunks();
        var worldgenChunks = CountWorldgenChunks();
        var activeNpcs = Count<ActiveNPCComponent>();
        var deviceNetwork = _deviceNetwork.GetLongRunStatus();
        var droppedDevicePackets = _deviceNetwork.GetLongRunDroppedPacketCount();
        var pendingPathfinding = _pathfinding.GetLongRunPendingRequestCount();
        var rejectedPathfinding = _pathfinding.GetLongRunRejectedRequestCount();
        var explosions = _explosions.GetLongRunStatus();
        var (managedHeap, workingSet) = GetMemory();
        var diskFree = GetDiskFreeBytes();
        var trend = UpdateTrend(
            managedHeap,
            workingSet,
            entityCount,
            loadedBiomeChunks,
            worldgenChunks);
        var warnings = GetLongRunWarnings(trend, out var trendWarnings);
        LogTrendWarnings(trendWarnings);

        var snapshot = new LongRunHealthSnapshot(
            DateTime.UtcNow,
            _roundId,
            _runLevel,
            entityCount,
            loadedMaps,
            pausedMaps,
            loadedGrids,
            pausedGrids,
            coldSystems,
            knownSystems,
            knownSurfaces,
            modifiedBiomeChunks,
            loadedBiomeChunks,
            worldgenChunks,
            activeNpcs,
            managedHeap,
            workingSet,
            frameStats.P95,
            frameStats.Max,
            GC.CollectionCount(2),
            diskFree,
            warnings,
            deviceNetwork.ActiveQueue + deviceNetwork.NextQueue,
            deviceNetwork.Networks,
            pendingPathfinding,
            explosions.Queued,
            explosions.UniqueQueued,
            explosions.Active,
            droppedDevicePackets,
            rejectedPathfinding,
            trend.SampleCount,
            trend.WindowHours,
            trend.ManagedHeapBytesPerHour,
            trend.WorkingSetBytesPerHour,
            trend.EntitiesPerHour,
            trend.BiomeLoadedChunksPerHour,
            trend.WorldgenChunksPerHour,
            landingReservations,
            landingSessions,
            planetaryTransits,
            boundaryStatus.CleanupCandidates,
            boundaryStatus.WarningHistory);

        lock (_snapshotLock)
        {
            _snapshot = snapshot;
        }

        ManagedHeapBytes.Set(managedHeap);
        ProcessWorkingSetBytes.Set(workingSet);
        EntityCount.Set(entityCount);
        LoadedMapCount.Set(loadedMaps);
        PausedMapCount.Set(pausedMaps);
        LoadedGridCount.Set(loadedGrids);
        PausedGridCount.Set(pausedGrids);
        ColdSystemCount.Set(coldSystems);
        BiomeModifiedChunkCount.Set(modifiedBiomeChunks);
        BiomeLoadedChunkCount.Set(loadedBiomeChunks);
        WorldgenChunkCount.Set(worldgenChunks);
        ActiveNpcCount.Set(activeNpcs);
        FrameTimeP95.Set(frameStats.P95);
        FrameTimeMax.Set(frameStats.Max);
        Generation2Collections.Set(GC.CollectionCount(2));
        DiskFreeBytes.Set(diskFree);
        WarningCount.Set(warnings.Length);
        DeviceNetworkQueuedPackets.Set(deviceNetwork.ActiveQueue + deviceNetwork.NextQueue);
        DeviceNetworkDroppedPackets.Set(droppedDevicePackets);
        PathfindingPendingRequests.Set(pendingPathfinding);
        PathfindingRejectedRequests.Set(rejectedPathfinding);
        ExplosionQueuedWork.Set(explosions.Queued);
        TrendWindowHours.Set(trend.WindowHours);
        ManagedHeapGrowthPerHour.Set(trend.ManagedHeapBytesPerHour);
        WorkingSetGrowthPerHour.Set(trend.WorkingSetBytesPerHour);
        EntityGrowthPerHour.Set(trend.EntitiesPerHour);
        BiomeLoadedChunkGrowthPerHour.Set(trend.BiomeLoadedChunksPerHour);
        WorldgenChunkGrowthPerHour.Set(trend.WorldgenChunksPerHour);
        LandingReservationCount.Set(landingReservations);
        LandingSessionCount.Set(landingSessions);
        PlanetaryTransitCount.Set(planetaryTransits);
        BoundaryCleanupCandidateCount.Set(boundaryStatus.CleanupCandidates);
        BoundaryWarningHistoryCount.Set(boundaryStatus.WarningHistory);
    }

    private (int Loaded, int Paused) CountMaps()
    {
        var loaded = 0;
        var paused = 0;
        var query = AllEntityQuery<MapComponent, MetaDataComponent>();

        while (query.MoveNext(out _, out _, out var metadata))
        {
            loaded++;
            if (metadata.EntityPaused)
                paused++;
        }

        return (loaded, paused);
    }

    private (int Loaded, int Paused) CountGrids()
    {
        var loaded = 0;
        var paused = 0;
        var query = AllEntityQuery<MapGridComponent, MetaDataComponent>();

        while (query.MoveNext(out _, out _, out var metadata))
        {
            loaded++;
            if (metadata.EntityPaused)
                paused++;
        }

        return (loaded, paused);
    }

    private (int Cold, int KnownSystems, int KnownSurfaces, int LandingReservations, int LandingSessions) CountSectorMaps()
    {
        var cold = 0;
        var systems = 0;
        var surfaces = 0;
        var reservations = 0;
        var sessions = 0;
        var query = AllEntityQuery<KoronusSectorRuleComponent>();

        while (query.MoveNext(out _, out var sector))
        {
            cold += sector.ColdUnloadedSystems.Count;
            systems += sector.SystemMaps.Count;
            surfaces += sector.SurfaceMaps.Count;
            reservations += sector.LandingReservations.Count;
            sessions += sector.LandingSessions.Count;
        }

        return (cold, systems, surfaces, reservations, sessions);
    }

    private (int Modified, int Loaded) CountBiomeChunks()
    {
        var modified = 0;
        var loaded = 0;
        var query = AllEntityQuery<BiomeComponent>();

        while (query.MoveNext(out _, out var biome))
        {
            modified += biome.ModifiedTiles.Count;
            loaded += biome.LoadedChunks.Count;
        }

        return (modified, loaded);
    }

    private int CountWorldgenChunks()
    {
        var chunks = 0;
        var query = AllEntityQuery<WorldControllerComponent>();

        while (query.MoveNext(out _, out var controller))
            chunks += controller.Chunks.Count;

        return chunks;
    }

    private (long ManagedHeap, long WorkingSet) GetMemory()
    {
        var managed = GC.GetTotalMemory(false);
        long workingSet;

        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            workingSet = process.WorkingSet64;
        }
        catch (Exception exception)
        {
            Log.Debug($"Unable to read process working set: {exception.Message}");
            workingSet = 0;
        }

        return (managed, workingSet);
    }

    private static long GetDiskFreeBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.CurrentDirectory);
            if (string.IsNullOrEmpty(root))
                return 0;

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    private (float P95, float Max) GetFrameStats()
    {
        if (_frameSampleCount == 0)
            return (0f, 0f);

        Array.Copy(_frameSamples, _frameSortBuffer, _frameSampleCount);
        Array.Sort(_frameSortBuffer, 0, _frameSampleCount);

        var p95Index = Math.Clamp((int)Math.Ceiling(_frameSampleCount * 0.95) - 1, 0, _frameSampleCount - 1);
        return (_frameSortBuffer[p95Index], _frameSortBuffer[_frameSampleCount - 1]);
    }

    private LongRunTrendSnapshot UpdateTrend(
        long managedHeap,
        long workingSet,
        int entityCount,
        int biomeLoadedChunks,
        int worldgenChunks)
    {
        if (_roundId <= 0)
            return LongRunTrendSnapshot.Empty;

        if (_timing.RealTime >= _nextTrendSample)
        {
            _trends.Add(
                _timing.RealTime,
                managedHeap,
                workingSet,
                entityCount,
                biomeLoadedChunks,
                worldgenChunks);
            _nextTrendSample = _timing.RealTime + TrendSampleInterval;
        }

        return _trends.GetTrend();
    }

    private string[] GetLongRunWarnings(LongRunTrendSnapshot trend, out string[] trendWarnings)
    {
        var warnings = new List<string>(10);

        var uptimeRestart = _configuration.GetCVar(CCVars.ServerUptimeRestartMinutes);
        if (uptimeRestart > 0)
            warnings.Add($"server.uptime_restart_minutes={uptimeRestart}");

        if (_configuration.GetCVar(CCVars.ReplayAutoRecord))
            warnings.Add("replay.auto_record=true");

        var maxTimeQuery = EntityQueryEnumerator<MaxTimeRestartRuleComponent, ActiveGameRuleComponent>();
        while (maxTimeQuery.MoveNext(out _, out var rule, out _))
            warnings.Add($"MaxTimeRestart={rule.RoundMaxTime.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)}m");

        var inactivityQuery = EntityQueryEnumerator<InactivityRuleComponent, ActiveGameRuleComponent>();
        while (inactivityQuery.MoveNext(out _, out _, out _))
            warnings.Add("InactivityRestart=active");

        var roundEndQuery = EntityQueryEnumerator<RoundEndTimeRuleComponent, ActiveGameRuleComponent>();
        while (roundEndQuery.MoveNext(out _, out var rule, out _))
            warnings.Add($"RoundEndTime={rule.EndAt.TotalHours.ToString("F1", CultureInfo.InvariantCulture)}h");

        trendWarnings = GetTrendWarnings(trend);
        warnings.AddRange(trendWarnings);
        return warnings.ToArray();
    }

    private string[] GetTrendWarnings(LongRunTrendSnapshot trend)
    {
        var minimumHours = Math.Max(0.25f, _configuration.GetCVar(MonoCVars.LongRunTrendMinimumHours));
        if (trend.WindowHours < minimumHours)
            return Array.Empty<string>();

        var warnings = new List<string>(5);
        var heapThreshold = _configuration.GetCVar(MonoCVars.LongRunManagedHeapWarningMiBPerHour);
        var heapGrowthMiB = trend.ManagedHeapBytesPerHour / BytesPerMebibyte;
        if (heapThreshold > 0f && heapGrowthMiB >= heapThreshold)
            warnings.Add($"trend.managed_heap={FormatSigned(heapGrowthMiB)}MiB/h/{trend.WindowHours:F1}h");

        var workingSetThreshold = _configuration.GetCVar(MonoCVars.LongRunWorkingSetWarningMiBPerHour);
        var workingSetGrowthMiB = trend.WorkingSetBytesPerHour / BytesPerMebibyte;
        if (workingSetThreshold > 0f && workingSetGrowthMiB >= workingSetThreshold)
            warnings.Add($"trend.working_set={FormatSigned(workingSetGrowthMiB)}MiB/h/{trend.WindowHours:F1}h");

        var entityThreshold = _configuration.GetCVar(MonoCVars.LongRunEntityWarningPerHour);
        if (entityThreshold > 0f && trend.EntitiesPerHour >= entityThreshold)
            warnings.Add($"trend.entities={FormatSigned(trend.EntitiesPerHour)}/h/{trend.WindowHours:F1}h");

        var biomeThreshold = _configuration.GetCVar(MonoCVars.LongRunBiomeChunkWarningPerHour);
        if (biomeThreshold > 0f && trend.BiomeLoadedChunksPerHour >= biomeThreshold)
            warnings.Add($"trend.biome_loaded={FormatSigned(trend.BiomeLoadedChunksPerHour)}/h/{trend.WindowHours:F1}h");

        var worldgenThreshold = _configuration.GetCVar(MonoCVars.LongRunWorldgenChunkWarningPerHour);
        if (worldgenThreshold > 0f && trend.WorldgenChunksPerHour >= worldgenThreshold)
            warnings.Add($"trend.worldgen={FormatSigned(trend.WorldgenChunksPerHour)}/h/{trend.WindowHours:F1}h");

        return warnings.ToArray();
    }

    private void LogTrendWarnings(string[] warnings)
    {
        if (warnings.Length == 0 || _timing.RealTime < _nextTrendWarningLog)
            return;

        _nextTrendWarningLog = _timing.RealTime + TrendWarningLogInterval;
        Log.Warning($"Long-run growth warning: {string.Join("; ", warnings)}");
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        _runLevel = ev.New;
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _roundId = ev.RoundId;
        _frameSampleCount = 0;
        _frameSampleIndex = 0;
        _nextSnapshot = TimeSpan.Zero;
        _nextTrendSample = TimeSpan.Zero;
        _nextTrendWarningLog = TimeSpan.Zero;
        _trends.Clear();

        foreach (var warning in GetLongRunWarnings(LongRunTrendSnapshot.Empty, out _))
            Log.Warning($"Long-run configuration warning: {warning}");
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _roundId = 0;
        _runLevel = GameRunLevel.PreRoundLobby;
        _frameSampleCount = 0;
        _frameSampleIndex = 0;
        _nextSnapshot = TimeSpan.Zero;
        _nextTrendSample = TimeSpan.Zero;
        _nextTrendWarningLog = TimeSpan.Zero;
        _trends.Clear();

        lock (_snapshotLock)
        {
            _snapshot = LongRunHealthSnapshot.Empty;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "n/a";

        var value = (double) bytes;
        var suffix = "B";
        if (value >= 1024)
        {
            value /= 1024;
            suffix = "KiB";
        }

        if (value >= 1024)
        {
            value /= 1024;
            suffix = "MiB";
        }

        if (value >= 1024)
        {
            value /= 1024;
            suffix = "GiB";
        }

        return $"{value.ToString("F1", CultureInfo.InvariantCulture)}{suffix}";
    }

    private static string FormatByteRate(double bytesPerHour)
    {
        return $"{FormatSigned(bytesPerHour / BytesPerMebibyte)}MiB/h";
    }

    private static string FormatCountRate(double countPerHour)
    {
        return $"{FormatSigned(countPerHour)}/h";
    }

    private static string FormatSigned(double value)
    {
        return value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
    }
}

public sealed record LongRunHealthSnapshot(
    DateTime CapturedUtc,
    int RoundId,
    GameRunLevel RunLevel,
    int EntityCount,
    int LoadedMapCount,
    int PausedMapCount,
    int LoadedGridCount,
    int PausedGridCount,
    int ColdSystemCount,
    int KnownSystemCount,
    int KnownSurfaceCount,
    int BiomeModifiedChunkCount,
    int BiomeLoadedChunkCount,
    int WorldgenChunkCount,
    int ActiveNpcCount,
    long ManagedHeapBytes,
    long ProcessWorkingSetBytes,
    float FrameTimeP95,
    float FrameTimeMax,
    int Generation2Collections,
    long DiskFreeBytes,
    string[] Warnings,
    int DeviceNetworkQueuedPackets,
    int DeviceNetworkCount,
    int PathfindingPendingRequests,
    int ExplosionQueuedWork,
    int ExplosionUniqueQueuedWork,
    bool ExplosionActive,
    long DeviceNetworkDroppedPackets,
    long PathfindingRejectedRequests,
    int TrendSampleCount,
    double TrendWindowHours,
    double ManagedHeapGrowthBytesPerHour,
    double WorkingSetGrowthBytesPerHour,
    double EntityGrowthPerHour,
    double BiomeLoadedChunkGrowthPerHour,
    double WorldgenChunkGrowthPerHour,
    int LandingReservationCount,
    int LandingSessionCount,
    int PlanetaryTransitCount,
    int BoundaryCleanupCandidateCount,
    int BoundaryWarningHistoryCount)
{
    public static LongRunHealthSnapshot Empty { get; } = new(
        DateTime.UtcNow,
        0,
        GameRunLevel.PreRoundLobby,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0f,
        0f,
        0,
        0,
        Array.Empty<string>(),
        0,
        0,
        0,
        0,
        0,
        false,
        0,
        0,
        0,
        0d,
        0d,
        0d,
        0d,
        0d,
        0,
        0,
        0,
        0,
        0,
        0);
}
