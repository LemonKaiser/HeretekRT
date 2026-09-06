using System.IO;
using System.Linq;
using Content.Server._WH40K.ItemRarity;
using Content.Server.Charges.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Durability;
using Content.Server.Labels;
using Content.Server.Light.EntitySystems;
using Content.Server.Medical.SuitSensors;
using Content.Server.PDA.Ringer;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Server.Stack;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.CCVar;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Mind.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared._WH40K.PersistentInventory;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.PersistentInventory.Serialization;

public enum PersistentInventoryCaptureStatus
{
    Success = 0,
    Rejected = 1,
}

public sealed record PersistentInventoryCaptureResult(
    PersistentInventoryCaptureStatus Status,
    PersistentInventoryPayload? Payload,
    IReadOnlyList<string> DeniedPrototypeIds,
    IReadOnlyList<EntityUid> CapturedEntities,
    IReadOnlyList<EntityUid> DeniedEntities,
    string? Error)
{
    public bool IsSuccess => Status == PersistentInventoryCaptureStatus.Success && Payload != null;

    public IReadOnlyList<PersistentInventoryOmittedComponent> OmittedComponents { get; init; } =
        Array.Empty<PersistentInventoryOmittedComponent>();
}

public sealed record PersistentInventoryOmittedComponent(
    string PrototypeId,
    string ComponentId,
    int Occurrences);

public sealed record PersistentInventoryRestoreResult(
    bool Success,
    IReadOnlyDictionary<int, EntityUid> Entities,
    IReadOnlyList<(PersistentInventoryRoot Root, EntityUid Entity)> Roots,
    string? Error,
    IReadOnlyList<string>? MigrationActions = null);

public sealed record PersistentInventoryRestorePreparation(
    PersistentInventoryPayload Payload,
    IReadOnlyList<string> MigrationActions,
    IReadOnlyList<string> RemovedPrototypeIds)
{
    public bool RequiresDatabaseRewrite => MigrationActions.Count > 0;
}

public sealed partial class PersistentInventorySnapshotSerializer : EntitySystem
{
    public const string DefaultPolicyId = "Wh40kDefault";

    private static readonly HashSet<string> DeniedComponentIds = new(StringComparer.Ordinal)
    {
        "Actor",
        "Body",
        "BodyPart",
        "Brain",
        "GameRule",
        "Map",
        "MapGrid",
        "MindContainer",
        "MobState",
        "MobStateActions",
        "Organ",
        "Shuttle",
        "StationMember",
    };

    private static readonly string[] DeniedComponentPrefixes =
    {
        "Admin",
        "GameRule",
        "Mapping",
        "ServerControl",
    };

    private static readonly HashSet<string> ProtectedGraphComponentIds = new(StringComparer.Ordinal)
    {
        "Actor",
        "GameRule",
        "Map",
        "MapGrid",
        "MobState",
        "MobStateActions",
        "Shuttle",
        "StationMember",
    };

