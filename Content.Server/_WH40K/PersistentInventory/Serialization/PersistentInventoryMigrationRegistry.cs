using System.IO;
using System.Linq;

namespace Content.Server._WH40K.PersistentInventory.Serialization;

/// <summary>
/// Single migration point for the wire format and renamed content IDs.
/// Explicitly registered and automatically detected removed prototypes
/// are pruned only together with their local subtree.
/// </summary>
public sealed class PersistentInventoryMigrationRegistry
{
    private readonly Dictionary<string, string> _prototypeAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _componentAliases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removedPrototypes = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Func<PersistentInventoryPayload, PersistentInventoryPayload>> _schemaMigrations = new();

    public void RegisterPrototypeAlias(string oldId, string newId)
    {
        RegisterAlias(_prototypeAliases, oldId, newId);
    }

    public void RegisterComponentAlias(string oldId, string newId)
    {
        RegisterAlias(_componentAliases, oldId, newId);
    }

    /// <summary>
    /// Explicitly permits losing an item and its complete local subtree after its prototype is removed.
    /// Unregistered missing prototypes still result in quarantine.
    /// </summary>
    public void RegisterRemovedPrototype(string prototypeId)
    {
        if (string.IsNullOrWhiteSpace(prototypeId))
            throw new ArgumentException("Persistent inventory removed prototype ID cannot be empty.");
        if (!_removedPrototypes.Add(prototypeId))
            throw new InvalidOperationException(
                $"Persistent inventory removed prototype {prototypeId} is already registered.");
    }

    public void RegisterSchemaMigration(
        int fromVersion,
        Func<PersistentInventoryPayload, PersistentInventoryPayload> migration)
    {
        if (fromVersion <= 0 || fromVersion >= PersistentInventoryPayloadCodec.CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(fromVersion));

        if (!_schemaMigrations.TryAdd(fromVersion, migration))
            throw new InvalidOperationException($"Schema migration from version {fromVersion} is already registered.");
    }

    public PersistentInventoryPayload Migrate(PersistentInventoryPayload payload)
    {
        return MigrateWithReport(payload).Payload;
    }

    public PersistentInventoryMigrationResult MigrateWithReport(PersistentInventoryPayload payload)
    {
        var actions = new List<string>();
        if (payload.SchemaVersion <= 0 ||
            payload.SchemaVersion > PersistentInventoryPayloadCodec.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported persistent inventory schema {payload.SchemaVersion}.");
        }

        while (payload.SchemaVersion < PersistentInventoryPayloadCodec.CurrentSchemaVersion)
        {
            if (!_schemaMigrations.TryGetValue(payload.SchemaVersion, out var migration))
                throw new InvalidDataException(
                    $"Missing persistent inventory migration from schema {payload.SchemaVersion}.");

            var oldVersion = payload.SchemaVersion;
            payload = migration(payload);
            if (payload.SchemaVersion != oldVersion + 1)
                throw new InvalidDataException(
                    $"Persistent inventory migration {oldVersion} did not advance exactly one schema version.");

            actions.Add($"schema:{oldVersion}->{payload.SchemaVersion}");
        }

        var entities = payload.Entities
            .Select(entity =>
            {
                var prototypeId = ResolveAlias(entity.PrototypeId, _prototypeAliases);
                if (prototypeId != entity.PrototypeId)
                    actions.Add($"prototype:{entity.PrototypeId}->{prototypeId}");

                var components = entity.Components
                    .Select(component =>
                    {
                        var componentId = ResolveAlias(component.ComponentId, _componentAliases);
                        if (componentId != component.ComponentId)
                            actions.Add($"component:{component.ComponentId}->{componentId}");
                        return component with { ComponentId = componentId };
                    })
                    .ToArray();
                return entity with
                {
                    PrototypeId = prototypeId,
                    Components = components,
                };
            })
            .ToArray();

        payload = payload with { Entities = entities };
        if (_removedPrototypes.Count > 0)
        {
            payload = RemovePrototypeSubtrees(
                payload,
                _removedPrototypes,
                actions,
                "removed-subtree");
        }

        return new PersistentInventoryMigrationResult(
            payload,
            actions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    public static PersistentInventoryMigrationResult RemoveUnavailablePrototypeSubtrees(
        PersistentInventoryPayload payload,
        IReadOnlySet<string> unavailablePrototypeIds)
    {
        var actions = new List<string>();
        payload = RemovePrototypeSubtrees(
            payload,
            unavailablePrototypeIds,
            actions,
            "obsolete-subtree");
        return new PersistentInventoryMigrationResult(
            payload,
            actions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    private static PersistentInventoryPayload RemovePrototypeSubtrees(
        PersistentInventoryPayload payload,
        IReadOnlySet<string> prototypeIds,
        List<string> actions,
        string actionPrefix)
    {
        var entities = payload.Entities.ToDictionary(entity => entity.EntityId);
        var removed = new HashSet<int>();
        var pending = new Stack<int>(
            payload.Entities
                .Where(entity => prototypeIds.Contains(entity.PrototypeId))
                .Select(entity => entity.EntityId));

        while (pending.TryPop(out var entityId))
        {
            if (!removed.Add(entityId) || !entities.TryGetValue(entityId, out var entity))
                continue;

            actions.Add($"{actionPrefix}:{entity.PrototypeId}");
            foreach (var child in entity.Children)
                pending.Push(child.EntityId);
        }

        if (removed.Count == 0)
            return payload;

        var retainedEntities = payload.Entities
            .Where(entity => !removed.Contains(entity.EntityId))
            .Select(entity => entity with
            {
                Children = entity.Children
                    .Where(child => !removed.Contains(child.EntityId))
                    .GroupBy(child => child.ContainerId, StringComparer.Ordinal)
                    .SelectMany(group => group
                        .OrderBy(child => child.Index)
                        .Select((child, index) => child with { Index = index }))
                    .OrderBy(child => child.ContainerId, StringComparer.Ordinal)
                    .ThenBy(child => child.Index)
                    .ToArray(),
            })
            .OrderBy(entity => entity.EntityId)
            .ToArray();
        var entityIds = retainedEntities
            .Select((entity, index) => (entity.EntityId, NewEntityId: index + 1))
            .ToDictionary(pair => pair.EntityId, pair => pair.NewEntityId);
        var migratedEntities = retainedEntities
            .Select(entity => entity with
            {
                EntityId = entityIds[entity.EntityId],
                Children = entity.Children
                    .Select(child => child with { EntityId = entityIds[child.EntityId] })
                    .ToArray(),
            })
            .ToArray();
        var migratedRoots = payload.Roots
            .Where(root => !removed.Contains(root.EntityId))
            .Select(root => root with { EntityId = entityIds[root.EntityId] })
            .ToArray();
        return payload with
        {
            Roots = migratedRoots,
            Entities = migratedEntities,
        };
    }

    private static void RegisterAlias(Dictionary<string, string> aliases, string oldId, string newId)
    {
        if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId) || oldId == newId)
            throw new ArgumentException("Persistent inventory aliases must have distinct non-empty IDs.");

        if (!aliases.TryAdd(oldId, newId))
            throw new InvalidOperationException($"Persistent inventory alias {oldId} is already registered.");
    }

    private static string ResolveAlias(string id, IReadOnlyDictionary<string, string> aliases)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (aliases.TryGetValue(id, out var next))
        {
            if (!visited.Add(id))
                throw new InvalidDataException("Persistent inventory alias cycle detected.");

            id = next;
        }

        return id;
    }
}

public sealed record PersistentInventoryMigrationResult(
    PersistentInventoryPayload Payload,
    IReadOnlyList<string> Actions);
