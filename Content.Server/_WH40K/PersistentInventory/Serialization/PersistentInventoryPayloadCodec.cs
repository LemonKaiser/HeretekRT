using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Content.Server._WH40K.PersistentInventory.Serialization;

/// <summary>
/// Canonical persistent-inventory wire format. It intentionally does not use the map serializer
/// and therefore does not raise global map-saving events.
/// </summary>
public static class PersistentInventoryPayloadCodec
{
    public const int CurrentSchemaVersion = 1;

    private static readonly byte[] Magic = "PIS2"u8.ToArray();
    private const int HeaderLength = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static PackedPersistentInventoryPayload Pack(
        PersistentInventoryPayload payload,
        PersistentInventoryLimits limits)
    {
        Validate(payload, limits);
        var uncompressed = WriteCanonical(payload);
        if (uncompressed.Length > limits.MaxUncompressedBytes)
            throw new InvalidDataException(
                $"Persistent inventory payload is {uncompressed.Length} bytes, limit is {limits.MaxUncompressedBytes}.");

        byte[] compressed;
        using (var stream = new MemoryStream())
        {
            stream.Write(Magic);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, uncompressed.Length);
            stream.Write(length);

            using (var brotli = new BrotliStream(stream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                brotli.Write(uncompressed);
            }

            compressed = stream.ToArray();
        }

        if (compressed.Length > limits.MaxCompressedBytes)
            throw new InvalidDataException(
                $"Compressed persistent inventory payload is {compressed.Length} bytes, limit is {limits.MaxCompressedBytes}.");

        return new PackedPersistentInventoryPayload(
            compressed,
            SHA256.HashData(uncompressed),
            uncompressed.Length,
            compressed.Length,
            payload.Entities.Count,
            payload.Roots.Count);
    }

    public static PersistentInventoryPayload Unpack(
        ReadOnlySpan<byte> packed,
        ReadOnlySpan<byte> expectedSha256,
        PersistentInventoryLimits limits)
    {
        if (packed.Length < HeaderLength || !packed[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Persistent inventory payload has an invalid envelope.");

        if (packed.Length > limits.MaxCompressedBytes)
            throw new InvalidDataException("Persistent inventory payload exceeds the compressed size limit.");

        var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(packed.Slice(Magic.Length, sizeof(int)));
        if (declaredLength < 0 || declaredLength > limits.MaxUncompressedBytes)
            throw new InvalidDataException("Persistent inventory payload declares an invalid uncompressed size.");

        byte[] uncompressed;
        using (var input = new MemoryStream(packed[HeaderLength..].ToArray(), writable: false))
        using (var brotli = new BrotliStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream(Math.Min(declaredLength, 64 * 1024)))
        {
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = brotli.Read(buffer);
                if (read == 0)
                    break;

                if (output.Length + read > limits.MaxUncompressedBytes)
                    throw new InvalidDataException("Persistent inventory payload exceeds the uncompressed size limit.");

                output.Write(buffer, 0, read);
            }

            uncompressed = output.ToArray();
        }

        if (uncompressed.Length != declaredLength)
            throw new InvalidDataException("Persistent inventory payload length does not match its envelope.");

        var actualHash = SHA256.HashData(uncompressed);
        if (expectedSha256.Length != actualHash.Length ||
            !CryptographicOperations.FixedTimeEquals(actualHash, expectedSha256))
        {
            throw new InvalidDataException("Persistent inventory payload SHA-256 mismatch.");
        }

        PersistentInventoryPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<PersistentInventoryPayload>(uncompressed, JsonOptions)
                ?? throw new InvalidDataException("Persistent inventory payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Persistent inventory payload contains invalid JSON.", exception);
        }

        Validate(payload, limits);

        if (!WriteCanonical(payload).AsSpan().SequenceEqual(uncompressed))
            throw new InvalidDataException("Persistent inventory payload is not in canonical form.");

        return payload;
    }

