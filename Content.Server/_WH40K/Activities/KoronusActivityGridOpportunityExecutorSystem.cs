using System.Numerics;
using System.Linq;
using Content.Server._WH40K.Activities.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.Activities.Prototypes;
using Content.Shared.Doors.Systems;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Prying.Components;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Creates the deliberately small orbital set pieces admitted in stage five. No external map is
/// loaded: the executor builds one static grid on the system map a living crew has already reached.
/// A grid never becomes a shuttle, FTL target, dock, player ship, or source of ghost roles.
/// </summary>
public sealed class KoronusActivityGridOpportunityExecutorSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);
    private const int SpawnAttempts = 24;
    private const float FirstSpawnDistance = 120f;
    private const float SpawnDistanceStep = 12f;
    private const float ProtectedAreaMargin = 18f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private ActivitySafetyGuard _safety = default!;
    [Dependency] private ActivityContentAudit _contentAudit = default!;
    [Dependency] private KoronusActivityDirectorSystem _director = default!;

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        foreach (var instance in _director.GetActiveInstances())
        {
            if (!_prototypes.TryIndex<KoronusActivityTemplatePrototype>(instance.TemplateId, out var template) ||
                !KoronusActivityRuntimePolicy.IsGridExecution(template.Execution) ||
                !TryGetProfile(template, out var profile))
            {
                continue;
            }

            if (instance.State == KoronusActivityState.Engaged)
            {
                MonitorGrid(instance, profile);
                continue;
            }

            if (instance.State == KoronusActivityState.Available &&
                TryGetReachedTarget(instance, template, out var context))
            {
                TryMaterialize(instance, template, profile, context);
            }
        }
    }

    private void TryMaterialize(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        in GridProfile profile,
        in ActivitySpawnContext context)
    {
        var spawned = new List<EntityUid>();
        if (!TryBuildGrid(instance, profile, context, spawned, out var grid))
        {
            AbortMaterialization(instance.Id, spawned, KoronusActivityTerminalReason.TargetUnreachable);
            return;
        }

        if (!_contentAudit.TryValidateStaticActivityGrid(grid, out _) ||
            !TryValidateGrid(grid, instance.SystemId, context.TargetMap, context.SpawnBand, profile.LeashRadius))
        {
            AbortMaterialization(instance.Id, spawned, KoronusActivityTerminalReason.SafetyViolation);
            return;
        }

        var objectiveCoordinates = new EntityCoordinates(
            grid,
            new Vector2(profile.Size.X / 2f + 0.5f, profile.Size.Y / 2f + 0.5f));
        var objective = Spawn(template.ObjectivePrototype!.Value, objectiveCoordinates);
        spawned.Add(objective);
        if (!_contentAudit.TryValidateEntity(objective, out _) ||
            !TryValidateOwnedEntity(objective, instance.SystemId, context.TargetMap, grid, profile.LeashRadius))
        {
            AbortMaterialization(instance.Id, spawned, KoronusActivityTerminalReason.ContentAuditFailed);
            return;
        }

        AssignOwned(objective, instance.Id, KoronusActivityOwnedKind.Entity);
        var activityObjective = EnsureComp<KoronusActivityObjectiveComponent>(objective);
        activityObjective.InstanceId = instance.Id;
        activityObjective.Execution = template.Execution;

        if (profile.RaiderCount > 0 &&
            !TrySpawnRaiders(instance, template, profile, context, grid, spawned))
        {
            DisableRaiderAi(instance.Id);
            AbortMaterialization(instance.Id, spawned, KoronusActivityTerminalReason.SafetyViolation);
            return;
        }

        if (_director.TryBeginObjective(instance.Id, objective, template.Execution, requiresSystemLease: true))
            return;

        DisableRaiderAi(instance.Id);
        foreach (var entity in spawned)
            QueueDel(entity);
    }

    private bool TryBuildGrid(
        KoronusActivityRuntimeInstance instance,
        in GridProfile profile,
        in ActivitySpawnContext context,
        List<EntityUid> spawned,
        out EntityUid grid)
    {
        grid = EntityUid.Invalid;
        var created = _mapManager.CreateGridEntity(context.TargetMap);
        grid = created.Owner;
        spawned.Add(grid);
        AssignOwned(grid, instance.Id, KoronusActivityOwnedKind.Grid);
        _transform.SetMapCoordinates(
            grid,
            new MapCoordinates(context.SpawnBand - new Vector2(profile.Size.X / 2f, profile.Size.Y / 2f), context.TargetMap));

        if (!TryComp<PhysicsComponent>(grid, out var physics))
            return false;

        _physics.SetLinearVelocity(grid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(grid, 0f, body: physics);
        _physics.SetBodyType(grid, BodyType.Static, body: physics);
        _physics.SetFixedRotation(grid, true, body: physics);

        var activityGrid = EnsureComp<KoronusActivityGridOpportunityComponent>(grid);
        activityGrid.InstanceId = instance.Id;
        activityGrid.SystemId = context.SystemId;
        activityGrid.MapId = context.TargetMap;
        activityGrid.SpawnPosition = context.SpawnBand;
        activityGrid.LeashRadius = profile.LeashRadius;

        var floor = _tiles["FloorSteel"].TileId;
        for (var x = 0; x < profile.Size.X; x++)
        {
            for (var y = 0; y < profile.Size.Y; y++)
            {
                _maps.SetTile(grid, created.Comp, new Vector2i(x, y), new Tile(floor));
                var border = x == 0 || x == profile.Size.X - 1 || y == 0 || y == profile.Size.Y - 1;
                var evaOpening = x == profile.Size.X / 2 && y == 0;
                if (!border || evaOpening)
                    continue;

                var wall = Spawn("WallSolid", new EntityCoordinates(grid, new Vector2(x + 0.5f, y + 0.5f)));
                spawned.Add(wall);
                AssignOwned(wall, instance.Id, KoronusActivityOwnedKind.Entity);
            }
        }

        return true;
    }

    private bool TrySpawnRaiders(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        in GridProfile profile,
        in ActivitySpawnContext context,
        EntityUid grid,
        List<EntityUid> spawned)
    {
        if (template.HostilePrototype is not { } hostilePrototype ||
            hostilePrototype.Id != "MobWH40KActivityRaiderDeckhand")
        {
            return false;
        }

        for (var index = 0; index < profile.RaiderCount; index++)
        {
            var local = new Vector2(2.5f + index * 2f, profile.Size.Y - 2.5f);
            var raider = Spawn(hostilePrototype, new EntityCoordinates(grid, local));
            spawned.Add(raider);
            if (!_contentAudit.TryValidateEntity(raider, out _))
                return false;

            // Inherited hostile bases may pry, use a tool, or bump doors. A cache raider is not
            // allowed to breach a visiting ship, and the set piece has no dockable door anyway.
            RemComp<PryingComponent>(raider);
            RemComp<ToolComponent>(raider);
            _tags.RemoveTag(raider, SharedDoorSystem.DoorBumpTag);
            AssignOwned(raider, instance.Id, KoronusActivityOwnedKind.Npc);
            if (!TryValidateOwnedEntity(raider, instance.SystemId, context.TargetMap, grid, profile.LeashRadius))
                return false;
        }

        return true;
    }

    private void MonitorGrid(KoronusActivityRuntimeInstance instance, in GridProfile profile)
    {
        if (!TryGetActivityGrid(instance.Id, out var grid, out var gridComponent) ||
            !TryValidateGrid(
                grid,
                instance.SystemId,
                gridComponent.MapId,
                gridComponent.SpawnPosition,
                profile.LeashRadius) ||
            instance.Objective is not { } objective ||
            !Exists(objective) ||
            !TryValidateOwnedEntity(objective, instance.SystemId, gridComponent.MapId, grid, profile.LeashRadius))
        {
            TerminateGrid(instance.Id, KoronusActivityTerminalReason.SafetyViolation);
            return;
        }

        var owned = EntityQueryEnumerator<KoronusActivityOwnedComponent>();
        while (owned.MoveNext(out var uid, out var marker))
        {
            if (marker.InstanceId != instance.Id || marker.Kind != KoronusActivityOwnedKind.Npc)
                continue;

            if (!TryValidateOwnedEntity(uid, instance.SystemId, gridComponent.MapId, grid, profile.LeashRadius))
            {
                TerminateGrid(instance.Id, KoronusActivityTerminalReason.SafetyViolation);
                return;
            }
        }
    }

    private bool TryGetReachedTarget(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        out ActivitySpawnContext context)
    {
        context = default;
        if (!_sector.TryGetSystemMap(instance.SystemId, out var mapId))
            return false;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player ||
                !IsLivingAttachedPlayer(player) ||
                !TryComp<TransformComponent>(player, out var transform) ||
                transform.MapID != mapId)
            {
                continue;
            }

            var position = _transform.ToMapCoordinates(transform.Coordinates).Position;
            if (CountLivingParticipants(mapId, position, template.ParticipantRadius) < template.MinimumLivingParticipants ||
                !TryFindSafeSpawn(instance, mapId, position, out var spawnBand))
            {
                continue;
            }

            context = new ActivitySpawnContext(
                mapId,
                instance.SystemId,
                spawnBand,
                instance.Id,
                GetDeterministicValue(instance.Id, 0x5EEDu));
            return true;
        }

        return false;
    }

    private bool TryFindSafeSpawn(
        KoronusActivityRuntimeInstance instance,
        MapId mapId,
        Vector2 playerPosition,
        out Vector2 position)
    {
        for (var attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            var angle = GetDeterministicUnit(instance.Id, attempt, 0xCA5Eu) * MathF.Tau;
            var distance = FirstSpawnDistance + SpawnDistanceStep * attempt +
                           GetDeterministicUnit(instance.Id, attempt, 0xB4ADu) * SpawnDistanceStep;
            var candidate = playerPosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            if (HasAnyGrid(new MapCoordinates(candidate, mapId), 36f) ||
                !IsProtectedBandClear(instance.SystemId, mapId, candidate))
            {
                continue;
            }

            position = candidate;
            return true;
        }

        position = default;
        return false;
    }

    private bool IsProtectedBandClear(string systemId, MapId mapId, Vector2 center)
    {
        foreach (var offset in new[]
                 {
                     Vector2.Zero,
                     Vector2.UnitX * ProtectedAreaMargin,
                     -Vector2.UnitX * ProtectedAreaMargin,
                     Vector2.UnitY * ProtectedAreaMargin,
                     -Vector2.UnitY * ProtectedAreaMargin,
                 })
        {
            if (!_safety.TryValidate(new KoronusActivityTarget(systemId, mapId, center + offset), out _))
                return false;
        }

        return true;
    }

    private bool TryGetActivityGrid(
        long instanceId,
        out EntityUid grid,
        out KoronusActivityGridOpportunityComponent component)
    {
        var grids = EntityQueryEnumerator<KoronusActivityGridOpportunityComponent>();
        while (grids.MoveNext(out var uid, out var candidate))
        {
            if (candidate.InstanceId == instanceId)
            {
                grid = uid;
                component = candidate;
                return true;
            }
        }

        grid = EntityUid.Invalid;
        component = default!;
        return false;
    }

    private bool TryValidateGrid(
        EntityUid grid,
        string systemId,
        MapId mapId,
        Vector2 spawnPosition,
        float leashRadius)
    {
        if (!TryComp<TransformComponent>(grid, out var transform) ||
            transform.MapID != mapId ||
            !TryComp<PhysicsComponent>(grid, out var physics) ||
            physics.BodyType != BodyType.Static ||
            !_contentAudit.TryValidateStaticActivityGrid(grid, out _))
        {
            return false;
        }

        var position = _transform.ToMapCoordinates(transform.Coordinates).Position;
        return Vector2.DistanceSquared(position, spawnPosition) <= 1f &&
               _safety.TryValidate(new KoronusActivityTarget(systemId, mapId, position, grid), out _) &&
               IsProtectedBandClear(systemId, mapId, position) &&
               !HasForeignGridWithin(grid, new MapCoordinates(position, mapId), leashRadius);
    }

    private bool TryValidateOwnedEntity(
        EntityUid uid,
        string systemId,
        MapId mapId,
        EntityUid grid,
        float leashRadius)
    {
        if (!TryComp<TransformComponent>(uid, out var transform) ||
            transform.MapID != mapId ||
            transform.GridUid != grid)
        {
            return false;
        }

        var position = _transform.ToMapCoordinates(transform.Coordinates).Position;
        return Vector2.DistanceSquared(position, _transform.ToMapCoordinates(Transform(grid).Coordinates).Position) <=
               leashRadius * leashRadius &&
               _safety.TryValidate(new KoronusActivityTarget(systemId, mapId, position, grid), out _);
    }

    private bool HasAnyGrid(MapCoordinates coordinates, float radius)
    {
        return _lookup.GetEntitiesInRange<MapGridComponent>(coordinates, radius).Any();
    }

    private bool HasForeignGridWithin(EntityUid ownGrid, MapCoordinates coordinates, float radius)
    {
        return _lookup.GetEntitiesInRange<MapGridComponent>(coordinates, radius).Any(grid => grid.Owner != ownGrid);
    }

    private int CountLivingParticipants(MapId mapId, Vector2 center, float radius)
    {
        var count = 0;
        var radiusSquared = radius * radius;
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player ||
                !IsLivingAttachedPlayer(player) ||
                !TryComp<TransformComponent>(player, out var transform) ||
                transform.MapID != mapId)
            {
                continue;
            }

            var position = _transform.ToMapCoordinates(transform.Coordinates).Position;
            if (Vector2.DistanceSquared(position, center) <= radiusSquared)
                count++;
        }

        return count;
    }

    private void TerminateGrid(long instanceId, KoronusActivityTerminalReason reason)
    {
        DisableRaiderAi(instanceId);
        _director.TryTerminateObjective(instanceId, reason);
    }

    private void DisableRaiderAi(long instanceId)
    {
        var npcs = EntityQueryEnumerator<KoronusActivityOwnedComponent, HTNComponent>();
        while (npcs.MoveNext(out var uid, out var marker, out var htn))
        {
            if (marker.InstanceId == instanceId && marker.Kind == KoronusActivityOwnedKind.Npc)
                _htn.SetHTNEnabled((uid, htn), false);
        }
    }

    private void AssignOwned(EntityUid uid, long instanceId, KoronusActivityOwnedKind kind)
    {
        var owned = EnsureComp<KoronusActivityOwnedComponent>(uid);
        owned.InstanceId = instanceId;
        owned.Kind = kind;
    }

    private void AbortMaterialization(
        long instanceId,
        List<EntityUid> spawned,
        KoronusActivityTerminalReason reason)
    {
        foreach (var entity in spawned)
            QueueDel(entity);

        _director.TryTerminateObjective(instanceId, reason);
    }

    private bool IsLivingAttachedPlayer(EntityUid entity)
    {
        if (HasComp<GhostComponent>(entity) ||
            !TryComp<MobStateComponent>(entity, out var mob) ||
            mob.CurrentState != MobState.Alive)
        {
            return false;
        }

        return _players.Sessions.Any(session => session.AttachedEntity == entity);
    }

    private static bool TryGetProfile(KoronusActivityTemplatePrototype template, out GridProfile profile)
    {
        profile = default;
        if (template.ObjectivePrototype is not { } objective ||
            !KoronusActivityRuntimePolicy.IsApprovedGridObjective(template.Execution, objective.Id) ||
            template.MinimumLivingParticipants < 1 ||
            template.ParticipantRadius <= 0f)
        {
            return false;
        }

        switch (template.Execution)
        {
            case KoronusActivityExecutionKind.OrbitSecureCacheGrid:
                if (template.HostilePrototype != null)
                    return false;
                profile = new GridProfile(new Vector2i(9, 7), 20f, 20f, 0);
                return true;
            case KoronusActivityExecutionKind.OrbitSalvageClusterGrid:
                if (template.HostilePrototype != null)
                    return false;
                profile = new GridProfile(new Vector2i(11, 7), 22f, 22f, 0);
                return true;
            case KoronusActivityExecutionKind.OrbitRescuePodGrid:
                if (template.HostilePrototype != null)
                    return false;
                profile = new GridProfile(new Vector2i(7, 5), 16f, 16f, 0);
                return true;
            case KoronusActivityExecutionKind.OrbitLostCargoGrid:
                if (template.HostilePrototype != null)
                    return false;
                profile = new GridProfile(new Vector2i(9, 5), 18f, 18f, 0);
                return true;
            case KoronusActivityExecutionKind.OrbitDerelictWreckGrid:
                if (template.HostilePrototype != null)
                    return false;
                profile = new GridProfile(new Vector2i(15, 9), 26f, 26f, 0);
                return true;
            case KoronusActivityExecutionKind.RaiderCutterCacheGrid:
                if (template.HostilePrototype is not { } hostile ||
                    hostile.Id != "MobWH40KActivityRaiderDeckhand" ||
                    template.MinimumLivingParticipants < 2 ||
                    template.MinimumHostiles != 2 ||
                    template.MaximumHostiles != 2)
                {
                    return false;
                }

                profile = new GridProfile(new Vector2i(13, 9), 24f, 24f, 2);
                return true;
            default:
                return false;
        }
    }

    private static float GetDeterministicUnit(long instanceId, int attempt, uint salt)
    {
        return GetDeterministicValue(instanceId, (uint) attempt * 0x9E3779B9u ^ salt) / (float) uint.MaxValue;
    }

    private static uint GetDeterministicValue(long instanceId, uint salt)
    {
        unchecked
        {
            uint value = (uint) instanceId;
            value ^= (uint) (instanceId >> 32);
            value ^= salt;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ value >> 16;
        }
    }

    private readonly record struct GridProfile(Vector2i Size, float ClearanceRadius, float LeashRadius, int RaiderCount);
}
