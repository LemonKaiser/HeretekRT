using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.SectorMap.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Timing;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed partial class KoronusSectorRuleSystem
{
    [Dependency] private IDependencyCollection _dependencies = default!;

    private static readonly TimeSpan ColdSerializationBudget = TimeSpan.FromMilliseconds(5);
    private ColdUnloadOperation? _coldUnload;

    public bool ColdUnloadPending => _coldUnload != null;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_coldUnload is not { } operation)
            return;

        if (!_maps.TryGetMap(operation.MapId, out var map) || map != operation.Map ||
            !_maps.IsPaused(operation.MapId) ||
            Timing.CurTime - _metaData.GetPauseTime(operation.Map) != operation.PausedAt ||
            HasResidentMind(operation.SystemId, operation.MapId) || HasUnsavableMob(operation.MapId))
        {
            Log.Debug($"Cancelled cold snapshot for {operation.SystemId}: the map is no longer eligible.");
            CancelColdUnload();
            return;
        }

        if (!_coldOperations.Add(operation.SystemId))
            return;

        var stage = operation.Stage;
        var stepStarted = Stopwatch.GetTimestamp();
        try
        {
            if (!SnapshotEntitiesUnchanged(operation))
            {
                Log.Debug($"Cancelled cold snapshot for {operation.SystemId}: its entities changed during preparation.");
                CancelColdUnload(retry: true);
                return;
            }

            switch (operation.Stage)
            {
                case ColdSnapshotStage.Prepare:
                    var options = SerializationOptions.Default with { Category = FileCategory.Map };
                    RaiseLocalEvent(new BeforeSerializationEvent([operation.Map], [operation.MapId], options.Category));
                    operation.Serializer = new EntitySerializer(_dependencies, options);
                    operation.Entities = CollectSnapshotEntities(operation.Map, operation.Serializer);
                    operation.Serializer.ReserveYamlIds(operation.Entities);
                    operation.Remaining = operation.Entities.GetEnumerator();
                    operation.Stage = ColdSnapshotStage.Serialize;
                    break;
                case ColdSnapshotStage.Serialize:
                    var watch = Stopwatch.StartNew();
                    do
                    {
                        if (!operation.Remaining.MoveNext())
                        {
                            operation.Data = operation.Serializer!.Write();
                            RaiseLocalEvent(new AfterSerializationEvent([operation.Map], operation.Data, FileCategory.Map));
                            operation.Stage = ColdSnapshotStage.Write;
                            break;
                        }

                        var uid = operation.Remaining.Current;
                        operation.Serializer!.SerializeEntity(uid);
                        operation.SerializedTicks[uid] = Comp<MetaDataComponent>(uid).EntityLastModifiedTick;
                    } while (watch.Elapsed < ColdSerializationBudget);
                    break;
                case ColdSnapshotStage.Write:
                    if (operation.Encoded == null)
                    {
                        if (!operation.Entities.SetEquals(CollectSnapshotEntities(operation.Map, operation.Serializer!)))
                        {
                            CancelColdUnload(retry: true);
                            return;
                        }

                        // Only detached, completed data leaves the game thread. The worker owns no entities or files.
                        var data = operation.Data!;
                        operation.Encoded = Task.Run(() => EncodeColdSnapshot(data));
                        break;
                    }

                    if (!operation.Encoded.IsCompleted)
                        break;
                    var encoded = operation.Encoded.GetAwaiter().GetResult();
                    if (encoded.Error != null)
                        throw new IOException("Could not encode cold snapshot.", encoded.Error);

                    var tempPath = GetColdSnapshotTempPath(operation.SystemId);
                    if (!TryDeleteSnapshot(tempPath, operation.SystemId))
                        throw new IOException($"Cannot remove temporary snapshot {tempPath}.");

                    _resources.UserData.CreateDir(tempPath.Directory);
                    using (var writer = _resources.UserData.OpenWriteText(tempPath))
                        writer.Write(encoded.Text);

                    operation.Data = null;
                    operation.Encoded = null;
                    operation.Stage = ColdSnapshotStage.Validate;
                    break;
                case ColdSnapshotStage.Validate:
                    var path = GetColdSnapshotTempPath(operation.SystemId);
                    if (!_resources.UserData.Exists(path) || !_mapLoader.TryReadFile(path, out _))
                        throw new IOException($"Temporary cold snapshot {path} failed validation.");

                    operation.Stage = ColdSnapshotStage.Commit;
                    break;
                case ColdSnapshotStage.Commit:
                    // Recheck the tree after file I/O too: a newly inserted child must never be lost.
                    if (!operation.Entities.SetEquals(CollectSnapshotEntities(operation.Map, operation.Serializer!)) ||
                        !CommitColdUnload(operation))
                    {
                        CancelColdUnload(retry: true);
                        return;
                    }

                    _coldUnload = null;
                    break;
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Cold snapshot failed for Koronus system {operation.SystemId}; the live map was kept: {exception}");
            CancelColdUnload(retry: true);
        }
        finally
        {
            _coldOperations.Remove(operation.SystemId);
            var elapsed = Stopwatch.GetElapsedTime(stepStarted);
            if (elapsed.TotalMilliseconds >= 50)
                Log.Debug($"Cold snapshot {operation.SystemId} stage {stage} took {elapsed.TotalMilliseconds:F1} ms.");
        }
    }

    private static (string? Text, Exception? Error) EncodeColdSnapshot(MappingDataNode data)
    {
        try
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            new YamlStream(new YamlDocument(data.ToYaml())).Save(new YamlMappingFix(new Emitter(writer)), false);
            return (writer.ToString(), null);
        }
        catch (Exception exception)
        {
            // An abandoned operation must not leave an unobserved task exception after a wake-up or round reset.
            return (null, exception);
        }
    }

    private HashSet<EntityUid> CollectSnapshotEntities(EntityUid root, EntitySerializer serializer)
    {
        HashSet<EntityUid> entities = new();
        Stack<EntityUid> remaining = new();
        remaining.Push(root);
        while (remaining.TryPop(out var uid))
        {
            // Use the native save:false policy, including exclusion of an unsavable parent's children.
            if (!serializer.IsSerializable(uid))
                continue;

            entities.Add(uid);
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
                remaining.Push(child);
        }

        return entities;
    }

    private bool SnapshotEntitiesUnchanged(ColdUnloadOperation operation)
    {
        EntitySerializer? verifier = null;
        List<(EntityUid Uid, GameTick Tick)>? unchanged = null;
        foreach (var (uid, tick) in operation.SerializedTicks)
        {
            if (!TryComp<MetaDataComponent>(uid, out var metadata))
                return false;
            if (metadata.EntityLastModifiedTick == tick)
                continue;

            // Network-only UI updates can dirty a sleeping entity without changing its saved fields.
            // Use the same native serializer and YAML ids to distinguish those updates from lost data.
            verifier ??= CreateSnapshotVerifier(operation.Serializer!);
            verifier.SerializeEntity(uid);
            var id = operation.Serializer!.YamlUidMap[uid];
            if (!verifier.EntityData[id].Node.Equals(operation.Serializer.EntityData[id].Node))
            {
                var changed = string.Join(", ", EntityManager.GetComponents(uid)
                    .Where(component => component.LastModifiedTick > tick).Select(component => component.GetType().Name));
                Log.Debug($"Cold snapshot entity {ToPrettyString(uid)} has changed saved data after tick {tick}: {changed}.");
                return false;
            }

            unchanged ??= new();
            unchanged.Add((uid, metadata.EntityLastModifiedTick));
        }

        if (unchanged != null)
            foreach (var (uid, tick) in unchanged)
                operation.SerializedTicks[uid] = tick;

        return true;
    }

    private EntitySerializer CreateSnapshotVerifier(EntitySerializer original)
    {
        var verifier = new EntitySerializer(_dependencies, original.Options);
        foreach (var (uid, id) in original.YamlUidMap)
            verifier.YamlUidMap.Add(uid, id);
        verifier.YamlIds.UnionWith(original.YamlIds);
        foreach (var (prototype, cache) in original.PrototypeCache)
            verifier.PrototypeCache.Add(prototype, cache);
        return verifier;
    }

    private bool CommitColdUnload(ColdUnloadOperation operation)
    {
        if (!TryGetSectorRule(out var rule) ||
            !rule.Comp.SystemMaps.TryGetValue(operation.SystemId, out var mapId) || mapId != operation.MapId)
            return false;

        var snapshotPath = GetColdSnapshotPath(operation.SystemId);
        if (!TryDeleteSnapshot(snapshotPath, operation.SystemId))
            return false;

        _resources.UserData.Rename(GetColdSnapshotTempPath(operation.SystemId), snapshotPath);
        if (!_resources.UserData.Exists(snapshotPath))
            throw new IOException($"Committed cold snapshot {snapshotPath} is missing after rename.");

        rule.Comp.ColdUnloadedSystems.Add(operation.SystemId);
        try
        {
            _maps.DeleteMap(mapId);
        }
        catch (Exception exception)
        {
            if (!_maps.TryGetMap(mapId, out _))
            {
                Log.Warning($"Map deletion for cold Koronus system {operation.SystemId} threw after removing map {mapId}: {exception}");
                return true;
            }

            rule.Comp.ColdUnloadedSystems.Remove(operation.SystemId);
            DeleteSnapshotBestEffort(snapshotPath, operation.SystemId);
            throw;
        }

        if (_maps.TryGetMap(mapId, out _))
        {
            rule.Comp.ColdUnloadedSystems.Remove(operation.SystemId);
            DeleteSnapshotBestEffort(snapshotPath, operation.SystemId);
            return false;
        }

        Log.Info($"Cold-unloaded Koronus system {operation.SystemId} from map {mapId} into {snapshotPath}.");
        return true;
    }

    private void CancelColdUnload(bool retry = false)
    {
        if (_coldUnload is not { } operation)
            return;

        _coldUnload = null;
        DeleteSnapshotBestEffort(GetColdSnapshotTempPath(operation.SystemId), operation.SystemId);
        if (retry && TryComp<KoronusSystemMapComponent>(operation.Map, out var runtime))
            runtime.ColdUnloadRetryAt = Timing.CurTime + KoronusSectorResidencySystem.ColdUnloadRetryDelay;
    }

    private enum ColdSnapshotStage : byte { Prepare, Serialize, Write, Validate, Commit }

    private sealed class ColdUnloadOperation(string systemId, MapId mapId, EntityUid map, TimeSpan? pausedAt)
    {
        public readonly string SystemId = systemId;
        public readonly MapId MapId = mapId;
        public readonly EntityUid Map = map;
        public readonly TimeSpan? PausedAt = pausedAt;
        public readonly Dictionary<EntityUid, GameTick> SerializedTicks = new();
        public ColdSnapshotStage Stage;
        public EntitySerializer? Serializer;
        public HashSet<EntityUid> Entities = new();
        public HashSet<EntityUid>.Enumerator Remaining;
        public MappingDataNode? Data;
        public Task<(string? Text, Exception? Error)>? Encoded;
    }
}
