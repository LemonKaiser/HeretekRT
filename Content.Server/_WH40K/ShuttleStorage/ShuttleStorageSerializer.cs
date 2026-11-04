using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Content.Server._WH40K.Shipyard;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WH40K.Quests;
using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.ShuttleStorage;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Audio.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed record ShuttleStorageInspection(int Width, int Height, HashSet<EntityUid> Entities);

public sealed partial class ShuttleStorageSerializer : EntitySystem
{
    public string? LastInspectionFailure { get; private set; }
    [Dependency] private IDependencyCollection _dependencies = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipyardPassengerSystem _passengers = default!;
    [Dependency] private SharedKoronusStationPropertySystem _property = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private ISerializationManager _serialization = default!;

    public ShuttleStorageResult Inspect(EntityUid grid, out ShuttleStorageInspection info)
    {
        info = new(0, 0, []);
        if (!TryComp(grid, out MapGridComponent? comp) || HasComp<MapComponent>(grid) ||
            comp.LocalAABB.IsEmpty())
            return ShuttleStorageResult.InvalidShip;
        var bounds = comp.LocalAABB;
        var min = bounds.BottomLeft;
        var max = bounds.TopRight;
        var width = (int) MathF.Ceiling(max.X) - (int) MathF.Floor(min.X);
        var height = (int) MathF.Ceiling(max.Y) - (int) MathF.Floor(min.Y);
        info = new(width, height, []);
        if (!ShuttleStorageLimits.FitsDimensions(width, height))
            return ShuttleStorageResult.Size;
        if (_atmosphere.HasUnsafeShuttleAtmosphere(grid))
        {
            LastInspectionFailure = "Active tile hotspots.";
            return ShuttleStorageResult.Unsupported;
        }

        var inverse = _transform.GetInvWorldMatrix(grid);
        var rotation = _transform.GetWorldRotation(grid);
        Stack<(EntityUid Uid, int Depth)> pending = new();
        pending.Push((grid, 0));
        var result = ShuttleStorageResult.Ready;
        while (pending.TryPop(out var next))
        {
            if (IsTransientAudio(next.Uid))
                continue;
            if (!info.Entities.Add(next.Uid) || info.Entities.Count > Math.Clamp(_config.GetCVar(ShuttleStorageCVars.MaxEntities), 1, 10000) ||
                next.Depth > Math.Clamp(_config.GetCVar(ShuttleStorageCVars.MaxDepth), 1, 64))
                return ShuttleStorageResult.Complexity;
            if (_passengers.IsPassenger(next.Uid))
                result = ShuttleStorageResult.Passengers;
            TryComp(next.Uid, out GasTankComponent? tank);
            if (MetaData(next.Uid).EntityPrototype?.MapSavable == false || _property.IsProperty(next.Uid) ||
                HasComp<Wh40kQuestItemComponent>(next.Uid) || HasComp<ProjectileComponent>(next.Uid) ||
                tank != null && tank.Integrity != 3 ||
                next.Uid != grid && HasComp<MapGridComponent>(next.Uid))
                if (result != ShuttleStorageResult.Passengers)
                {
                    LastInspectionFailure = $"{ToPrettyString(next.Uid)}: savable={MetaData(next.Uid).EntityPrototype?.MapSavable}, " +
                        $"property={_property.IsProperty(next.Uid)}, quest={HasComp<Wh40kQuestItemComponent>(next.Uid)}, " +
                        $"projectile={HasComp<ProjectileComponent>(next.Uid)}, tankIntegrity={tank?.Integrity}, nestedGrid={next.Uid != grid && HasComp<MapGridComponent>(next.Uid)}";
                    result = ShuttleStorageResult.Unsupported;
                }

            if (next.Uid != grid)
            {
                var pos = Vector2.Transform(_transform.GetWorldPosition(next.Uid), inverse);
                min = Vector2.Min(min, new Vector2(MathF.Floor(pos.X), MathF.Floor(pos.Y)));
                max = Vector2.Max(max, new Vector2(MathF.Floor(pos.X) + 1, MathF.Floor(pos.Y) + 1));
                if (TryComp(next.Uid, out FixturesComponent? fixtures))
                {
                    var relative = new Robust.Shared.Physics.Transform(pos, _transform.GetWorldRotation(next.Uid) - rotation);
                    foreach (var fixture in fixtures.Fixtures.Values)
                    {
                        if (!fixture.Hard)
                            continue;
                        for (var child = 0; child < fixture.Shape.ChildCount; child++)
                        {
                            var box = fixture.Shape.ComputeAABB(relative, child).Enlarged(-0.01f);
                            min = Vector2.Min(min, box.BottomLeft);
                            max = Vector2.Max(max, box.TopRight);
                        }
                    }
                }
            }
            var children = Transform(next.Uid).ChildEnumerator;
            while (children.MoveNext(out var childUid))
                pending.Push((childUid, next.Depth + 1));
        }

        width = (int) MathF.Ceiling(max.X - 0.001f) - (int) MathF.Floor(min.X + 0.001f);
        height = (int) MathF.Ceiling(max.Y - 0.001f) - (int) MathF.Floor(min.Y + 0.001f);
        info = new(width, height, info.Entities);
        return !ShuttleStorageLimits.FitsDimensions(width, height)
            ? ShuttleStorageResult.Size : result;
    }

