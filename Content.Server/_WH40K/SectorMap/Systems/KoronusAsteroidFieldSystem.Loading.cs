using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Systems;
using Content.Shared._WH40K.SectorMap.BUI;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server._Mono.GridClaimer;
using Content.Server.Shuttles.Components;
using Content.Shared.Item;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusAsteroidFieldSystem
{
    private void OnAsteroidStartup(EntityUid uid, KoronusMiningAsteroidComponent component, ComponentStartup args)
    {
        // Grid initialization adds ShuttleComponent again on restore. Preserve explicit player conversions.
        if (!component.IsShuttle)
            TryConfigureAsteroid(uid);
    }

    private void OnBeforeSerialization(BeforeSerializationEvent args)
    {
        var asteroids = AllEntityQuery<KoronusMiningAsteroidComponent, TransformComponent>();
        while (asteroids.MoveNext(out var uid, out var asteroid, out var transform))
            if (args.MapIds.Contains(transform.MapID))
            {
                asteroid.IsShuttle = HasComp<ShuttleComponent>(uid);
                asteroid.PristineOnRestore = TryComp<MapGridComponent>(uid, out var grid) &&
                    IsPristine(uid, asteroid, transform, grid);
                if (asteroid.PristineOnRestore)
                    asteroid.OriginalChildCount = transform.ChildCount;
            }
    }

    public List<KoronusAsteroidClusterState> GetNavigationFields(EntityUid observer)
    {
        var result = new List<KoronusAsteroidClusterState>();
        var xform = Transform(observer);
        if (!TryComp<KoronusAsteroidFieldComponent>(xform.MapUid, out var field))
            return result;

        for (var i = 0; i < field.Clusters.Count; i++)
        {
            var cluster = field.Clusters[i];
            result.Add(new KoronusAsteroidClusterState(i + 1, cluster.Position, cluster.Radius));
        }
        var position = _transform.GetWorldPosition(xform);
        result.Sort((a, b) => Vector2.DistanceSquared(a.Position, position)
            .CompareTo(Vector2.DistanceSquared(b.Position, position)));
        return result;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromMilliseconds(250);

        foreach (var positions in _observers.Values)
            positions.Clear();
        _occupiedGrids.Clear();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } uid || HasComp<GhostComponent>(uid) ||
                !TryComp<TransformComponent>(uid, out var xform) ||
                !HasComp<KoronusAsteroidFieldComponent>(xform.MapUid))
                continue;
            if (!_observers.TryGetValue(xform.MapID, out var positions))
                _observers[xform.MapID] = positions = new List<Vector2>();
            positions.Add(_transform.GetWorldPosition(xform));
            if (xform.GridUid is { } grid)
                _occupiedGrids.Add(grid);
        }

        var budget = 2;
        var fields = AllEntityQuery<KoronusAsteroidFieldComponent, MapComponent, KoronusSystemMapComponent>();
        while (fields.MoveNext(out var uid, out var field, out var map, out var runtime))
        {
            _observers.TryGetValue(map.MapId, out var positions);
            if (_timing.CurTime >= field.NextReplenishment)
            {
                field.ReplenishmentCycle++;
                field.NextReplenishment = _timing.CurTime + ReplenishmentInterval;
                ReplenishSites(uid, field, runtime.SystemId, positions);
            }
            if (_maps.IsPaused(map.MapId) || positions is not { Count: > 0 })
                continue;
            foreach (var position in positions)
            {
                if (budget <= 0)
                    break;
                budget -= MaterializeNearby(uid, position, budget);
            }
        }

        _retire.Clear();
        var asteroids = AllEntityQuery<KoronusMiningAsteroidComponent, TransformComponent, MapGridComponent>();
        while (asteroids.MoveNext(out var uid, out var asteroid, out var xform, out var grid))
        {
            // Child transforms start after their grid. Rebuild verified pristine references after map loading finishes.
            if (asteroid.PristineOnRestore)
            {
                if (xform.ChildCount == asteroid.OriginalChildCount)
                {
                    asteroid.OriginalChildren = new HashSet<EntityUid>();
                    var children = xform.ChildEnumerator;
                    while (children.MoveNext(out var child))
                        asteroid.OriginalChildren.Add(child);
                }
                asteroid.PristineOnRestore = false;
            }
            var position = _transform.GetWorldPosition(xform);
            _observers.TryGetValue(xform.MapID, out var positions);
            if (_occupiedGrids.Contains(uid) || IsNear(position, positions, 30f))
            {
                asteroid.Visited = true;
                asteroid.FarSince = null;
                asteroid.AbandonedSince = null;
                continue;
            }

            if (!asteroid.Modified && (asteroid.Visited || HasComp<ShuttleComponent>(uid) ||
                TryComp<ClaimableGridComponent>(uid, out var claim) && claim.Claimed ||
                !IsPristine(uid, asteroid, xform, grid)))
                asteroid.Modified = true;

            if (asteroid.Modified)
            {
                asteroid.AbandonedSince ??= _timing.CurTime;
                if (_timing.CurTime - asteroid.AbandonedSince.Value >= DepletedEvictionDelay)
                    _retire.Add(uid);
                continue;
            }
            if (IsNear(position, positions, RetentionDistance))
            {
                asteroid.FarSince = null;
                continue;
            }
            asteroid.FarSince ??= _timing.CurTime;
            if (_timing.CurTime - asteroid.FarSince.Value < EvictionDelay)
                continue;
            asteroid.Evicting = true;
            _retire.Add(uid);
        }
        foreach (var uid in _retire)
        {
            RemoveLooseAsteroidItems(uid);
            QueueDel(uid);
        }
    }

    /// <summary>Amortized activation, also usable by scripted controlled transfers before the next proximity check.</summary>
    public int MaterializeNearby(EntityUid mapUid, Vector2 observer, int budget = 2)
    {
        if (budget <= 0 || !TryComp<KoronusAsteroidFieldComponent>(mapUid, out var field) ||
            !TryComp<KoronusSystemMapComponent>(mapUid, out var runtime) ||
            !TryComp<MapComponent>(mapUid, out var map) ||
            !_sector.TryGetSystemPrototype(runtime.SystemId, out var system))
            return 0;

        var loaded = 0;
        _observers.TryGetValue(map.MapId, out var observers);
        while (loaded < budget)
        {
            var nearest = LoadingDistance * LoadingDistance;
            var index = -1;
            for (var i = 0; i < field.Sites.Count; i++)
            {
                var candidate = field.Sites[i];
                var distance = Vector2.DistanceSquared(observer, candidate.Position);
                if (candidate.Materialized || candidate.Depleted || distance > nearest ||
                    IsNear(candidate.Position, observers, VisitDistance) ||
                    !HasGridClearance(map.MapId, candidate.Position) || !HasCelestialClearance(system, candidate.Position))
                    continue;
                nearest = distance;
                index = i;
            }
            if (index < 0)
                break;
            var site = field.Sites[index];

            var uid = Spawn(site.Prototype, new EntityCoordinates(mapUid, site.Position));
            if (!TryConfigureAsteroid(uid))
            {
                QueueDel(uid);
                break;
            }
            site.Materialized = true;
            var asteroid = AddComp<KoronusMiningAsteroidComponent>(uid);
            asteroid.Site = index;
            // Populate once through the native floor-plan system, without allocating worldgen chunks.
            RemComp<LocalityLoaderComponent>(uid);
            RaiseLocalEvent(uid, new LocalStructureLoadedEvent());
            // These are one-shot builders. Keeping them in a snapshot would rebuild mined floors on restore.
            RemComp<BlobFloorPlanBuilderComponent>(uid);
            RemComp<SimpleFloorPlanPopulatorComponent>(uid);
            RemComp<RandomEntityPopulatorComponent>(uid);
            asteroid.OriginalChildren = new HashSet<EntityUid>();
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                // Native table spawners have already created their rocks and delete their marker next tick.
                if (!HasComp<EntityTableSpawnerComponent>(child))
                    asteroid.OriginalChildren.Add(child);
            }
            asteroid.OriginalTiles = new Dictionary<Vector2i, KoronusAsteroidTile>();
            var tiles = _maps.GetAllTilesEnumerator(uid, Comp<MapGridComponent>(uid));
            while (tiles.MoveNext(out var tile))
            {
                var original = tile.Value.Tile;
                asteroid.OriginalTiles.Add(tile.Value.GridIndices, new KoronusAsteroidTile
                {
                    Type = _tiles[original.TypeId].ID,
                    Flags = original.Flags,
                    Variant = original.Variant,
                    RotationMirroring = original.RotationMirroring,
                });
            }
            loaded++;
        }
        return loaded;
    }

    private void ReplenishSites(EntityUid mapUid, KoronusAsteroidFieldComponent field, string systemId,
        List<Vector2>? observers)
    {
        if (!_sector.TryGetSystemPrototype(systemId, out var system))
            return;
        var mapId = Comp<MapComponent>(mapUid).MapId;
        // Reuse depleted slots; the finite sector plan is never enlarged.
        var budget = ReplenishmentBudget;
        foreach (var site in field.Sites)
        {
            if (budget <= 0)
                break;
            if (!site.Depleted || site.AvailableOnCycle > field.ReplenishmentCycle ||
                !TryFindSafePosition(system, mapId, field, site.Cluster, out var position) ||
                IsNear(position, observers, RetentionDistance))
                continue;
            site.Position = position;
            site.Prototype = PickAsteroidPrototype();
            site.Depleted = false;
            budget--;
        }
    }

    private void OnAsteroidTerminating(EntityUid uid, KoronusMiningAsteroidComponent comp, EntityTerminatingEvent args)
    {
        var map = Transform(uid).MapUid;
        if (map is not { } mapUid || TerminatingOrDeleted(mapUid) ||
            !TryComp<KoronusAsteroidFieldComponent>(mapUid, out var field) ||
            comp.Site < 0 || comp.Site >= field.Sites.Count)
            return;
        var site = field.Sites[comp.Site];
        site.Materialized = false;
        site.Depleted = !comp.Evicting;
        site.AvailableOnCycle = field.ReplenishmentCycle + (comp.Evicting ? 0 : 1);
    }

    private bool IsPristine(EntityUid uid, KoronusMiningAsteroidComponent asteroid,
        TransformComponent xform, MapGridComponent grid)
    {
        if (asteroid.OriginalChildren == null || xform.ChildCount != asteroid.OriginalChildren.Count ||
            asteroid.OriginalTiles == null || !HasOriginalTiles(uid, grid, asteroid.OriginalTiles))
            return false;

        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!asteroid.OriginalChildren.Contains(child) ||
                TryComp<DamageableComponent>(child, out var damage) && damage.TotalDamage > 0)
                return false;
        }
        return true;
    }

    private void RemoveLooseAsteroidItems(EntityUid uid)
    {
        var asteroid = Comp<KoronusMiningAsteroidComponent>(uid);
        if (asteroid.Evicting || asteroid.OriginalTiles == null)
            return;
        var xform = Transform(uid);
        var inverse = _transform.GetInvWorldMatrix(xform);
        var position = _transform.GetWorldPosition(xform);
        var tileSize = Comp<MapGridComponent>(uid).TileSize;
        foreach (var item in _lookup.GetEntitiesInRange<ItemComponent>(new MapCoordinates(position, xform.MapID), AsteroidRadius))
        {
            var itemTransform = Transform(item.Owner);
            if (itemTransform.ParentUid != xform.MapUid)
                continue;
            var local = Vector2.Transform(_transform.GetWorldPosition(itemTransform), inverse) / tileSize;
            var indices = new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));
            if (asteroid.OriginalTiles.ContainsKey(indices))
                QueueDel(item.Owner);
        }
    }

    private bool HasOriginalTiles(EntityUid uid, MapGridComponent grid, Dictionary<Vector2i, KoronusAsteroidTile> original)
    {
        var count = 0;
        var tiles = _maps.GetAllTilesEnumerator(uid, grid);
        while (tiles.MoveNext(out var tile))
        {
            var current = tile.Value.Tile;
            if (!original.TryGetValue(tile.Value.GridIndices, out var previous) ||
                previous.Type != _tiles[current.TypeId].ID || previous.Flags != current.Flags ||
                previous.Variant != current.Variant || previous.RotationMirroring != current.RotationMirroring)
                return false;
            count++;
        }
        return count == original.Count;
    }

    private static bool IsNear(Vector2 position, List<Vector2>? observers, float distance)
    {
        if (observers == null)
            return false;
        foreach (var observer in observers)
            if (Vector2.DistanceSquared(position, observer) <= distance * distance)
                return true;
        return false;
    }
}
