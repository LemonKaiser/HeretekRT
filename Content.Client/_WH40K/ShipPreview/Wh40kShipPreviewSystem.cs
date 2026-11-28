using System.IO;
using System.Linq;
using System.Numerics;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Client._WH40K.ShipPreview;

public readonly record struct Wh40kShipPreviewGrid(
    Entity<MapGridComponent> Grid,
    Box2 LocalBounds,
    Vector2i SizeInTiles,
    int TileCount);

public sealed class Wh40kShipPreviewHandle
{
    internal MapId MapId = MapId.Nullspace;
    internal EntityUid MapUid = EntityUid.Invalid;
    internal ResPath? LoadedPath;
    internal Wh40kShipPreviewGrid? Loaded;
    internal LoadResult? LoadedEntities;

    public MapId PreviewMap => MapId;
}

/// <summary>
/// Loads ship grids onto private paused client-side preview maps so UI can render them cleanly
/// without touching the server or disrupting the player's eye.
/// </summary>
public sealed partial class Wh40kShipPreviewSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private readonly List<Wh40kShipPreviewHandle> _handles = new();

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var handle in _handles)
            DestroyMap(handle);

        _handles.Clear();
    }

    public Wh40kShipPreviewHandle Acquire()
    {
        var handle = new Wh40kShipPreviewHandle();
        _handles.Add(handle);
        return handle;
    }

    public void Release(Wh40kShipPreviewHandle handle)
    {
        DestroyMap(handle);
        _handles.Remove(handle);
    }

    public Wh40kShipPreviewGrid? GetCurrent(Wh40kShipPreviewHandle handle)
    {
        return handle.Loaded is { } loaded && Exists(loaded.Grid.Owner) ? loaded : null;
    }

    public bool TryLoad(Wh40kShipPreviewHandle handle, VesselPrototype vessel, out Wh40kShipPreviewGrid preview)
    {
        return TryLoad(handle, vessel.ShuttlePath, vessel.Name, out preview);
    }

    public bool TryLoad(Wh40kShipPreviewHandle handle, ResPath path, string? name, out Wh40kShipPreviewGrid preview)
    {
        return TryLoad(handle, path, name, out preview, () => _loader.TryReadFile(path, out var data) ? data : null);
    }

    public bool TryLoadText(Wh40kShipPreviewHandle handle, ResPath key, string yaml, string? name, out Wh40kShipPreviewGrid preview)
    {
        return TryLoad(handle, key, name, out preview, () =>
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode.ToDataNode() as MappingDataNode : null;
        });
    }

    private bool TryLoad(
        Wh40kShipPreviewHandle handle,
        ResPath path,
        string? name,
        out Wh40kShipPreviewGrid preview,
        Func<MappingDataNode?> read)
    {
        preview = default;

        if (GetCurrent(handle) is { } current && handle.LoadedPath == path)
        {
            preview = current;
            return true;
        }

        if (!EnsureMap(handle))
            return false;

        Clear(handle);

        Entity<MapGridComponent>? grid;
        try
        {
            if (read() is not { } data)
            {
                Log.Warning($"Ship preview couldn't read {path}");
                return false;
            }

            if (!TryLoadGrid(handle, data, out grid))
            {
                Log.Warning($"Ship preview failed to load grid {path}");
                return false;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Ship preview threw while loading grid {path}: {e}");
            DestroyMap(handle);
            return false;
        }

        _xform.SetLocalPositionRotation(grid.Value.Owner, Vector2.Zero, Angle.Zero);

        if (!string.IsNullOrEmpty(name))
            _meta.SetEntityName(grid.Value.Owner, name);

        var bounds = grid.Value.Comp.LocalAABB;
        var tileSize = MathF.Max(grid.Value.Comp.TileSize, 1f);
        var size = new Vector2i(
            (int) MathF.Round(bounds.Width / tileSize),
            (int) MathF.Round(bounds.Height / tileSize));

        var tiles = 0;
        var enumerator = _map.GetAllTiles(grid.Value.Owner, grid.Value.Comp);
        while (enumerator.MoveNext(out _))
            tiles++;

        preview = new Wh40kShipPreviewGrid(grid.Value, bounds, size, tiles);
        handle.Loaded = preview;
        handle.LoadedPath = path;
        return true;
    }

    private void RemoveUnknownComponents(MappingDataNode data)
    {
        if (!data.TryGet("entities", out SequenceDataNode? prototypes))
            return;

        foreach (var prototype in prototypes.OfType<MappingDataNode>())
        {
            if (!prototype.TryGet("entities", out SequenceDataNode? entities))
                continue;

            foreach (var entity in entities.OfType<MappingDataNode>())
            {
                if (!entity.TryGet("components", out SequenceDataNode? components))
                    continue;

                for (var i = components.Count - 1; i >= 0; i--)
                {
                    if (components[i] is MappingDataNode component &&
                        component.TryGet("type", out ValueDataNode? type) &&
                        !_factory.TryGetRegistration(type.Value, out _))
                    {
                        components.RemoveAt(i);
                    }
                }
            }
        }
    }

    public void Clear(Wh40kShipPreviewHandle handle)
    {
        if (handle.LoadedEntities is { } entities)
            _loader.Delete(entities);

        handle.LoadedEntities = null;
        handle.Loaded = null;
        handle.LoadedPath = null;
    }

    private bool EnsureMap(Wh40kShipPreviewHandle handle)
    {
        if (MapAlive(handle))
            return true;

        Clear(handle);
        handle.MapId = MapId.Nullspace;
        handle.MapUid = EntityUid.Invalid;

        try
        {
            handle.MapUid = _map.CreateMap(out var mapId, runMapInit: false);
            handle.MapId = mapId;
        }
        catch (Exception e)
        {
            Log.Error($"Ship preview failed to create its map: {e}");
            handle.MapUid = EntityUid.Invalid;
            handle.MapId = MapId.Nullspace;
            return false;
        }

        _meta.SetEntityName(handle.MapUid, "WH40K ship preview");
        return true;
    }

    private bool MapAlive(Wh40kShipPreviewHandle handle)
    {
        return handle.MapId != MapId.Nullspace
               && Exists(handle.MapUid)
               && _map.TryGetMap(handle.MapId, out var existing)
               && existing == handle.MapUid;
    }

    private void DestroyMap(Wh40kShipPreviewHandle handle)
    {
        Clear(handle);

        if (MapAlive(handle))
            Del(handle.MapUid);

        handle.MapId = MapId.Nullspace;
        handle.MapUid = EntityUid.Invalid;
    }
}
