using System.Numerics;
using Content.Server._WH40K.Weather;
using Content.Shared._WH40K.GroundCover;
using Content.Shared._WH40K.Weather;
using Content.Shared.Atmos;
using Content.Shared.Light.Components;
using Content.Shared.Particles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WH40K.GroundCover;

public sealed partial class Wh40kGroundCoverSystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;

    /// <summary>Chunks updated a tick at most.</summary>
    public const int ChunksPerTick = 8;

    /// <summary>Fewest depth units laid on a chunk at once, so slow snowfall isn't rounded away.</summary>
    private const float MinFallUnits = 3f;

    /// <summary>Fewest seconds melted on a chunk at once.</summary>
    private const double MinMeltSeconds = 10;

    /// <summary>Snow doesn't settle on tiles whose air is this much above freezing.</summary>
    private const float SettleLimit = 2f;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    private readonly Queue<(EntityUid Grid, Vector2i Chunk)> _queue = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Chunk)> _queued = new();
    private readonly List<(EntityUid Grid, Vector2i Chunk)> _view = new();
    private TimeSpan _nextScan;
    private uint _snowRevision;

    private void UpdateSnow(TimeSpan now)
    {
        if (now >= _nextScan)
        {
            _nextScan = now + ScanInterval;
            foreach (var session in _player.Sessions)
            {
                _view.Clear();
                CollectViewCached(session, _view);
                foreach (var key in _view)
                {
                    if (_queued.Add(key))
                        _queue.Enqueue(key);
                }
            }
        }

        for (var i = 0; i < ChunksPerTick && _queue.TryDequeue(out var key); i++)
        {
            _queued.Remove(key);
            UpdateChunk(key.Grid, key.Chunk);
        }
    }

    /// <summary>Snow stays on exposed surface grids. Landed shuttles shelter the ground beneath them.</summary>
    private bool CanHoldCover(EntityUid grid, EntityUid map)
    {
        return _wolfWeather.IsSurface(map) && !_wolfWeather.IsCoverGrid(grid);
    }

    /// <summary>Lays, melts or starts the snow of one chunk from its map's totals. False if the chunk didn't change.</summary>
    public bool UpdateChunk(EntityUid gridUid, Vector2i index)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid)
            || !TryComp(gridUid, out TransformComponent? xform)
            || !TryComp<Wh40kGroundWeatherComponent>(xform.MapUid, out var ground)
            || !CanHoldCover(gridUid, xform.MapUid.Value))
            return false;

        TryComp<Wh40kSnowCoverComponent>(gridUid, out var cover);
        Wh40kSnowChunk? chunk = null;
        cover?.Chunks.TryGetValue(index, out chunk);

        var roofed = new Entity<MapGridComponent, RoofComponent?>(gridUid, grid, CompOrNull<RoofComponent>(gridUid));
        var map = xform.MapUid.Value;
        bool changed;

        if (chunk == null)
        {
            if (ground.BaseDepth <= 0f && ground.Snow <= 0f)
                return false;

            chunk = new Wh40kSnowChunk { FallenAt = ground.Fallen, MeltAt = ground.MeltClock };
            if (!Apply(roofed, map, index, chunk, ground.BaseDepth * Wh40kSnowCover.UnitsPerLevel, 0f))
                return false;

            cover ??= EnsureComp<Wh40kSnowCoverComponent>(gridUid);
            cover.Chunks[index] = chunk;
            chunk.Version = ++_snowRevision;
            return true;
        }

        var fall = (float) (ground.Fallen - chunk.FallenAt) * Wh40kSnowCover.UnitsPerLevel;
        var melt = ground.MeltClock - chunk.MeltAt;
        var doFall = fall >= MinFallUnits;
        var doMelt = melt >= MinMeltSeconds;
        if (!doFall && !doMelt)
            return false;

        if (doFall)
            chunk.FallenAt = ground.Fallen;

        if (doMelt)
            chunk.MeltAt = ground.MeltClock;

        changed = Apply(roofed, map, index, chunk, doFall ? fall : 0f, doMelt ? (float) melt : 0f);
        if (changed)
            chunk.Version = ++_snowRevision;

        if (chunk.IsEmpty() && ground.BaseDepth <= 0f && ground.Snow <= 0f)
        {
            cover!.Chunks.Remove(index);
            if (cover.Chunks.Count == 0)
                RemComp<Wh40kSnowCoverComponent>(gridUid);
            return true;
        }

        return changed;
    }

    /// <summary>Lays some depth units on the chunk's open tiles and melts some seconds off its warm ones.</summary>
    private bool Apply(Entity<MapGridComponent, RoofComponent?> grid, EntityUid map, Vector2i index, Wh40kSnowChunk chunk, float fall, float meltSeconds)
    {
        var changed = false;
        var planet = _wolfWeather.IsOnPlanetSurface(grid.Owner);
        for (var i = 0; i < Wh40kSnowCover.ChunkArea; i++)
        {
            var tile = Wh40kSnowCover.TileAt(index, i);
            var depth = (int) chunk.Depths[i];
            var tileRef = _map.GetTileRef(grid.Owner, grid.Comp1, tile);

            if (tileRef.Tile.IsEmpty)
            {
                depth = 0;
            }
            else if (fall > 0f || meltSeconds > 0f && depth > 0)
            {
                var open = _wolfWeather.IsExposed(grid, tileRef);
                var temperature = TileTemperature(grid.Owner, map, tile, open && planet);
                if (fall > 0f && open && temperature <= Freezing + SettleLimit
                    && !IsOpenWater(grid, tile))
                    depth = Math.Min(Wh40kSnowCover.MaxDepth, depth + (int) MathF.Round(fall * Wh40kSnowCover.Drift(tile)));

                if (meltSeconds > 0f && depth > 0 && temperature > Freezing)
                    depth = Math.Max(0, depth - (int) MathF.Ceiling(MeltRate(temperature) * meltSeconds / 60f * Wh40kSnowCover.UnitsPerLevel));
            }

            if (depth == chunk.Depths[i])
                continue;

            chunk.Depths[i] = (byte) depth;
            changed = true;
        }

        return changed;
    }

    private bool IsOpenWater(Entity<MapGridComponent, RoofComponent?> grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntities(grid.Owner, grid.Comp1, tile);
        while (anchored.MoveNext(out var entity))
        {
            if (HasComp<WaterEntryParticleComponent>(entity.Value))
                return true;
        }

        return false;
    }

    /// <summary>The air temperature over a tile; a tile with no air of its own takes the map's.</summary>
    public float TileTemperature(EntityUid grid, EntityUid map, Vector2i tile, bool outdoors = false)
    {
        return AirOver(grid, map, tile, outdoors)?.Temperature ?? 0f;
    }

    /// <summary>
    /// The air over a tile. Open sky on a planet takes the planet's air, as built ground such as a city street keeps
    /// its own air from when it was laid while the climate only moves the map's.
    /// </summary>
    private GasMixture? AirOver(EntityUid grid, EntityUid map, Vector2i tile, bool outdoors)
    {
        if (outdoors && _atmos.GetTileMixture(null, map, tile) is { TotalMoles: >= 1f } outside)
            return outside;

        var air = _atmos.GetTileMixture((grid, null, null), map, tile);
        if (air == null || air.TotalMoles < 1f)
            air = _atmos.GetTileMixture(null, map, tile);

        return air;
    }

    /// <summary>Snow depth on a tile, in units of which <see cref="Wh40kSnowCover.UnitsPerLevel"/> make a level.</summary>
    public int GetDepth(EntityUid grid, Vector2i tile)
    {
        if (!TryComp<Wh40kSnowCoverComponent>(grid, out var cover)
            || !cover.Chunks.TryGetValue(Wh40kSnowCover.ChunkOf(tile), out var chunk))
            return 0;

        return chunk.Depths[Wh40kSnowCover.IndexIn(tile)];
    }

    /// <summary>Sets the snow depth on a tile, as shoveling and admins do.</summary>
    public void SetDepth(EntityUid grid, Vector2i tile, int depth)
    {
        depth = Math.Clamp(depth, 0, Wh40kSnowCover.MaxDepth);
        if (depth > 0 && (Transform(grid).MapUid is not { } map || !CanHoldCover(grid, map)))
            return;

        var index = Wh40kSnowCover.ChunkOf(tile);
        if (!TryComp<Wh40kSnowCoverComponent>(grid, out var cover))
        {
            if (depth == 0)
                return;

            cover = EnsureComp<Wh40kSnowCoverComponent>(grid);
        }

        if (!cover.Chunks.TryGetValue(index, out var chunk))
        {
            if (depth == 0)
                return;

            chunk = new Wh40kSnowChunk();
            if (TryComp(grid, out TransformComponent? xform) && TryComp<Wh40kGroundWeatherComponent>(xform.MapUid, out var ground))
            {
                chunk.FallenAt = ground.Fallen;
                chunk.MeltAt = ground.MeltClock;
            }

            cover.Chunks[index] = chunk;
        }

        var i = Wh40kSnowCover.IndexIn(tile);
        if (chunk.Depths[i] == depth)
            return;

        chunk.Depths[i] = (byte) depth;
        chunk.Version = ++_snowRevision;
    }

    /// <summary>Whether weather reaches a tile, so it gets wet and snowed on.</summary>
    public bool IsOpen(EntityUid grid, Vector2i tile)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp)
            || !_map.TryGetTileRef(grid, gridComp, tile, out var tileRef)
            || tileRef.Tile.IsEmpty)
            return false;

        var roofed = new Entity<MapGridComponent, RoofComponent?>(grid, gridComp, CompOrNull<RoofComponent>(grid));
        return _wolfWeather.IsExposed(roofed, tileRef);
    }

    /// <summary>Strips snow a grid shouldn't hold, listing its chunks so the clients that were sent them are told it's gone.</summary>
    private void PurgeCover(EntityUid grid, List<(EntityUid Grid, Vector2i Chunk)> into)
    {
        if (!TryComp<Wh40kSnowCoverComponent>(grid, out var cover))
            return;

        foreach (var index in cover.Chunks.Keys)
        {
            into.Add((grid, index));
        }

        RemComp<Wh40kSnowCoverComponent>(grid);
    }

    /// <summary>How long a session's collected view is reused by the snow, decal and send scans.</summary>
    private static readonly TimeSpan ViewCacheTime = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<ICommonSession, (TimeSpan At, List<(EntityUid Grid, Vector2i Chunk)> Keys)> _viewCache = new();

    private void CollectViewCached(ICommonSession session, List<(EntityUid Grid, Vector2i Chunk)> into)
    {
        var now = _timing.CurTime;
        var found = _viewCache.TryGetValue(session, out var cached);
        if (!found)
            cached = (TimeSpan.Zero, new List<(EntityUid Grid, Vector2i Chunk)>());

        if (!found || now - cached.At >= ViewCacheTime || cached.At > now)
        {
            cached.Keys.Clear();
            CollectView(session, cached.Keys);
            cached.At = now;
            _viewCache[session] = cached;
        }

        into.AddRange(cached.Keys);
    }

    private void CollectView(ICommonSession session, List<(EntityUid Grid, Vector2i Chunk)> into)
    {
        if (!TryComp(session.AttachedEntity, out TransformComponent? xform) || xform.MapUid is not { } map
            || !HasComp<Wh40kGroundWeatherComponent>(map) && !HasComp<Wh40kSnowCoverComponent>(xform.GridUid))
            return;

        var position = _transform.GetWorldPosition(xform);
        var box = Box2.CenteredAround(position, new Vector2(ViewRange * 2f));
        _grids.Clear();
        _map.FindGridsIntersecting(xform.MapID, box, ref _grids);
        foreach (var grid in _grids)
        {
            if (!CanHoldCover(grid, map))
            {
                PurgeCover(grid.Owner, into);
                continue;
            }

            var local = _transform.GetInvWorldMatrix(grid).TransformBox(box);
            var min = Wh40kSnowCover.ChunkOf(new Vector2i((int) MathF.Floor(local.Left), (int) MathF.Floor(local.Bottom)));
            var max = Wh40kSnowCover.ChunkOf(new Vector2i((int) MathF.Floor(local.Right), (int) MathF.Floor(local.Top)));
            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                    into.Add((grid.Owner, new Vector2i(x, y)));
            }
        }
    }
}
