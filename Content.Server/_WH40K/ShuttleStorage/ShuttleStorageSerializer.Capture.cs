using System.Linq;
using Content.Shared._WH40K.ShuttleStorage;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSerializer
{
    public ShuttleStorageResult Capture(EntityUid grid, out ShuttleStorageInspection info, out MappingDataNode? snapshot)
    {
        snapshot = null;
        var result = Inspect(grid, out info);
        if (result != ShuttleStorageResult.Ready)
            return result;

        HashSet<EntityUid> roots = [grid];
        HashSet<MapId> maps = [Transform(grid).MapID];
        try
        {
            RaiseLocalEvent(new BeforeSerializationEvent(roots, maps, FileCategory.Grid));
            var external = false;
            var members = info.Entities;
            var serializer = CreateSerializer(grid, info, () => external = true);
            serializer.SerializeEntityRecursive(grid);
            if (external || serializer.YamlUidMap.Keys.Any(uid => !members.Contains(uid)) ||
                serializer.GetCategory() != FileCategory.Grid ||
                !members.SetEquals(serializer.EntityData.Values.Select(entity => entity.Uid)))
            {
                return ShuttleStorageResult.Unsupported;
            }

            ApplySnapshotAdapters(grid, serializer);
            snapshot = serializer.Write();
            return ValidateNode(snapshot) ? ShuttleStorageResult.Ready : ShuttleStorageResult.Unsupported;
        }
        catch (Exception error)
        {
            LastInspectionFailure = error.Message;
            Log.Warning($"Cannot capture shuttle {ToPrettyString(grid)}: {error.Message}");
            return ShuttleStorageResult.Unsupported;
        }
        finally
        {
            // Map save handlers may temporarily remove runtime components. Keep both events on
            // the same simulation tick, including when serialization aborts.
            RaiseLocalEvent(new AfterSerializationEvent(roots, snapshot ?? new MappingDataNode(), FileCategory.Grid));
        }
    }

    // The worker receives detached map data. Only the simulation thread reads components.
    public static (ShuttleStorageResult Result, byte[]? Payload) EncodeCapture(MappingDataNode snapshot)
    {
        try
        {
            return (ShuttleStorageResult.Ready, ShuttleStoragePayloadCodec.Pack(EncodeNode(snapshot)));
        }
        catch (ShuttlePayloadTooLargeException)
        {
            return (ShuttleStorageResult.TooLarge, null);
        }
    }
}
