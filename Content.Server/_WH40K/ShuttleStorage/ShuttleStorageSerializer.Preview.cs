using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared._WH40K.ShuttleStorage;
using Content.Shared.Tag;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSerializer
{
    [Dependency] private TagSystem _previewTags = default!;
    private Dictionary<string, ShuttleStorageCell>? _previewTypes;

    // Snapshot this on the simulation thread. The worker only receives immutable prototype classifications.
    public IReadOnlyDictionary<string, ShuttleStorageCell> PreviewTypes()
    {
        if (_previewTypes != null)
            return _previewTypes;
        _previewTypes = new();
        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            var type = prototype.Components.ContainsKey("Door") ? ShuttleStorageCell.Door :
                prototype.Components.ContainsKey("Window") ? ShuttleStorageCell.Window :
                prototype.TryGetComponent<TagComponent>(out var tags, _factory) && _previewTags.HasTag(tags, "Wall")
                    ? ShuttleStorageCell.Wall : ShuttleStorageCell.Empty;
            if (type != ShuttleStorageCell.Empty)
                _previewTypes[prototype.ID] = type;
        }
        return _previewTypes;
    }

    public ShuttleStoragePreview? PreviewGrid(EntityUid grid, ShuttleStorageInspection info)
    {
        if (!TryComp(grid, out MapGridComponent? map) || !ShuttleStorageLimits.FitsDimensions(info.Width, info.Height))
            return null;
        var tiles = new Dictionary<(int X, int Y), byte>();
        var tileTypes = IoCManager.Resolve<Robust.Shared.Map.ITileDefinitionManager>();
        foreach (var tile in _previewMaps.GetAllTiles(grid, map))
        {
            if (tiles.Count >= ShuttleStorageLimits.PreviewCells)
                return null;
            tiles[(tile.X, tile.Y)] = FloorType(tileTypes[tile.Tile.TypeId].ID);
        }
        var features = new List<(int X, int Y, byte Type)>();
        var types = PreviewTypes();
        foreach (var entity in info.Entities)
        {
            var transform = Transform(entity);
            if (transform.ParentUid != grid || !transform.Anchored ||
                MetaData(entity).EntityPrototype is not { } prototype || !types.TryGetValue(prototype.ID, out var type))
                continue;
            var pos = transform.LocalPosition;
            features.Add(((int) MathF.Floor(pos.X), (int) MathF.Floor(pos.Y), (byte) type));
        }
        return MakePreview(tiles, features);
    }

    [Dependency] private Robust.Server.GameObjects.MapSystem _previewMaps = default!;

    public static ShuttleStoragePreview? PreviewPayload(byte[] payload, IReadOnlyDictionary<string, ShuttleStorageCell> types)
    {
        if (payload.Length is 0 or > ShuttleStorageLimits.Bytes)
            return null;
        try
        {
            var root = ParseNode(payload);
            var gridId = ((ValueDataNode) ((SequenceDataNode) root["grids"])[0]).AsInt();
            var tileMap = (MappingDataNode) root["tilemap"];
            var tiles = new Dictionary<(int X, int Y), byte>();
            var features = new List<(int X, int Y, byte Type)>();
            foreach (MappingDataNode group in (SequenceDataNode) root["entities"])
            foreach (MappingDataNode entity in (SequenceDataNode) group["entities"])
            {
                if (!entity.TryGet("components", out SequenceDataNode? components))
                    continue;
                var id = ((ValueDataNode) entity["uid"]).AsInt();
                if (id == gridId)
                {
                    var map = components.OfType<MappingDataNode>().First(node => ((ValueDataNode) node["type"]).Value == "MapGrid");
                    var chunks = (MappingDataNode) map["chunks"];
                    if (chunks.Count > 64)
                        return null;
                    foreach (var (_, value) in chunks)
                    {
                        var chunk = (MappingDataNode) value;
                        var size = chunk.TryGet("size", out ValueDataNode? sizeNode) ? sizeNode.AsInt() : 16;
                        if (size is < 1 or > 64 || ((ValueDataNode) chunk["version"]).AsInt() != 7)
                            return null;
                        var index = ParsePosition(((ValueDataNode) chunk["ind"]).Value);
                        var encoded = ((ValueDataNode) chunk["tiles"]).Value;
                        if (encoded.Length > (size * size * 7 + 2) / 3 * 4)
                            return null;
                        var bytes = Convert.FromBase64String(encoded);
                        if (bytes.Length != size * size * 7)
                            return null;
                        using var reader = new BinaryReader(new MemoryStream(bytes, false));
                        for (var y = 0; y < size; y++)
                        for (var x = 0; x < size; x++)
                        {
                            var tileId = reader.ReadInt32();
                            reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
                            var tile = ((ValueDataNode) tileMap[tileId.ToString(CultureInfo.InvariantCulture)]).Value;
                            if (tile == "Space")
                                continue;
                            if (tiles.Count >= ShuttleStorageLimits.PreviewCells)
                                return null;
                            tiles[(checked((int) index.X * size + x), checked((int) index.Y * size + y))] = FloorType(tile);
                        }
                    }
                }
                else if (types.TryGetValue(((ValueDataNode) group["proto"]).Value, out var type))
                {
                    var transform = components.OfType<MappingDataNode>().FirstOrDefault(node => ((ValueDataNode) node["type"]).Value == "Transform");
                    if (transform == null || !transform.TryGet("parent", out ValueDataNode? parent) || parent.AsInt() != gridId)
                        continue;
                    var pos = transform.TryGet("pos", out ValueDataNode? position) ? ParsePosition(position.Value) : Vector2.Zero;
                    features.Add(((int) MathF.Floor(pos.X), (int) MathF.Floor(pos.Y), (byte) type));
                }
            }
            return MakePreview(tiles, features);
        }
        catch (Exception) { return null; } // Presentation failure must never block storage or alter saved bytes.
    }

    private static Vector2 ParsePosition(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 2)
            throw new InvalidDataException();
        return new(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static byte FloorType(string id) => (byte) (id.Contains("Lattice", StringComparison.OrdinalIgnoreCase) ||
        id.Contains("Grating", StringComparison.OrdinalIgnoreCase) ? ShuttleStorageCell.Grating : ShuttleStorageCell.Floor);

    private static ShuttleStoragePreview? MakePreview(Dictionary<(int X, int Y), byte> tiles,
        List<(int X, int Y, byte Type)> features)
    {
        if (tiles.Count == 0)
            return null;
        var minX = tiles.Keys.Min(pos => pos.X);
        var minY = tiles.Keys.Min(pos => pos.Y);
        var width = (long) tiles.Keys.Max(pos => pos.X) - minX + 1;
        var height = (long) tiles.Keys.Max(pos => pos.Y) - minY + 1;
        if (!ShuttleStorageLimits.FitsDimensions(width, height))
            return null;
        var cells = new byte[width * height];
        foreach (var (pos, type) in tiles)
            cells[(pos.Y - minY) * width + pos.X - minX] = type;
        foreach (var feature in features)
            if ((long) feature.X - minX >= 0 && (long) feature.X - minX < width &&
                (long) feature.Y - minY >= 0 && (long) feature.Y - minY < height)
                cells[(feature.Y - minY) * width + feature.X - minX] |= feature.Type;
        return new((int) width, (int) height, cells);
    }
}
