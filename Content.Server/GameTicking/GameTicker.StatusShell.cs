using System.Linq;
using System.Text.Json.Nodes;
using Content.Server._Mono.LongRun;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Server.ServerStatus;
using Robust.Shared.Configuration;

namespace Content.Server.GameTicking
{
    public sealed partial class GameTicker
    {
        /// <summary>
        ///     Used for thread safety, given <see cref="IStatusHost.OnStatusRequest"/> is called from another thread.
        /// </summary>
        private readonly object _statusShellLock = new();

        /// <summary>
        ///     Round start time in UTC, for status shell purposes.
        /// </summary>
        [ViewVariables]
        private DateTime _roundStartDateTime;

        /// <summary>
        ///     For access to CVars in status responses.
        /// </summary>
        [Dependency] private IConfigurationManager _cfg = default!;
        /// <summary>
        ///     For access to the round ID in status responses.
        /// </summary>
        [Dependency] private SharedGameTicker _gameTicker = default!;

        /// <summary>
        ///     Immutable long-run snapshot. The health system never exposes live entity state here because this
        ///     callback is invoked from the status thread.
        /// </summary>
        [Dependency] private LongRunHealthSystem _longRunHealth = default!;

        private void InitializeStatusShell()
        {
            IoCManager.Resolve<IStatusHost>().OnStatusRequest += GetStatusResponse;
        }

        private void GetStatusResponse(JsonNode jObject)
        {
            var preset = CurrentPreset ?? Preset;

            // This method is raised from another thread, so this better be thread safe!
            lock (_statusShellLock)
            {
                jObject["name"] = _baseServer.ServerName;
                jObject["map"] = _gameMapManager.GetSelectedMap()?.MapName;
                jObject["round_id"] = _gameTicker.RoundId;
                jObject["players"] = _cfg.GetCVar(CCVars.AdminsCountInReportedPlayerCount)
                    ? _playerManager.PlayerCount
                    : _playerManager.PlayerCount - _adminManager.ActiveAdmins.Count();
                jObject["soft_max_players"] = _cfg.GetCVar(CCVars.SoftMaxPlayers);
                jObject["panic_bunker"] = _cfg.GetCVar(CCVars.PanicBunkerEnabled);
                jObject["run_level"] = (int) _runLevel;
                if (preset != null)
                    jObject["preset"] = Loc.GetString(preset.ModeTitle);
                if (_runLevel >= GameRunLevel.InRound)
                {
                    jObject["round_start_time"] = _roundStartDateTime.ToString("o");
                }

                var health = _longRunHealth.GetSnapshot();
                jObject["longrun"] = new JsonObject
                {
                    ["captured_utc"] = health.CapturedUtc.ToString("o"),
                    ["entities"] = health.EntityCount,
                    ["loaded_maps"] = health.LoadedMapCount,
                    ["paused_maps"] = health.PausedMapCount,
                    ["loaded_grids"] = health.LoadedGridCount,
                    ["paused_grids"] = health.PausedGridCount,
                    ["cold_systems"] = health.ColdSystemCount,
                    ["known_systems"] = health.KnownSystemCount,
                    ["known_surfaces"] = health.KnownSurfaceCount,
                    ["landing_reservations"] = health.LandingReservationCount,
                    ["landing_sessions"] = health.LandingSessionCount,
                    ["planetary_transits"] = health.PlanetaryTransitCount,
                    ["boundary_cleanup_candidates"] = health.BoundaryCleanupCandidateCount,
                    ["boundary_warning_history"] = health.BoundaryWarningHistoryCount,
                    ["biome_modified_chunks"] = health.BiomeModifiedChunkCount,
                    ["biome_loaded_chunks"] = health.BiomeLoadedChunkCount,
                    ["worldgen_chunks"] = health.WorldgenChunkCount,
                    ["active_npcs"] = health.ActiveNpcCount,
                    ["managed_heap_bytes"] = health.ManagedHeapBytes,
                    ["process_working_set_bytes"] = health.ProcessWorkingSetBytes,
                    ["frame_time_p95_seconds"] = health.FrameTimeP95,
                    ["frame_time_max_seconds"] = health.FrameTimeMax,
                    ["generation2_collections"] = health.Generation2Collections,
                    ["disk_free_bytes"] = health.DiskFreeBytes,
                    ["trend_sample_count"] = health.TrendSampleCount,
                    ["trend_window_hours"] = health.TrendWindowHours,
                    ["managed_heap_growth_bytes_per_hour"] = health.ManagedHeapGrowthBytesPerHour,
                    ["working_set_growth_bytes_per_hour"] = health.WorkingSetGrowthBytesPerHour,
                    ["entity_growth_per_hour"] = health.EntityGrowthPerHour,
                    ["biome_loaded_chunk_growth_per_hour"] = health.BiomeLoadedChunkGrowthPerHour,
                    ["worldgen_chunk_growth_per_hour"] = health.WorldgenChunkGrowthPerHour,
                    ["device_network_queued_packets"] = health.DeviceNetworkQueuedPackets,
                    ["device_network_dropped_packets"] = health.DeviceNetworkDroppedPackets,
                    ["device_network_count"] = health.DeviceNetworkCount,
                    ["pathfinding_pending_requests"] = health.PathfindingPendingRequests,
                    ["pathfinding_rejected_requests"] = health.PathfindingRejectedRequests,
                    ["explosion_queued_work"] = health.ExplosionQueuedWork,
                    ["explosion_unique_queued_work"] = health.ExplosionUniqueQueuedWork,
                    ["explosion_active"] = health.ExplosionActive,
                    ["warnings"] = new JsonArray(health.Warnings.Select(warning => (JsonNode) warning).ToArray()),
                };
            }
        }
    }
}