    private EntitySerializer CreateSerializer(EntityUid grid, ShuttleStorageInspection info, Action markExternal, bool notify = true)
    {
        var options = SerializationOptions.Default with
        {
            Category = FileCategory.Grid,
            MissingEntityBehaviour = MissingEntityBehaviour.AutoInclude,
        };
        if (notify)
            RaiseLocalEvent(new BeforeSerializationEvent([grid], [Transform(grid).MapID], FileCategory.Grid));
        var serializer = new EntitySerializer(_dependencies, options);
        var members = info.Entities;
        serializer.OnIsSerializeable += (Entity<MetaDataComponent> entity, ref bool serializable) =>
        {
            if (IsTransientAudio(entity.Owner))
            {
                serializable = false;
                return;
            }
            if (!members.Contains(entity.Owner))
            {
                markExternal();
                serializable = false;
            }
        };
        // Runtime station/deed registrations are rebuilt on delivery, never imported from the old world.
        if (Transform(grid).MapUid is { } map)
            serializer.ErroringEntities.Add(map);
        if (TryComp(grid, out StationMemberComponent? member))
            serializer.ErroringEntities.Add(member.Station);
        if (TryComp(grid, out ShuttleDeedComponent? deed) && deed.DeedHolder is { } holder && !members.Contains(holder))
            serializer.ErroringEntities.Add(holder);
        return serializer;
    }

    private void ApplySnapshotAdapters(EntityUid grid, EntitySerializer serializer)
    {
        foreach (var (entity, data) in serializer.EntityData.Values)
        {
            if (TryComp(entity, out JointComponent? joints) && joints.JointCount == 0 && joints.Relay == null &&
                data.TryGet("components", out SequenceDataNode? runtimeComponents))
            {
                foreach (var component in runtimeComponents.OfType<MappingDataNode>().ToArray())
                    if (((ValueDataNode) component["type"]).Value == "Joint")
                        runtimeComponents.Remove(component);
            }
            if (!TryComp(entity, out DamageableComponent? damageable))
                continue;
            if (!data.TryGet("components", out SequenceDataNode? components))
                data.Add("components", components = new SequenceDataNode());
            var damage = components.OfType<MappingDataNode>().FirstOrDefault(component =>
                ((ValueDataNode) component["type"]).Value == "Damageable");
            if (damage == null)
            {
                damage = new MappingDataNode { { "type", "Damageable" } };
                components.Add(damage);
            }
            // Damageable.damage is read-only YAML in content. It is valid input for native load,
            // but native saving omits it, which would otherwise repair every stored machine.
            var types = new MappingDataNode();
            foreach (var (type, amount) in damageable.Damage.DamageDict)
                if (amount != FixedPoint2.Zero)
                    types.Add(type, _serialization.WriteValue(amount, alwaysWrite: true));
            damage["damage"] = new MappingDataNode { { "types", types } };
        }
        // A station is a runtime registration, not part of this grid. Omitting its component also
        // avoids a non-nullable reference to the old station being read as an invalid entity.
        if (!serializer.EntityData.TryGetValue(serializer.YamlUidMap[grid], out var root))
            return;
        var gridData = root.Node;
        if (gridData.TryGet("components", out SequenceDataNode? gridComponents))
        {
            foreach (var component in gridComponents.OfType<MappingDataNode>().ToArray())
                if (((ValueDataNode) component["type"]).Value == "StationMember")
                    gridComponents.Remove(component);
        }
    }