    public static byte[] WriteCanonical(PersistentInventoryPayload payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", payload.SchemaVersion);
            writer.WriteNumber("capturedAtUnixMilliseconds", payload.CapturedAtUnixMilliseconds);
            writer.WriteString("policyId", payload.PolicyId);
            writer.WriteNumber("policyVersion", payload.PolicyVersion);

            writer.WritePropertyName("roots");
            writer.WriteStartArray();
            foreach (var root in payload.Roots
                         .OrderBy(root => root.Kind)
                         .ThenBy(root => root.Name, StringComparer.Ordinal)
                         .ThenBy(root => root.EntityId))
            {
                writer.WriteStartObject();
                writer.WriteNumber("kind", (int) root.Kind);
                writer.WriteString("name", root.Name);
                writer.WriteNumber("entityId", root.EntityId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("entities");
            writer.WriteStartArray();
            foreach (var entity in payload.Entities.OrderBy(entity => entity.EntityId))
            {
                writer.WriteStartObject();
                writer.WriteNumber("entityId", entity.EntityId);
                writer.WriteString("prototypeId", entity.PrototypeId);

                writer.WritePropertyName("components");
                writer.WriteStartArray();
                foreach (var component in entity.Components.OrderBy(component => component.ComponentId, StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("componentId", component.ComponentId);
                    writer.WritePropertyName("fields");
                    writer.WriteStartObject();
                    foreach (var (name, value) in component.Fields.OrderBy(field => field.Key, StringComparer.Ordinal))
                        writer.WriteString(name, value);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WritePropertyName("children");
                writer.WriteStartArray();
                foreach (var child in entity.Children
                             .OrderBy(child => child.ContainerId, StringComparer.Ordinal)
                             .ThenBy(child => child.Index)
                             .ThenBy(child => child.EntityId))
                {
                    writer.WriteStartObject();
                    writer.WriteString("containerId", child.ContainerId);
                    writer.WriteNumber("index", child.Index);
                    writer.WriteNumber("entityId", child.EntityId);
                    if (child.StorageLocation is { } storageLocation)
                    {
                        writer.WritePropertyName("storageLocation");
                        writer.WriteStartObject();
                        writer.WriteNumber("x", storageLocation.X);
                        writer.WriteNumber("y", storageLocation.Y);
                        writer.WriteNumber("direction", storageLocation.Direction);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static void Validate(PersistentInventoryPayload payload, PersistentInventoryLimits limits)
    {
        if (payload.SchemaVersion <= 0 || payload.SchemaVersion > CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported persistent inventory schema {payload.SchemaVersion}.");

        if (string.IsNullOrWhiteSpace(payload.PolicyId) || payload.PolicyVersion <= 0)
            throw new InvalidDataException("Persistent inventory payload has an invalid policy identity.");

        if (payload.CapturedAtUnixMilliseconds < 0)
            throw new InvalidDataException("Persistent inventory payload has an invalid capture timestamp.");

        if (payload.Roots.Count > limits.MaxRoots)
            throw new InvalidDataException("Persistent inventory payload has too many roots.");

        if (payload.Entities.Count > limits.MaxEntities)
            throw new InvalidDataException("Persistent inventory payload has too many entities.");

        var entities = new Dictionary<int, PersistentInventoryEntityState>();
        foreach (var entity in payload.Entities)
        {
            if (entity.EntityId <= 0 ||
                string.IsNullOrWhiteSpace(entity.PrototypeId) ||
                !entities.TryAdd(entity.EntityId, entity))
            {
                throw new InvalidDataException("Persistent inventory payload has an invalid entity identity.");
            }

            if (entity.Components.Count > limits.MaxComponentsPerEntity)
                throw new InvalidDataException($"Entity {entity.EntityId} has too many mutable components.");

            var componentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var component in entity.Components)
            {
                if (string.IsNullOrWhiteSpace(component.ComponentId) ||
                    !componentIds.Add(component.ComponentId) ||
                    component.Fields.Any(field => string.IsNullOrWhiteSpace(field.Key) || field.Value == null))
                {
                    throw new InvalidDataException($"Entity {entity.EntityId} has invalid component state.");
                }
            }

            var childPositions = new HashSet<(string ContainerId, int Index)>();
            foreach (var child in entity.Children)
            {
                if (string.IsNullOrWhiteSpace(child.ContainerId) ||
                    child.Index < 0 ||
                    child.EntityId <= 0 ||
                    child.StorageLocation is { Direction: < 0 or > 7 } ||
                    !childPositions.Add((child.ContainerId, child.Index)))
                {
                    throw new InvalidDataException($"Entity {entity.EntityId} has invalid child placement.");
                }
            }

            foreach (var group in entity.Children.GroupBy(child => child.ContainerId, StringComparer.Ordinal))
            {
                if (!group.OrderBy(child => child.Index)
                        .Select((child, index) => child.Index == index)
                        .All(matches => matches))
                {
                    throw new InvalidDataException(
                        $"Entity {entity.EntityId} has non-contiguous child placement.");
                }
            }
        }

        if (payload.Roots.Select(root => root.EntityId).Distinct().Count() != payload.Roots.Count)
            throw new InvalidDataException("Persistent inventory root entity is referenced more than once.");

        var parentByChild = new Dictionary<int, int>();
        foreach (var entity in entities.Values)
        {
            foreach (var child in entity.Children)
            {
                if (!entities.ContainsKey(child.EntityId) ||
                    !parentByChild.TryAdd(child.EntityId, entity.EntityId))
                {
                    throw new InvalidDataException("Persistent inventory graph has an invalid child reference.");
                }
            }
        }

        var rootIds = new HashSet<int>();
        var rootPlacements = new HashSet<(PersistentInventoryRootKind Kind, string Name)>();
        foreach (var root in payload.Roots)
        {
            if (!Enum.IsDefined(root.Kind) ||
                string.IsNullOrWhiteSpace(root.Name) ||
                !entities.ContainsKey(root.EntityId) ||
                parentByChild.ContainsKey(root.EntityId) ||
                !rootIds.Add(root.EntityId) ||
                !rootPlacements.Add((root.Kind, root.Name)))
            {
                throw new InvalidDataException("Persistent inventory payload has an invalid root.");
            }
        }

        var visited = new HashSet<int>();
        foreach (var root in rootIds)
            Visit(root, 1);

        if (visited.Count != entities.Count)
            throw new InvalidDataException("Persistent inventory payload contains unreachable entities.");

        void Visit(int entityId, int depth)
        {
            if (depth > limits.MaxDepth)
                throw new InvalidDataException("Persistent inventory graph exceeds the depth limit.");

            if (!visited.Add(entityId))
                throw new InvalidDataException("Persistent inventory graph contains a cycle.");

            foreach (var child in entities[entityId].Children)
                Visit(child.EntityId, depth + 1);
        }
    }
}
