using Stopwatch = System.Diagnostics.Stopwatch;
using System.Linq;
using Content.Shared._WH40K.ShuttleStorage;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSerializer
{
    public sealed class CaptureSession(EntityUid grid, ShuttleStorageInspection info, ShuttleStorageResult result)
    {
        public readonly EntityUid Grid = grid;
        public readonly ShuttleStorageInspection Info = info;
        public ShuttleStorageResult Result = result;
        public byte[]? Payload;
        public bool Changed;
        internal EntitySerializer? Serializer;
        internal IEnumerator<EntityUid>? Remaining;
        internal MappingDataNode? Node;
        internal bool External;
        internal readonly Dictionary<EntityUid, GameTick> Ticks = new();
    }

    public CaptureSession BeginCapture(EntityUid grid)
    {
        var result = Inspect(grid, out var info);
        var capture = new CaptureSession(grid, info, result);
        if (result != ShuttleStorageResult.Ready)
            return capture;
        try
        {
            capture.Serializer = CreateSerializer(grid, info, () => capture.External = true);
            capture.Serializer.ReserveYamlIds(info.Entities);
            capture.Remaining = info.Entities.GetEnumerator();
        }
        catch (Exception error)
        {
            Log.Warning($"Cannot prepare shuttle capture: {error.Message}");
            capture.Result = ShuttleStorageResult.Unsupported;
        }
        return capture;
    }

    // Same native serializer and budget as cold storage. Every ECS read remains on the simulation thread.
    public bool AdvanceCapture(CaptureSession capture)
    {
        if (capture.Result != ShuttleStorageResult.Ready || capture.Node != null)
            return true;
        var watch = Stopwatch.StartNew();
        try
        {
            var serializer = capture.Serializer!;
            do
            {
                if (!capture.Remaining!.MoveNext())
                {
                    capture.Remaining.Dispose();
                    if (capture.External || serializer.YamlUidMap.Keys.Any(uid => !capture.Info.Entities.Contains(uid)) ||
                        serializer.GetCategory() != FileCategory.Grid ||
                        !capture.Info.Entities.SetEquals(serializer.EntityData.Values.Select(entity => entity.Uid)))
                    {
                        capture.Result = ShuttleStorageResult.Unsupported;
                        return true;
                    }
                    ApplySnapshotAdapters(capture.Grid, serializer);
                    capture.Node = serializer.Write();
                    RaiseLocalEvent(new AfterSerializationEvent([capture.Grid], capture.Node, FileCategory.Grid));
                    return true;
                }
                var entity = capture.Remaining.Current;
                serializer.SerializeEntity(entity);
                capture.Ticks[entity] = MetaData(entity).EntityLastModifiedTick;
            } while (watch.Elapsed < TimeSpan.FromMilliseconds(5));
            return false;
        }
        catch (Exception error)
        {
            capture.Remaining?.Dispose();
            capture.Result = ShuttleStorageResult.Unsupported;
            Log.Warning($"Incremental shuttle capture rejected: {error.Message}");
            return true;
        }
    }

    public bool CaptureStillCurrent(CaptureSession capture)
    {
        if (Inspect(capture.Grid, out var current) != ShuttleStorageResult.Ready || !capture.Info.Entities.SetEquals(current.Entities)) return false;
        foreach (var (entity, tick) in capture.Ticks)
            if (!TryComp(entity, out MetaDataComponent? metadata) || metadata.EntityLastModifiedTick != tick)
            {
                if (metadata == null) return false;
                // As in cold storage, network-only dirtying is allowed only after comparing native saved fields.
                var external = false;
                var verifier = CreateSerializer(capture.Grid, capture.Info, () => external = true, notify: false);
                var original = capture.Serializer!;
                foreach (var (uid, id) in original.YamlUidMap) verifier.YamlUidMap.Add(uid, id);
                verifier.YamlIds.UnionWith(original.YamlIds);
                foreach (var (prototype, cache) in original.PrototypeCache) verifier.PrototypeCache.Add(prototype, cache);
                verifier.SerializeEntity(entity);
                ApplySnapshotAdapters(capture.Grid, verifier);
                var yaml = original.YamlUidMap[entity];
                if (external || !original.EntityData[yaml].Node.Equals(verifier.EntityData[yaml].Node))
                {
                    var before = original.EntityData[yaml].Node;
                    var after = verifier.EntityData[yaml].Node;
                    var changed = new List<string>();
                    if (before.TryGet("components", out SequenceDataNode? components) && after.TryGet("components", out SequenceDataNode? otherComponents))
                        foreach (var component in components.OfType<MappingDataNode>())
                        {
                            var type = ((ValueDataNode) component["type"]).Value;
                            var matching = otherComponents.OfType<MappingDataNode>().FirstOrDefault(candidate => ((ValueDataNode) candidate["type"]).Value == type);
                            if (matching == null) { changed.Add(type); continue; }
                            changed.AddRange(component.Children.Where(pair => !matching.Children.TryGetValue(pair.Key, out var value) || !pair.Value.Equals(value))
                                .Select(pair => type + "/" + pair.Key));
                        }
                    LastInspectionFailure = $"Shuttle saved fields changed during capture: {ToPrettyString(entity)}: {string.Join(", ", changed)}";
                    Log.Debug(LastInspectionFailure);
                    return false;
                }
            }
        return true;
    }

    // Only detached data is used here. No ECS, configuration, prototype lookup or entity events on the worker.
    public static (ShuttleStorageResult Result, byte[]? Payload, MappingDataNode? Parsed) EncodeCapture(CaptureSession capture)
    {
        try
        {
            var payload = EncodeNode(capture.Node!);
            var parsed = ParseNode(payload);
            return (ShuttleStorageResult.Ready, ShuttleStoragePayloadCodec.Pack(payload), parsed);
        }
        catch (ShuttlePayloadTooLargeException) { return (ShuttleStorageResult.TooLarge, null, null); }
        catch (Exception) { return (ShuttleStorageResult.Unsupported, null, null); }
    }
}