    private static byte[] EncodeNode(MappingDataNode node)
    {
        using var stream = new BoundedStream(ShuttleStorageLimits.Bytes);
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true), 4096, leaveOpen: true))
        {
            new YamlStream(new YamlDocument(node.ToYaml())).Save(new YamlMappingFix(new Emitter(writer)), false);
        }
        return stream.ToArray();
    }

    private bool IsTransientAudio(EntityUid entity) => MetaData(entity).EntityPrototype is { ID: "Audio", MapSavable: false } &&
        HasComp<AudioComponent>(entity) && Transform(entity).ChildCount == 0 && !_passengers.IsPassenger(entity);

    public bool Validate(byte[] payload, byte[]? hash = null)
    {
        if (payload.Length is 0 or > ShuttleStorageLimits.Bytes || hash != null &&
            (hash.Length != 32 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), hash)))
            return false;
        try
        {
            return ValidateNode(ParseNode(payload));
        }
        catch (Exception) { return false; }
    }

    public static MappingDataNode ParseNode(byte[] payload)
    {
        using var reader = new StringReader(new UTF8Encoding(false, true).GetString(ShuttleStoragePayloadCodec.Unpack(payload)));
        var docs = DataNodeParser.ParseYamlStream(reader).ToArray();
        if (docs.Length != 1 || docs[0].Root is not MappingDataNode root)
            throw new InvalidDataException("Expected one native grid document.");
        return root;
    }

    public bool ValidateNode(MappingDataNode root)
    {
        try
        {
            if (root["meta"] is not MappingDataNode meta || ((ValueDataNode) meta["format"]).AsInt() != EntitySerializer.MapFormatVersion ||
                ((ValueDataNode) meta["category"]).Value != "Grid" ||
                ((SequenceDataNode) root["grids"]).Count != 1 || ((SequenceDataNode) root["maps"]).Count != 0 ||
                ((SequenceDataNode) root["nullspace"]).Count != 0 || ((SequenceDataNode) root["orphans"]).Count != 1)
                return false;
            var count = 0;
            foreach (var entry in (SequenceDataNode) root["entities"])
            {
                var group = (MappingDataNode) entry;
                var prototype = ((ValueDataNode) group["proto"]).Value;
                if (prototype.Length > 0 && !_prototypes.HasIndex<EntityPrototype>(prototype))
                    return false;
                foreach (var entity in (SequenceDataNode) group["entities"])
                {
                    count++;
                    if (count > Math.Clamp(_config.GetCVar(ShuttleStorageCVars.MaxEntities), 1, 10000))
                        return false;
                    var data = (MappingDataNode) entity;
                    if (!data.TryGet("components", out SequenceDataNode? components))
                        continue;
                    foreach (var component in components)
                    {
                        var type = ((ValueDataNode) ((MappingDataNode) component)["type"]).Value;
                        if (!_factory.TryGetRegistration(type, out _))
                            return false;
                    }
                }
            }
            return count > 0 && ((ValueDataNode) meta["entityCount"]).AsInt() == count;
        }
        catch (Exception) { return false; }
    }

    public bool TryLoad(MapId map, byte[] payload, out EntityUid grid)
    {
        grid = EntityUid.Invalid;
        try { payload = ShuttleStoragePayloadCodec.Unpack(payload); }
        catch (InvalidDataException) { return false; }
        if (!Validate(payload))
            return false;
        using var reader = new StringReader(Encoding.UTF8.GetString(payload));
        if (!_loader.TryLoadGrid(map, reader, "WH40K personal shuttle", out var loaded))
            return false;
        grid = loaded.Value.Owner;
        return Inspect(grid, out _) == ShuttleStorageResult.Ready;
    }

    public bool Equivalent(byte[] first, byte[] second, out string difference)
    {
        try
        {
            MappingDataNode Read(byte[] bytes)
            {
                var node = ParseNode(bytes);
                ((MappingDataNode) node["meta"]).Remove("time");
                return node;
            }
            var before = Read(first);
            var after = Read(second);
            difference = SnapshotDifference(before, after, "snapshot") ?? "";
            return difference.Length == 0;
        }
        catch (Exception error)
        {
            difference = $"Cannot compare native snapshots: {error.Message}";
            return false;
        }
    }

    private static string? SnapshotDifference(DataNode before, DataNode after, string path)
    {
        if (before.Equals(after)) return null;
        if (before is MappingDataNode first && after is MappingDataNode second)
        {
            foreach (var (key, value) in first)
            {
                if (!second.TryGet(key, out DataNode? other)) return path + "/" + key + " (removed)";
                if (SnapshotDifference(value, other, path + "/" + key) is { } difference) return difference;
            }
            return path + " (fields added)";
        }
        if (before is SequenceDataNode list && after is SequenceDataNode otherList)
        {
            for (var i = 0; i < Math.Min(list.Count, otherList.Count); i++)
            {
                var label = i.ToString();
                if (list[i] is MappingDataNode item)
                    foreach (var field in new[] { "type", "uid", "proto" })
                        if (item.TryGet(field, out ValueDataNode? id)) { label = field + "=" + id.Value; break; }
                if (SnapshotDifference(list[i], otherList[i], path + "[" + label + "]") is { } difference) return difference;
            }
            return path + " (entries added or removed)";
        }
        return path;
    }

    private sealed class ShuttlePayloadTooLargeException : IOException;

    private sealed class BoundedStream(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > limit)
                throw new ShuttlePayloadTooLargeException();
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > limit)
                throw new ShuttlePayloadTooLargeException();
            base.Write(buffer);
        }
    }
}
