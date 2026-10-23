using System.Diagnostics;
using System.Linq;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

public sealed partial class BiomeSystem
{
    private void RecordSurfaceMarker(EntityUid grid, EntityUid uid, Vector2i tile, string layer)
    {
        if (TryComp<KoronusPlanetSurfaceMapComponent>(grid, out var surface))
            surface.GeneratedMarkers[uid] = (tile, layer);
    }

    public sealed class SurfaceRefresh
    {
        public readonly Entity<BiomeComponent, MapGridComponent> Terrain;
        public readonly KoronusPlanetSurfaceMapComponent Surface;
        public readonly HashSet<Vector2i> ProtectedTiles = new();
        public readonly HashSet<EntityUid> GeneratedEntities = new();
        public readonly Dictionary<Vector2i, List<(EntityUid Uid, Vector2i Tile)>> EntitiesByChunk = new();
        public readonly HashSet<string> PreservedMarkerLayers = new();
        public readonly Queue<Vector2i> Chunks;
        public readonly bool WasEnabled;

        public SurfaceRefresh(Entity<BiomeComponent, MapGridComponent> terrain,
            KoronusPlanetSurfaceMapComponent surface, HashSet<Vector2i> chunks)
        {
            Terrain = terrain;
            Surface = surface;
            Chunks = new Queue<Vector2i>(chunks);
            WasEnabled = terrain.Comp1.Enabled;
        }
    }