    private static readonly HashSet<string> DeniedCategoryIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Debug",
        "Mapping",
        "Test",
    };

    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedItemSystem _items = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ComponentTogglerSystem _componentTogglers = default!;
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private SharedIdCardSystem _idCards = default!;
    [Dependency] private BatterySystem _batteries = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ItemDurabilitySystem _durability = default!;
    [Dependency] private PowerCellSystem _powerCells = default!;
    [Dependency] private LabelSystem _labels = default!;
    [Dependency] private GunSystem _guns = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PressurizedSolutionSystem _pressurized = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private RingerSystem _ringers = default!;
    [Dependency] private UnpoweredFlashlightSystem _flashlights = default!;
    [Dependency] private SuitSensorSystem _suitSensors = default!;
    [Dependency] private OpenableSystem _openable = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private ExpendableLightSystem _expendableLights = default!;
    [Dependency] private ItemRarityRandomizationSystem _itemRarity = default!;
    [Dependency] private ChargesSystem _charges = default!;

    private readonly PersistentInventoryMigrationRegistry _migrations = new();
    private readonly List<IItemStateAdapter> _adapters = new();
    private readonly Dictionary<string, IItemStateAdapter> _adaptersById =
        new(StringComparer.Ordinal);
    private readonly Dictionary<Type, IItemStateAdapter> _adaptersByType = new();
    private readonly List<IPersistentInventoryComponentSanitizer> _sanitizers = new();

    public override void Initialize()
    {
        base.Initialize();

        _adapters.Add(new StackItemStateAdapter(EntityManager, _stacks));
        _adapters.Add(new RewardDeliveryClaimItemStateAdapter(EntityManager));
        _adapters.Add(new LimitedChargesItemStateAdapter(EntityManager, _charges));
        _adapters.Add(new IdCardItemStateAdapter(EntityManager, _prototypes, _idCards));
        _adapters.Add(new AccessItemStateAdapter(EntityManager, _prototypes, _access));
        _adapters.Add(new BatteryItemStateAdapter(EntityManager, _batteries));
        _adapters.Add(new ItemDurabilityStateAdapter(EntityManager, _durability));
        _adapters.Add(new PowerCellDrawItemStateAdapter(EntityManager, _powerCells));
        _adapters.Add(new LabelItemStateAdapter(EntityManager, _labels));
        _adapters.Add(new PressurizedSolutionItemStateAdapter(EntityManager, _timing, _pressurized));
        _adapters.Add(new DeviceNetworkItemStateAdapter(EntityManager, _deviceNetwork));
        _adapters.Add(new RingerItemStateAdapter(EntityManager, _ringers));
        _adapters.Add(new UnpoweredFlashlightItemStateAdapter(EntityManager, _flashlights));
        _adapters.Add(new SuitSensorItemStateAdapter(EntityManager, _suitSensors));
        _adapters.Add(new DamageItemStateAdapter(EntityManager, _damageable));
        _adapters.Add(new BasicAmmoItemStateAdapter(EntityManager, _guns));
        _adapters.Add(new BallisticAmmoItemStateAdapter(EntityManager, _guns));
        _adapters.Add(new GunItemStateAdapter(EntityManager, _guns));
        _adapters.Add(new OpenableItemStateAdapter(EntityManager, _openable));
        _adapters.Add(new PaperItemStateAdapter(EntityManager, _paper));
        _adapters.Add(new FiberItemStateAdapter(EntityManager));
        _adapters.Add(new RandomSpriteItemStateAdapter(EntityManager));
        _adapters.Add(new ExpendableLightItemStateAdapter(EntityManager, _expendableLights));
        _adapters.Add(new ItemRarityItemStateAdapter(_itemRarity));
        _adapters.Add(new ForensicsItemStateAdapter(EntityManager));
        _adapters.Add(new SolutionItemStateAdapter(EntityManager, _solutions));
        foreach (var adapter in _adapters)
        {
            if (!_adaptersById.TryAdd(adapter.ComponentId, adapter))
                throw new InvalidOperationException($"Duplicate persistent inventory adapter ID {adapter.ComponentId}.");

            var componentType = _componentFactory.GetRegistration(adapter.ComponentId).Type;
            if (!_adaptersByType.TryAdd(componentType, adapter))
            {
                throw new InvalidOperationException(
                    $"Duplicate persistent inventory adapter for component type {componentType}.");
            }
        }

        _sanitizers.Add(new PersistentInventoryDerivedStateSanitizer(_componentTogglers));
    }

    public PersistentInventoryMigrationRegistry Migrations => _migrations;

    public PersistentInventoryLimits GetConfiguredLimits()
    {
        return new PersistentInventoryLimits(
            _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMaxRoots),
            _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMaxEntities),
            _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMaxDepth),
            _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMaxComponentsPerEntity),
            _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMaxUncompressedBytes),
            _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMaxCompressedBytes));
    }

    public PersistentInventoryRestorePreparation PrepareForRestore(
        PersistentInventoryPayload originalPayload,
        PersistentInventoryLimits? limits = null)
    {
        var activeLimits = limits ?? GetConfiguredLimits();
        var migration = _migrations.MigrateWithReport(originalPayload);
        var unavailablePrototypeIds = migration.Payload.Entities
            .Select(entity => entity.PrototypeId)
            .Where(prototypeId => !_prototypes.TryIndex<EntityPrototype>(prototypeId, out _))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var cleanup = PersistentInventoryMigrationRegistry.RemoveUnavailablePrototypeSubtrees(
            migration.Payload,
            unavailablePrototypeIds.ToHashSet(StringComparer.Ordinal));
        var actions = migration.Actions
            .Concat(cleanup.Actions)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        PersistentInventoryPayloadCodec.Validate(cleanup.Payload, activeLimits);
        return new PersistentInventoryRestorePreparation(
            cleanup.Payload,
            actions,
            unavailablePrototypeIds);
    }

    public PersistentInventoryCaptureResult CaptureOwner(
        EntityUid owner,
        string policyId = DefaultPolicyId,
        long? capturedAtUnixMilliseconds = null,
        PersistentInventoryLimits? limits = null)
    {
        var roots = new List<CaptureRoot>();
        foreach (var hand in _hands.EnumerateHands(owner)
                     .Where(hand => hand.HeldEntity != null)
                     .OrderBy(hand => hand.Name, StringComparer.Ordinal))
        {
            roots.Add(new CaptureRoot(
                PersistentInventoryRootKind.Hand,
                hand.Name,
                hand.HeldEntity!.Value));
        }

        var enumerator = _inventory.GetSlotEnumerator(owner);
        while (enumerator.NextItem(out var item, out var slot))
        {
            roots.Add(new CaptureRoot(
                PersistentInventoryRootKind.InventorySlot,
                slot.Name,
                item));
        }

        return CaptureRoots(
            roots,
            policyId,
            capturedAtUnixMilliseconds ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            limits ?? GetConfiguredLimits());
    }

    public PersistentInventoryCaptureResult CaptureRoots(
        IReadOnlyList<CaptureRoot> sourceRoots,
        string policyId,
        long capturedAtUnixMilliseconds,
        PersistentInventoryLimits limits)
    {
        if (!_prototypes.TryIndex<PersistentInventoryPolicyPrototype>(policyId, out var policy))
            return Rejected($"Unknown persistent inventory policy {policyId}.");

        if (sourceRoots.Count > limits.MaxRoots)
            return Rejected("Persistent inventory root limit exceeded.");

        var state = new CaptureState(policy, limits);
        var payloadRoots = new List<PersistentInventoryRoot>();
        var safetyVisited = new HashSet<EntityUid>();
        foreach (var root in sourceRoots
                     .OrderBy(root => root.Kind)
                     .ThenBy(root => root.Name, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(root.Name))
                return Rejected("Persistent inventory root has an empty placement name.", state.DeniedPrototypeIds);

            var safetyError = ValidateContainmentSafety(
                root.Entity,
                depth: 1,
                $"{root.Kind}:{root.Name}",
                limits,
                safetyVisited);
            if (safetyError != null)
                return Rejected(safetyError, state.DeniedPrototypeIds);

            var result = CaptureEntity(root.Entity, depth: 1, state);
            if (result.Status == CaptureEntityStatus.Rejected)
                return Rejected(result.Error!, state.DeniedPrototypeIds);

            if (result.Status == CaptureEntityStatus.Denied)
                continue;

            payloadRoots.Add(new PersistentInventoryRoot(root.Kind, root.Name, result.EntityId));
        }

        var payload = new PersistentInventoryPayload(
            PersistentInventoryPayloadCodec.CurrentSchemaVersion,
            capturedAtUnixMilliseconds,
            policy.ID,
            policy.Version,
            payloadRoots,
            state.Entities);

        try
        {
            PersistentInventoryPayloadCodec.Validate(payload, limits);
            _ = PersistentInventoryPayloadCodec.Pack(payload, limits);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return Rejected(exception.Message, state.DeniedPrototypeIds);
        }

        return new PersistentInventoryCaptureResult(
            PersistentInventoryCaptureStatus.Success,
            payload,
            state.DeniedPrototypeIds.Order(StringComparer.Ordinal).ToArray(),
            state.EntityIds.Keys.OrderBy(uid => uid.ToString(), StringComparer.Ordinal).ToArray(),
            state.DeniedEntities.OrderBy(uid => uid.ToString(), StringComparer.Ordinal).ToArray(),
            null)
        {
            OmittedComponents = state.OmittedComponents
                .OrderBy(entry => entry.Key.PrototypeId, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.ComponentId, StringComparer.Ordinal)
                .Select(entry => new PersistentInventoryOmittedComponent(
                    entry.Key.PrototypeId,
                    entry.Key.ComponentId,
                    entry.Value))
                .ToArray(),
        };
    }

    public PersistentInventoryRestoreResult RestoreIsolated(
        PersistentInventoryPayload originalPayload,
        PersistentInventoryLimits? limits = null)
    {
        var activeLimits = limits ?? GetConfiguredLimits();
        PersistentInventoryPayload payload;
        IReadOnlyList<string> migrationActions;
        try
        {
            var migration = _migrations.MigrateWithReport(originalPayload);
            payload = migration.Payload;
            migrationActions = migration.Actions;
            PersistentInventoryPayloadCodec.Validate(payload, activeLimits);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            return RestoreFailed(exception.Message);
        }

        if (!_prototypes.TryIndex<PersistentInventoryPolicyPrototype>(payload.PolicyId, out var policy) ||
            policy.Version != payload.PolicyVersion)
        {
            return RestoreFailed("Persistent inventory policy version is unavailable.");
        }

        foreach (var entityState in payload.Entities)
        {
            if (!_prototypes.TryIndex<EntityPrototype>(entityState.PrototypeId, out var prototype))
                return RestoreFailed($"Persistent inventory prototype {entityState.PrototypeId} is unavailable.");

            var policyError = ValidatePrototypePolicy(prototype, policy);
            if (policyError != null)
                return RestoreFailed(policyError);
        }

        var spawned = new Dictionary<int, EntityUid>();
        try
        {
            foreach (var entityState in payload.Entities.OrderBy(entity => entity.EntityId))
            {
                var uid = EntityManager.SpawnEntity(entityState.PrototypeId, MapCoordinates.Nullspace);
                spawned.Add(entityState.EntityId, uid);
            }

            ClearPrototypeGeneratedContents(spawned);

            foreach (var entityState in payload.Entities.OrderBy(entity => entity.EntityId))
            {
                    var uid = spawned[entityState.EntityId];
                    foreach (var componentState in entityState.Components
                             .OrderBy(component =>
                                 _adaptersById.GetValueOrDefault(component.ComponentId)?.RestorePriority ??
                                 int.MinValue)
                             .ThenBy(component => component.ComponentId, StringComparer.Ordinal))
                {
                    if (!_adaptersById.TryGetValue(componentState.ComponentId, out var adapter))
                        return CleanupAndFail(spawned, $"Unknown state adapter {componentState.ComponentId}.");

                    if (!adapter.TryRestore(uid, componentState, out var error))
                        return CleanupAndFail(spawned, error ?? $"Cannot restore {componentState.ComponentId}.");
                }
            }

            foreach (var entityState in payload.Entities.OrderBy(entity => entity.EntityId))
            {
                var parent = spawned[entityState.EntityId];
                if (!TryRestoreChildren(parent, entityState.Children, spawned, out var error))
                {
                    return CleanupAndFail(
                        spawned,
                        error ?? $"Cannot restore children of entity {entityState.EntityId}.");
                }
            }

            var rootSources = payload.Roots
                .Select(root => new CaptureRoot(root.Kind, root.Name, spawned[root.EntityId]))
                .ToArray();
            var verification = CaptureRoots(
                rootSources,
                payload.PolicyId,
                payload.CapturedAtUnixMilliseconds,
                activeLimits);

            if (!verification.IsSuccess || verification.Payload == null)
                return CleanupAndFail(spawned, verification.Error ?? "Cannot verify restored persistent inventory.");

            var comparableVerification = NormalizeLegacyStorageLocationsForComparison(
                payload,
                verification.Payload);
            if (!PersistentInventoryPayloadCodec.WriteCanonical(comparableVerification)
                    .AsSpan()
                    .SequenceEqual(PersistentInventoryPayloadCodec.WriteCanonical(payload)))
            {
                return CleanupAndFail(
                    spawned,
                    DescribeRoundTripDifference(payload, comparableVerification));
            }

            var roots = payload.Roots
                .Select(root => (root, spawned[root.EntityId]))
                .ToArray();
            return new PersistentInventoryRestoreResult(
                true,
                spawned,
                roots,
                null,
                migrationActions);
        }
        catch (Exception exception)
        {
            return CleanupAndFail(spawned, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public void DeleteIsolated(PersistentInventoryRestoreResult result)
    {
        foreach (var uid in result.Entities
                     .OrderByDescending(pair => pair.Key)
                     .Select(pair => pair.Value))
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }
    }

    private CaptureEntityResult CaptureEntity(EntityUid uid, int depth, CaptureState state)
    {
        if (depth > state.Limits.MaxDepth)
            return CaptureEntityResult.Rejected("Persistent inventory depth limit exceeded.");

        if (state.EntityIds.ContainsKey(uid))
            return CaptureEntityResult.Rejected("Persistent inventory entity is referenced more than once.");

        if (!TryComp(uid, out MetaDataComponent? metadata) || metadata.EntityPrototype is not { } prototype)
            return CaptureEntityResult.Rejected("Persistent inventory contains an entity without a prototype.");

        var policyError = ValidateEntityPolicy(uid, prototype, state.Policy, out var explicitlyDenied);
        if (policyError != null)
        {
            if (explicitlyDenied)
            {
                state.DeniedPrototypeIds.Add(prototype.ID);
                AddDeniedSubtree(uid, state);
                return CaptureEntityResult.Denied();
            }

            return CaptureEntityResult.Rejected(policyError);
        }

        if (state.EntityIds.Count >= state.Limits.MaxEntities)
            return CaptureEntityResult.Rejected("Persistent inventory entity limit exceeded.");

        var entityId = state.EntityIds.Count + 1;
        state.EntityIds.Add(uid, entityId);

        var componentStates = new List<PersistentInventoryComponentState>();
        foreach (var component in AllComps(uid)
                     .OrderBy(component => _componentFactory.GetComponentName(component.GetType()), StringComparer.Ordinal))
        {
            var componentId = _componentFactory.GetComponentName(component.GetType());
            if (_sanitizers.Any(sanitizer => sanitizer.CanOmit(uid, componentId, component)))
                continue;

            if (_adaptersByType.TryGetValue(component.GetType(), out var adapter))
            {
                if (!adapter.TryCapture(uid, component, out var componentState, out var error))
                    return CaptureEntityResult.Rejected(error ?? $"Cannot capture component {componentId}.");

                componentStates.Add(componentState);
                continue;
            }

            // Prototype-declared components are reconstructed from the same prototype.
            // Report only additional runtime components that would otherwise disappear
            // silently; known mutable prototype state must be represented by an adapter.
            if (!prototype.Components.ContainsKey(componentId))
                state.TrackOmitted(prototype.ID, componentId);
        }

        if (componentStates.Count > state.Limits.MaxComponentsPerEntity)
            return CaptureEntityResult.Rejected($"Entity {prototype.ID} has too many mutable components.");

        var children = new List<PersistentInventoryChild>();
        var persistedContainers = new List<(string Id, BaseContainer Container)>();
        if (TryComp(uid, out ContainerManagerComponent? containerManager))
        {
            foreach (var container in _containers.GetAllContainers(uid, containerManager))
            {
                var containerId = container.ID;
                if (ShouldPersistContainer(containerId))
                    persistedContainers.Add((containerId, container));
            }
        }

        foreach (var (containerId, container) in persistedContainers
                     .OrderBy(entry => entry.Id, StringComparer.Ordinal))
        {
            var persistedIndex = 0;
            for (var index = 0; index < container.ContainedEntities.Count; index++)
            {
                var childUid = container.ContainedEntities[index];
                var child = CaptureEntity(childUid, depth + 1, state);
                if (child.Status == CaptureEntityStatus.Rejected)
                    return child;

                if (child.Status == CaptureEntityStatus.Denied)
                    continue;

                PersistentInventoryStorageLocation? storageLocation = null;
                if (containerId == StorageComponent.ContainerId &&
                    TryComp(uid, out StorageComponent? storage) &&
                    storage.StoredItems.TryGetValue(childUid, out var location))
                {
                    storageLocation = new PersistentInventoryStorageLocation(
                        location.Position.X,
                        location.Position.Y,
                        (int) location.Direction);
                }

                children.Add(new PersistentInventoryChild(
                    containerId,
                    persistedIndex,
                    child.EntityId,
                    storageLocation));
                persistedIndex++;
            }
        }

        state.Entities.Add(new PersistentInventoryEntityState(
            entityId,
            prototype.ID,
            componentStates,
            children));
        return CaptureEntityResult.Success(entityId);
    }

    private string? ValidateEntityPolicy(
        EntityUid uid,
        EntityPrototype prototype,
        PersistentInventoryPolicyPrototype policy,
        out bool explicitlyDenied)
    {
        explicitlyDenied = HasComp<NoPersistentInventoryComponent>(uid);
        if (explicitlyDenied)
            return $"Prototype {prototype.ID} has NoPersistentInventory.";

        if (TryComp(uid, out PersistentInventoryItemComponent? overrideComponent) &&
            overrideComponent.Policy is { } overridePolicy &&
            !string.Equals(overridePolicy, policy.ID, StringComparison.Ordinal))
        {
            explicitlyDenied = true;
            return $"Prototype {prototype.ID} requires policy {overridePolicy}.";
        }

        var error = ValidatePrototypePolicy(prototype, policy);
        explicitlyDenied = error != null;
        return error;
    }

    private string? ValidatePrototypePolicy(
        EntityPrototype prototype,
        PersistentInventoryPolicyPrototype policy)
    {
        if (!prototype.Components.ContainsKey("Item"))
            return $"Prototype {prototype.ID} is not an item.";

        if (prototype.Components.ContainsKey("NoPersistentInventory"))
            return $"Prototype {prototype.ID} has NoPersistentInventory.";

        if (prototype.Components.TryGetValue("PersistentInventoryItem", out var policyRegistration) &&
            policyRegistration.Component is PersistentInventoryItemComponent itemPolicy &&
            itemPolicy.Policy is { } requiredPolicy &&
            !string.Equals(requiredPolicy, policy.ID, StringComparison.Ordinal))
        {
            return $"Prototype {prototype.ID} requires policy {requiredPolicy}.";
        }

        if (policy.DeniedPrototypes.Contains(prototype.ID) ||
            policy.DeniedPrototypePrefixes.Any(prefix =>
                prototype.ID.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Prototype {prototype.ID} is denied by policy.";
        }

        if (prototype.Categories.Any(category => DeniedCategoryIds.Contains(category.ID)))
            return $"Prototype {prototype.ID} belongs to a denied category.";

        foreach (var componentId in prototype.Components.Keys)
        {
            if (IsDeniedPolicyComponent(componentId))
                return $"Prototype {prototype.ID} has denied component {componentId}.";
        }

        return null;
    }

    private string? ValidateContainmentSafety(
        EntityUid uid,
        int depth,
        string path,
        PersistentInventoryLimits limits,
        HashSet<EntityUid> visited)
    {
        if (depth > limits.MaxDepth)
            return "Persistent inventory containment safety depth limit exceeded.";

        if (!visited.Add(uid))
            return null;

        if (visited.Count > limits.MaxEntities)
            return "Persistent inventory containment safety entity limit exceeded.";

        var prototypeId = TryComp(uid, out MetaDataComponent? metadata) &&
                          metadata.EntityPrototype is { } prototype
            ? prototype.ID
            : uid.ToString();
        var protectedComponent =
            TryComp(uid, out MindContainerComponent? mindContainer) && mindContainer.HasMind
                ? "MindContainer"
                : AllComps(uid)
                    .Select(component => _componentFactory.GetComponentName(component.GetType()))
                    .Order(StringComparer.Ordinal)
                    .FirstOrDefault(IsProtectedGraphComponent);
        if (protectedComponent != null)
        {
            return
                $"Нельзя сохранить профиль: внутри инвентаря найден защищённый мировой объект " +
                $"{prototypeId} с компонентом {protectedComponent} по пути {path}. " +
                $"Уберите игроков, живых мобов, занятые разумом носители, карты, шаттлы " +
                $"и другие мировые сущности из контейнеров. / " +
                $"Cannot save profile: protected world entity {prototypeId} with component " +
                $"{protectedComponent} was found at {path}.";
        }

        if (!TryComp(uid, out ContainerManagerComponent? containerManager))
            return null;

        foreach (var container in _containers.GetAllContainers(uid, containerManager)
                     .OrderBy(container => container.ID, StringComparer.Ordinal))
        {
            for (var index = 0; index < container.ContainedEntities.Count; index++)
            {
                var child = container.ContainedEntities[index];
                var error = ValidateContainmentSafety(
                    child,
                    depth + 1,
                    $"{path}/{container.ID}[{index}]",
                    limits,
                    visited);
                if (error != null)
                    return error;
            }
        }

        return null;
    }

    private static bool IsProtectedGraphComponent(string componentId)
    {
        return ProtectedGraphComponentIds.Contains(componentId) ||
               DeniedComponentPrefixes.Any(prefix =>
                   componentId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDeniedPolicyComponent(string componentId)
    {
        return DeniedComponentIds.Contains(componentId) ||
               DeniedComponentPrefixes.Any(prefix =>
                   componentId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearPrototypeGeneratedContents(
        IReadOnlyDictionary<int, EntityUid> spawned)
    {
        foreach (var uid in spawned
                     .OrderBy(pair => pair.Key)
                     .Select(pair => pair.Value))
        {
            if (!TryComp(uid, out ContainerManagerComponent? containerManager))
                continue;

            foreach (var container in _containers.GetAllContainers(uid, containerManager).ToArray())
            {
                var containerId = container.ID;
                if (!ShouldPersistContainer(containerId))
                    continue;

                foreach (var child in container.ContainedEntities.ToArray())
                    Del(child);
            }
        }
    }

    private bool TryRestoreChildren(
        EntityUid parent,
        IReadOnlyList<PersistentInventoryChild> children,
        IReadOnlyDictionary<int, EntityUid> spawned,
        out string? error)
    {
        error = null;
        foreach (var group in children
                     .GroupBy(child => child.ContainerId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(child => child.Index).ToArray();
            if (!_containers.TryGetContainer(parent, group.Key, out var container))
            {
                error = $"Cannot find container {group.Key}.";
                return false;
            }

            if (group.Key == StorageComponent.ContainerId &&
                TryComp(parent, out StorageComponent? storage))
            {
                Dictionary<int, PersistentInventoryStorageLocation>? legacyLocations = null;
                var locationCount = ordered.Count(child => child.StorageLocation != null);
                if (locationCount != 0 && locationCount != ordered.Length)
                {
                    error = $"Container {group.Key} has mixed legacy and positioned storage children.";
                    return false;
                }

                if (locationCount == 0 &&
                    !TryPlanLegacyStorageLocations(
                        (parent, storage),
                        ordered,
                        spawned,
                        out legacyLocations))
                {
                    error = $"Cannot find a valid storage layout for container {group.Key}.";
                    return false;
                }

                foreach (var child in ordered)
                {
                    var savedLocation = child.StorageLocation ?? legacyLocations![child.EntityId];
                    var location = new ItemStorageLocation(
                        ((Direction) savedLocation.Direction).ToAngle(),
                        new Vector2i(savedLocation.X, savedLocation.Y));
                    if (!_storage.InsertAt(
                            (parent, storage),
                            (spawned[child.EntityId], null),
                            location,
                            out _,
                            playSound: false,
                            stackAutomatically: false))
                    {
                        error = $"Cannot restore entity {child.EntityId} into container {group.Key}.";
                        return false;
                    }
                }

                continue;
            }

            if (ordered.Any(child => child.StorageLocation != null))
            {
                error = $"Container {group.Key} does not support storage-grid locations.";
                return false;
            }

            foreach (var child in ordered)
            {
                var childUid = spawned[child.EntityId];
                if (!_containers.Insert(childUid, container, force: true) ||
                    !container.Contains(childUid))
                {
                    error = $"Cannot restore entity {child.EntityId} into container {group.Key}.";
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryPlanLegacyStorageLocations(
        Entity<StorageComponent?> storage,
        IReadOnlyList<PersistentInventoryChild> children,
        IReadOnlyDictionary<int, EntityUid> spawned,
        out Dictionary<int, PersistentInventoryStorageLocation> locations)
    {
        var plannedLocations = new Dictionary<int, PersistentInventoryStorageLocation>();
        locations = plannedLocations;
        if (storage.Comp == null)
            return false;

        var candidates = new List<(PersistentInventoryChild Child, EntityUid Uid, ItemComponent Item, int Area)>();
        foreach (var child in children)
        {
            var uid = spawned[child.EntityId];
            if (!TryComp(uid, out ItemComponent? item))
                return false;

            candidates.Add((
                child,
                uid,
                item,
                _items.GetItemShape((uid, item)).GetArea()));
        }

        candidates.Sort((left, right) =>
        {
            var areaComparison = right.Area.CompareTo(left.Area);
            return areaComparison != 0
                ? areaComparison
                : left.Child.Index.CompareTo(right.Child.Index);
        });

        var bounds = storage.Comp.Grid.GetBoundingBox();
        var rotations = new[]
        {
            Angle.Zero,
            Angle.FromDegrees(90),
            Angle.FromDegrees(180),
            Angle.FromDegrees(270),
        };
        const int attemptLimit = 250_000;
        var attempts = 0;

        var success = Place(0);
        foreach (var candidate in candidates)
            storage.Comp.StoredItems.Remove(candidate.Uid);
        return success;

        bool Place(int candidateIndex)
        {
            if (candidateIndex == candidates.Count)
                return true;

            var candidate = candidates[candidateIndex];
            for (var y = bounds.Bottom; y <= bounds.Top; y++)
            {
                for (var x = bounds.Left; x <= bounds.Right; x++)
                {
                    foreach (var rotation in rotations)
                    {
                        if (++attempts > attemptLimit)
                            return false;

                        var location = new ItemStorageLocation(rotation, new Vector2i(x, y));
                        if (!_storage.ItemFitsInGridLocation(
                                (candidate.Uid, candidate.Item),
                                storage,
                                location))
                        {
                            continue;
                        }

                        storage.Comp.StoredItems[candidate.Uid] = location;
                        plannedLocations[candidate.Child.EntityId] =
                            new PersistentInventoryStorageLocation(
                                location.Position.X,
                                location.Position.Y,
                                (int) location.Direction);
                        if (Place(candidateIndex + 1))
                            return true;

                        storage.Comp.StoredItems.Remove(candidate.Uid);
                        plannedLocations.Remove(candidate.Child.EntityId);
                    }
                }
            }

            return false;
        }
    }

    private static PersistentInventoryPayload NormalizeLegacyStorageLocationsForComparison(
        PersistentInventoryPayload source,
        PersistentInventoryPayload restored)
    {
        var sourceEntities = source.Entities.ToDictionary(entity => entity.EntityId);
        var entities = restored.Entities.Select(restoredEntity =>
        {
            if (!sourceEntities.TryGetValue(restoredEntity.EntityId, out var sourceEntity))
                return restoredEntity;

            var sourceChildren = sourceEntity.Children.ToDictionary(
                child => (child.ContainerId, child.Index));
            return restoredEntity with
            {
                Children = restoredEntity.Children.Select(child =>
                {
                    if (sourceChildren.TryGetValue(
                            (child.ContainerId, child.Index),
                            out var sourceChild) &&
                        sourceChild.StorageLocation == null)
                    {
                        return child with { StorageLocation = null };
                    }

                    return child;
                }).ToArray(),
            };
        }).ToArray();
        return restored with { Entities = entities };
    }

    private static bool ShouldPersistContainer(string containerId)
    {
        return !containerId.StartsWith("solution@", StringComparison.Ordinal) &&
               !string.Equals(containerId, ActionsContainerComponent.ContainerId, StringComparison.Ordinal);
    }

    private static string DescribeRoundTripDifference(
        PersistentInventoryPayload source,
        PersistentInventoryPayload restored)
    {
        var sourceRoots = source.Roots
            .OrderBy(root => root.Kind)
            .ThenBy(root => root.Name, StringComparer.Ordinal)
            .ThenBy(root => root.EntityId)
            .ToArray();
        var restoredRoots = restored.Roots
            .OrderBy(root => root.Kind)
            .ThenBy(root => root.Name, StringComparer.Ordinal)
            .ThenBy(root => root.EntityId)
            .ToArray();
        if (!sourceRoots.SequenceEqual(restoredRoots))
            return "Persistent inventory isolated round-trip differs in root placement.";

        var sourceEntities = source.Entities.ToDictionary(entity => entity.EntityId);
        var restoredEntities = restored.Entities.ToDictionary(entity => entity.EntityId);
        foreach (var entityId in sourceEntities.Keys.Union(restoredEntities.Keys).Order())
        {
            if (!sourceEntities.TryGetValue(entityId, out var sourceEntity))
                return $"Persistent inventory isolated round-trip gained entity {entityId}.";
            if (!restoredEntities.TryGetValue(entityId, out var restoredEntity))
                return $"Persistent inventory isolated round-trip lost entity {entityId} ({sourceEntity.PrototypeId}).";
            if (!string.Equals(sourceEntity.PrototypeId, restoredEntity.PrototypeId, StringComparison.Ordinal))
                return $"Persistent inventory isolated round-trip changed prototype of entity {entityId}.";

            var sourceComponents = sourceEntity.Components.ToDictionary(
                component => component.ComponentId,
                StringComparer.Ordinal);
            var restoredComponents = restoredEntity.Components.ToDictionary(
                component => component.ComponentId,
                StringComparer.Ordinal);
            foreach (var componentId in sourceComponents.Keys
                         .Union(restoredComponents.Keys, StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                if (!sourceComponents.TryGetValue(componentId, out var sourceComponent))
                {
                    return $"Persistent inventory isolated round-trip gained component {componentId} " +
                           $"on entity {entityId} ({sourceEntity.PrototypeId}).";
                }

                if (!restoredComponents.TryGetValue(componentId, out var restoredComponent))
                {
                    return $"Persistent inventory isolated round-trip lost component {componentId} " +
                           $"on entity {entityId} ({sourceEntity.PrototypeId}).";
                }

                foreach (var fieldName in sourceComponent.Fields.Keys
                             .Union(restoredComponent.Fields.Keys, StringComparer.Ordinal)
                             .Order(StringComparer.Ordinal))
                {
                    if (!sourceComponent.Fields.TryGetValue(fieldName, out var sourceValue) ||
                        !restoredComponent.Fields.TryGetValue(fieldName, out var restoredValue) ||
                        !string.Equals(sourceValue, restoredValue, StringComparison.Ordinal))
                    {
                        return $"Persistent inventory isolated round-trip changed field {fieldName} " +
                               $"of component {componentId} on entity {entityId} ({sourceEntity.PrototypeId}).";
                    }
                }
            }

            var sourceChildren = sourceEntity.Children
                .OrderBy(child => child.ContainerId, StringComparer.Ordinal)
                .ThenBy(child => child.Index)
                .ThenBy(child => child.EntityId)
                .ToArray();
            var restoredChildren = restoredEntity.Children
                .OrderBy(child => child.ContainerId, StringComparer.Ordinal)
                .ThenBy(child => child.Index)
                .ThenBy(child => child.EntityId)
                .ToArray();
            if (!sourceChildren.SequenceEqual(restoredChildren))
            {
                return $"Persistent inventory isolated round-trip changed container placement " +
                       $"on entity {entityId} ({sourceEntity.PrototypeId}).";
            }
        }

        return "Persistent inventory isolated round-trip differs in payload metadata.";
    }

    private PersistentInventoryRestoreResult CleanupAndFail(
        IReadOnlyDictionary<int, EntityUid> spawned,
        string error)
    {
        foreach (var uid in spawned.OrderByDescending(pair => pair.Key).Select(pair => pair.Value))
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }

        return RestoreFailed(error);
    }

    private static PersistentInventoryCaptureResult Rejected(
        string error,
        IEnumerable<string>? denied = null)
    {
        return new PersistentInventoryCaptureResult(
            PersistentInventoryCaptureStatus.Rejected,
            null,
            denied?.Order(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>(),
            Array.Empty<EntityUid>(),
            Array.Empty<EntityUid>(),
            error);
    }

    private void AddDeniedSubtree(EntityUid uid, CaptureState state)
    {
        if (!state.DeniedEntities.Add(uid) ||
            !TryComp(uid, out ContainerManagerComponent? containerManager))
        {
            return;
        }

        foreach (var container in _containers.GetAllContainers(uid, containerManager))
        {
            foreach (var child in container.ContainedEntities)
                AddDeniedSubtree(child, state);
        }
    }

    private static PersistentInventoryRestoreResult RestoreFailed(string error)
    {
        return new PersistentInventoryRestoreResult(
            false,
            new Dictionary<int, EntityUid>(),
            Array.Empty<(PersistentInventoryRoot Root, EntityUid Entity)>(),
            error);
    }

    public readonly record struct CaptureRoot(
        PersistentInventoryRootKind Kind,
        string Name,
        EntityUid Entity);

    private sealed class CaptureState
    {
        public readonly PersistentInventoryPolicyPrototype Policy;
        public readonly PersistentInventoryLimits Limits;
        public readonly Dictionary<EntityUid, int> EntityIds = new();
        public readonly List<PersistentInventoryEntityState> Entities = new();
        public readonly HashSet<string> DeniedPrototypeIds = new(StringComparer.Ordinal);
        public readonly HashSet<EntityUid> DeniedEntities = new();
        public readonly Dictionary<(string PrototypeId, string ComponentId), int> OmittedComponents = new();

        public CaptureState(
            PersistentInventoryPolicyPrototype policy,
            PersistentInventoryLimits limits)
        {
            Policy = policy;
            Limits = limits;
        }

        public void TrackOmitted(string prototypeId, string componentId)
        {
            var key = (prototypeId, componentId);
            OmittedComponents[key] = OmittedComponents.GetValueOrDefault(key) + 1;
        }
    }

    private enum CaptureEntityStatus
    {
        Success,
        Denied,
        Rejected,
    }

    private readonly record struct CaptureEntityResult(
        CaptureEntityStatus Status,
        int EntityId,
        string? Error)
    {
        public static CaptureEntityResult Success(int entityId)
        {
            return new CaptureEntityResult(CaptureEntityStatus.Success, entityId, null);
        }

        public static CaptureEntityResult Denied()
        {
            return new CaptureEntityResult(CaptureEntityStatus.Denied, default, null);
        }

        public static CaptureEntityResult Rejected(string error)
        {
            return new CaptureEntityResult(CaptureEntityStatus.Rejected, default, error);
        }
    }

}
