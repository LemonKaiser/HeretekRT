using Content.Server._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Ghost;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Keeps populated systems running and pauses empty remote systems after their configured grace period.
/// </summary>
public sealed class KoronusSectorResidencySystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private TimeSpan _nextUpdate;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(250);
    private readonly HashSet<MapId> _playerMaps = new();
    private readonly HashSet<MapId> _adminGhostMaps = new();
    private readonly HashSet<MapId> _planetaryTransitMaps = new();
    private readonly List<string> _coldUnloadCandidates = new();
    private static readonly TimeSpan ColdUnloadRetryDelay = TimeSpan.FromMinutes(1);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;
        BuildWakeMapIndex();
        BuildPlanetaryTransitMapIndex();
        _coldUnloadCandidates.Clear();

        var query = EntityQueryEnumerator<KoronusSystemMapComponent, MapComponent>();
        while (query.MoveNext(out _, out var runtime, out var map))
        {
            if (!_sector.TryGetSystemPrototype(runtime.SystemId, out var system))
                continue;

            var occupied = _playerMaps.Contains(map.MapId) ||
                           _planetaryTransitMaps.Contains(map.MapId) ||
                           system.UnpauseOnAdminGhost && _adminGhostMaps.Contains(map.MapId);
            UpdateSystem(runtime, map.MapId, system, occupied);
        }

        var surfaces = EntityQueryEnumerator<KoronusPlanetSurfaceMapComponent, MapComponent>();
        while (surfaces.MoveNext(out _, out var runtime, out var map))
        {
            if (!_prototypes.TryIndex<KoronusPlanetSurfacePrototype>(runtime.SurfaceId, out var surface))
                continue;

            UpdateSurface(runtime, map.MapId, surface,
                _playerMaps.Contains(map.MapId) || _planetaryTransitMaps.Contains(map.MapId));
        }

        // Deleting maps while an entity query is iterating over their map components can invalidate
        // the query. Snapshot candidates are therefore processed only after both residency scans finish.
        foreach (var systemId in _coldUnloadCandidates)
        {
            if (_sector.TryColdUnloadSystem(systemId) ||
                !TryGetRuntime(systemId, out var runtime, out _))
            {
                continue;
            }

            runtime.ColdUnloadRetryAt = _timing.CurTime + ColdUnloadRetryDelay;
        }
    }

    /// <summary>
    /// Prevents a destination from being re-paused while a sector jump is travelling to it.
    /// </summary>
    public bool BeginIncomingSectorJump(string systemId)
    {
        if (!_sector.TryEnsureSystemMapLoaded(systemId, out _) ||
            !TryGetRuntime(systemId, out var runtime, out var mapId))
        {
            return false;
        }

        if (_sector.TryGetSystemPrototype(systemId, out var system) && system.HoldAwakeOnIncomingSectorJump)
            runtime.IncomingSectorJumps++;
        runtime.EmptySince = null;
        runtime.ColdUnloadRetryAt = null;
        _maps.SetPaused(mapId, false);
        return true;
    }

    /// <summary>
    /// Releases the residency lock created by <see cref="BeginIncomingSectorJump"/>.
    /// </summary>
    public void EndIncomingSectorJump(string systemId)
    {
        if (!TryGetRuntime(systemId, out var runtime, out _))
            return;

        runtime.IncomingSectorJumps = int.Max(runtime.IncomingSectorJumps - 1, 0);
    }

    private void UpdateSystem(KoronusSystemMapComponent runtime, MapId mapId, KoronusSystemPrototype system, bool occupied)
    {
        if (occupied || runtime.IncomingSectorJumps > 0 || !system.PauseWhenEmpty)
        {
            runtime.EmptySince = null;
            runtime.ColdUnloadRetryAt = null;
            if (_maps.IsPaused(mapId))
                _maps.SetPaused(mapId, false);
            return;
        }

        runtime.EmptySince ??= _timing.CurTime;
        if (_timing.CurTime - runtime.EmptySince.Value < TimeSpan.FromSeconds(system.RepauseDelay))
            return;

        if (!_maps.IsPaused(mapId))
            _maps.SetPaused(mapId, true);

        if (!system.AllowColdUnload || system.ColdUnloadDelay <= 0f ||
            _timing.CurTime - runtime.EmptySince.Value < TimeSpan.FromSeconds(system.ColdUnloadDelay) ||
            runtime.ColdUnloadRetryAt is { } retryAt && _timing.CurTime < retryAt)
        {
            return;
        }

        _coldUnloadCandidates.Add(system.ID);
    }

    private void UpdateSurface(
        KoronusPlanetSurfaceMapComponent runtime,
        MapId mapId,
        KoronusPlanetSurfacePrototype surface,
        bool occupied)
    {
        if (occupied || !surface.PauseWhenEmpty)
        {
            runtime.EmptySince = null;
            if (_maps.IsPaused(mapId))
                _maps.SetPaused(mapId, false);
            return;
        }

        runtime.EmptySince ??= _timing.CurTime;
        if (_timing.CurTime - runtime.EmptySince.Value < TimeSpan.FromSeconds(surface.RepauseDelay))
            return;

        if (!_maps.IsPaused(mapId))
            _maps.SetPaused(mapId, true);
    }

    private void BuildWakeMapIndex()
    {
        _playerMaps.Clear();
        _adminGhostMaps.Clear();

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } entity ||
                !TryComp<TransformComponent>(entity, out var transform))
            {
                continue;
            }

            if (!TryComp<GhostComponent>(entity, out var ghost))
                _playerMaps.Add(transform.MapID);
            else if (ghost.CanGhostInteract)
                _adminGhostMaps.Add(transform.MapID);
        }
    }

    /// <summary>
    /// A controlled planetary transfer must keep both its departure and destination maps awake.
    /// Otherwise an unattended surface may be paused before the shuttle has left or arrived.
    /// </summary>
    private void BuildPlanetaryTransitMapIndex()
    {
        _planetaryTransitMaps.Clear();

        var transfers = EntityQueryEnumerator<KoronusPlanetaryTransitComponent>();
        while (transfers.MoveNext(out _, out var transfer))
        {
            _planetaryTransitMaps.Add(transfer.SourceMap);
            _planetaryTransitMaps.Add(transfer.TargetMap);
        }
    }

    private bool TryGetRuntime(string systemId, out KoronusSystemMapComponent runtime, out MapId mapId)
    {
        runtime = default!;
        mapId = MapId.Nullspace;

        if (!_sector.TryGetSystemMap(systemId, out mapId) ||
            !_maps.TryGetMap(mapId, out var mapUid) ||
            !TryComp<KoronusSystemMapComponent>(mapUid.Value, out var foundRuntime))
        {
            return false;
        }

        runtime = foundRuntime!;
        return true;
    }
}