    public SurfaceRefresh BeginSurfaceRefresh(Entity<BiomeComponent, MapGridComponent> terrain,
        KoronusPlanetSurfaceMapComponent surface)
    {
        var chunks = new HashSet<Vector2i>(terrain.Comp1.LoadedChunks);
        chunks.UnionWith(terrain.Comp1.ModifiedTiles.Keys);
        chunks.UnionWith(terrain.Comp1.LoadedEntities.Keys);
        chunks.UnionWith(terrain.Comp1.LoadedDecals.Keys);
        var tiles = _mapSystem.GetAllTilesEnumerator(terrain, terrain.Comp2);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is { } turf)
                chunks.Add(SharedMapSystem.GetChunkIndices(turf.GridIndices, ChunkSize) * ChunkSize);
        }

        var refresh = new SurfaceRefresh(terrain, surface, chunks);
        foreach (var entities in terrain.Comp1.LoadedEntities.Values)
        {
            foreach (var (uid, tile) in entities)
                Track(uid, tile);
        }
        foreach (var (uid, marker) in surface.GeneratedMarkers)
        {
            Track(uid, marker.Tile);
            // Do not multiply living fauna on each resource refresh.
            if (!Deleted(uid) && HasComp<MobStateComponent>(uid))
                refresh.PreservedMarkerLayers.Add(marker.Layer);
        }
        foreach (var chunk in refresh.EntitiesByChunk.Keys)
        {
            if (chunks.Add(chunk))
                refresh.Chunks.Enqueue(chunk);
        }
        SetEnabled((terrain.Owner, terrain.Comp1), false);
        return refresh;

        void Track(EntityUid uid, Vector2i tile)
        {
            if (!refresh.GeneratedEntities.Add(uid))
                return;
            var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
            refresh.EntitiesByChunk.GetOrNew(chunk).Add((uid, tile));
        }
    }

    public void ProtectSurfaceArea(SurfaceRefresh refresh, Box2 bounds)
    {
        for (var x = (int) MathF.Floor(bounds.Left); x < MathF.Ceiling(bounds.Right); x++)
        {
            for (var y = (int) MathF.Floor(bounds.Bottom); y < MathF.Ceiling(bounds.Top); y++)
                refresh.ProtectedTiles.Add(new Vector2i(x, y));
        }
    }

    /// <summary>Preserves player objects, containers and mobs, including arrivals during the reset.</summary>
    public void ProtectSurfaceOccupants(SurfaceRefresh refresh)
    {
        var terrain = refresh.Terrain.Owner;
        var children = Transform(terrain).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (!TryComp(uid, out TransformComponent? transform) || HasComp<MapGridComponent>(uid))
                continue;
            if (refresh.GeneratedEntities.Contains(uid) && !HasComp<MobStateComponent>(uid) && !HasContents(uid))
                continue;

            var position = transform.LocalPosition;
            var bounds = Box2.CenteredAround(position, new System.Numerics.Vector2(2f));
            if (HasComp<PhysicsComponent>(uid))
                bounds = bounds.Union(_physics.GetWorldAABB(uid));
            ProtectSurfaceArea(refresh, bounds);
        }
    }

    public bool ProcessSurfaceRefresh(SurfaceRefresh refresh, int chunkBudget = 8)
    {
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < chunkBudget && refresh.Chunks.TryDequeue(out var chunk); i++)
        {
            ResetSurfaceChunk(refresh, chunk);
            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(4))
                break;
        }
        if (refresh.Chunks.Count > 0)
            return false;

        var biome = refresh.Terrain.Comp1;
        foreach (var layer in biome.LoadedMarkers.Keys.ToArray())
        {
            if (!refresh.PreservedMarkerLayers.Contains(layer))
                biome.LoadedMarkers.Remove(layer);
        }
        foreach (var layers in biome.PendingMarkers.Values)
        {
            foreach (var layer in layers.Keys.ToArray())
            {
                if (!refresh.PreservedMarkerLayers.Contains(layer))
                    layers.Remove(layer);
            }
        }
        biome.ForcedMarkerLayers.Clear();
        _markerChunks.Remove(biome);
        foreach (var tile in refresh.ProtectedTiles)
        {
            var chunk = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
            biome.ModifiedTiles.GetOrNew(chunk).Add(tile);
        }
        foreach (var uid in refresh.Surface.GeneratedMarkers.Keys.ToArray())
        {
            if (Deleted(uid) || Transform(uid).MapUid != Transform(refresh.Terrain).MapUid)
                refresh.Surface.GeneratedMarkers.Remove(uid);
        }
        CancelSurfaceRefresh(refresh);
        return true;
    }

    public void CancelSurfaceRefresh(SurfaceRefresh refresh)
    {
        if (!TerminatingOrDeleted(refresh.Terrain))
            SetEnabled((refresh.Terrain.Owner, refresh.Terrain.Comp1), refresh.WasEnabled);
    }

    private bool HasContents(EntityUid uid)
    {
        return TryComp<ContainerManagerComponent>(uid, out var containers) &&
               containers.Containers.Values.Any(container => container.ContainedEntities.Count > 0);
    }

    private void ResetSurfaceChunk(SurfaceRefresh refresh, Vector2i chunk)
    {
        var (uid, biome, grid) = refresh.Terrain;
        if (refresh.EntitiesByChunk.TryGetValue(chunk, out var generated))
        {
            foreach (var (entity, originalTile) in generated)
            {
                if (TerminatingOrDeleted(entity) || !TryComp(entity, out TransformComponent? transform) ||
                    transform.ParentUid != uid)
                    continue;
                var currentTile = _mapSystem.LocalToTile(uid, grid, transform.Coordinates);
                if (currentTile == originalTile && !refresh.ProtectedTiles.Contains(currentTile) &&
                    !HasComp<MobStateComponent>(entity) && !HasContents(entity))
                    Del(entity);
                else
                    refresh.ProtectedTiles.Add(currentTile);
            }
        }
        if (biome.LoadedDecals.Remove(chunk, out var decals))
        {
            foreach (var decal in decals.Keys)
                _decals.RemoveDecal(uid, decal);
        }

        var modified = new HashSet<Vector2i>();
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = chunk.X; x < chunk.X + ChunkSize; x++)
        {
            for (var y = chunk.Y; y < chunk.Y + ChunkSize; y++)
            {
                var index = new Vector2i(x, y);
                if (refresh.ProtectedTiles.Contains(index) || HasAnchoredEntity(uid, grid, index))
                {
                    modified.Add(index);
                    continue;
                }
                if (!_mapSystem.TryGetTileRef(uid, grid, index, out var existing) || existing.Tile.IsEmpty)
                    continue;
                // Player-built floors and foundations are not procedural terrain.
                if (!TryGetTile(index, biome.Layers, biome.Seed, null, out var natural) || existing.Tile != natural.Value)
                    modified.Add(index);
                else
                    tiles.Add((index, Tile.Empty));
            }
        }
        if (tiles.Count > 0)
            _mapSystem.SetTiles(uid, grid, tiles);
        biome.ModifiedTiles.Remove(chunk);
        if (modified.Count > 0)
            biome.ModifiedTiles[chunk] = modified;
        biome.LoadedEntities.Remove(chunk);
        biome.LoadedChunks.Remove(chunk);
    }
}
