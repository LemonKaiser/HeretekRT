using System.Numerics;
using Content.Server._WH40K.Activities.Components;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.Activities.Prototypes;
using Content.Shared.Doors.Systems;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Prying.Components;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Materializes the small hostile contacts allowed by stage four. It never loads a map, uses an
/// observer, spawns on a grid other than the generated terrain, targets a ship, or creates a
/// reinforcement. Any violation terminates the one instance and lets the director clean up only
/// entities marked with its id.
/// </summary>
public sealed class KoronusActivityHostileExecutorSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.25);
    private const int AnchorSpawnAttempts = 24;
    private const int HostileSpawnAttempts = 12;
    private const float AnchorFirstSpawnDistance = 20f;
    private const float AnchorSpawnDistanceStep = 4f;
    private const float HostileFirstSpawnDistance = 5f;
    private const float HostileSpawnDistanceStep = 1.25f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private ActivitySafetyGuard _safety = default!;
    [Dependency] private ActivityContentAudit _contentAudit = default!;
    [Dependency] private KoronusActivityDirectorSystem _director = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private TagSystem _tags = default!;

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusActivityThreatAnchorComponent, InteractHandEvent>(OnAnchorInteract);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        foreach (var instance in _director.GetActiveInstances())
        {
            if (!_prototypes.TryIndex<KoronusActivityTemplatePrototype>(instance.TemplateId, out var template) ||
                !KoronusActivityRuntimePolicy.IsHostileExecution(template.Execution))
            {
                continue;
            }

            if (instance.State == KoronusActivityState.Engaged)
            {
                MonitorThreat(instance, template);
                continue;
            }

            if (instance.State != KoronusActivityState.Available ||
                !TryGetReachedTarget(instance, template, out var target))
            {
                continue;
            }

            TryMaterializeThreat(instance, template, target);
        }
    }

    private void OnAnchorInteract(EntityUid uid, KoronusActivityThreatAnchorComponent anchor, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!IsLivingAttachedPlayer(args.User) ||
            !TryComp<TransformComponent>(args.User, out var userTransform) ||
            !TryComp<TransformComponent>(uid, out var anchorTransform) ||
            userTransform.MapID != anchorTransform.MapID)
        {
            return;
        }

        // Containment is the non-lethal alternative: a living crew member reaches the single
        // telegraphed anchor and seals it. The director then deletes the same owned group, so no
        // hostile stays behind even if the interaction races an AI tick.
        if (!_director.TryCompleteThreat(anchor.InstanceId, uid, KoronusActivityTerminalReason.Contained))
        {
            _popup.PopupCursor(Loc.GetString("koronus-activity-objective-unavailable"), args.User, PopupType.SmallCaution);
            return;
        }

        _popup.PopupCursor(Loc.GetString("koronus-activity-threat-contained"), args.User, PopupType.Medium);
    }

    private void TryMaterializeThreat(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        in ThreatTarget target)
    {
        if (!HasApprovedConfiguration(template) ||
            template.ObjectivePrototype is not { } anchorPrototype ||
            template.HostilePrototype is not { } hostilePrototype)
        {
            _director.TryTerminateThreat(instance.Id, KoronusActivityTerminalReason.ContentAuditFailed);
            return;
        }

        if (!TryFindSafeAnchorSpawn(instance, target, out var anchorMapCoordinates, out var anchorCoordinates))
        {
            _director.TryTerminateThreat(instance.Id, KoronusActivityTerminalReason.TargetUnreachable);
            return;
        }

        var spawned = new List<EntityUid>();
        var anchor = Spawn(anchorPrototype, anchorCoordinates);
        spawned.Add(anchor);
        if (!_contentAudit.TryValidateEntity(anchor, out _) ||
            !_safety.TryValidate(new KoronusActivityTarget(
                instance.SystemId,
                anchorMapCoordinates.MapId,
                anchorMapCoordinates.Position,
                target.TerrainGrid), out _))
        {
            AbortMaterialization(instance.Id, spawned, KoronusActivityTerminalReason.ContentAuditFailed);
            return;
        }

        AssignOwned(anchor, instance.Id, KoronusActivityOwnedKind.Entity);
        var threatAnchor = EnsureComp<KoronusActivityThreatAnchorComponent>(anchor);
        threatAnchor.InstanceId = instance.Id;
        threatAnchor.Execution = template.Execution;

        var hostileCount = GetHostileCount(instance.Id, template);
        for (var hostileIndex = 0; hostileIndex < hostileCount; hostileIndex++)
        {
            if (!TryFindSafeHostileSpawn(instance, target, anchorMapCoordinates.Position, hostileIndex, out var hostileCoordinates))
            {
                AbortMaterialization(instance.Id, spawned, KoronusActivityTerminalReason.TargetUnreachable);
                return;
            }

            var hostile = Spawn(hostilePrototype, hostileCoordinates);
            spawned.Add(hostile);
            if (!_contentAudit.TryValidateEntity(hostile, out _))
            {
                AbortMaterialization(instance.Id, spawned, KoronusActivityTerminalReason.ContentAuditFailed);
                return;
            }

            // Hormagaunts inherit tool and pry components. Activity hostiles must never acquire
            // access to a shuttle or protected facility, so remove those capabilities before the
            // AI can run; the tag blocks automatic door-bumping as well.
            RemComp<PryingComponent>(hostile);
            RemComp<ToolComponent>(hostile);
            _tags.RemoveTag(hostile, SharedDoorSystem.DoorBumpTag);
            AssignOwned(hostile, instance.Id, KoronusActivityOwnedKind.Npc);
            var hostileComponent = EnsureComp<KoronusActivityHostileComponent>(hostile);
            hostileComponent.InstanceId = instance.Id;
            hostileComponent.SystemId = instance.SystemId;
            hostileComponent.MapId = target.MapId;
            hostileComponent.AllowedGrid = target.TerrainGrid;
            hostileComponent.AnchorPosition = anchorMapCoordinates.Position;
            hostileComponent.LeashRadius = template.ThreatLeashRadius;

            // The final transform is rechecked after spawn. This makes prototype edits fail
            // closed instead of allowing an NPC to begin on a player ship or protected grid.
            if (!IsHostilePositionSafe(hostile, hostileComponent))
            {
                AbortMaterialization(instance.Id, spawned, KoronusActivityTerminalReason.SafetyViolation);
                return;
            }
        }

        var requiresSystemLease = KoronusActivityRuntimePolicy.RequiresSystemLease(template.Execution);
        if (_director.TryBeginThreat(instance.Id, anchor, template.Execution, requiresSystemLease))
            return;

        foreach (var entity in spawned)
            QueueDel(entity);
    }

    private void MonitorThreat(KoronusActivityRuntimeInstance instance, KoronusActivityTemplatePrototype template)
    {
        if (instance.Objective is not { } anchor || !Exists(anchor) ||
            !TryComp<TransformComponent>(anchor, out var anchorTransform))
        {
            TerminateThreat(instance.Id, KoronusActivityTerminalReason.OwnedEntityMissing);
            return;
        }

        var anchorMap = _transform.ToMapCoordinates(anchorTransform.Coordinates);
        if (!_safety.TryValidate(new KoronusActivityTarget(
                instance.SystemId,
                anchorMap.MapId,
                anchorMap.Position,
                anchorTransform.GridUid), out _))
        {
            TerminateThreat(instance.Id, KoronusActivityTerminalReason.SafetyViolation);
            return;
        }

        var aliveHostiles = 0;
        var hostiles = EntityQueryEnumerator<KoronusActivityHostileComponent>();
        while (hostiles.MoveNext(out var hostile, out var hostileComponent))
        {
            if (hostileComponent.InstanceId != instance.Id)
                continue;

            if (!IsHostilePositionSafe(hostile, hostileComponent))
            {
                TerminateThreat(instance.Id, KoronusActivityTerminalReason.SafetyViolation);
                return;
            }

            if (TryComp<MobStateComponent>(hostile, out var state) && state.CurrentState != MobState.Dead)
                aliveHostiles++;
        }

        if (aliveHostiles == 0)
        {
            _director.TryCompleteThreat(instance.Id, anchor, KoronusActivityTerminalReason.Defeated);
            return;
        }

        var participants = CountLivingParticipants(
            instance,
            template,
            anchorTransform.MapID,
            anchorMap.Position);
        if (participants >= template.MinimumLivingParticipants)
        {
            instance.LastThreatParticipantAt = _timing.CurTime;
            return;
        }

        var lastParticipantAt = instance.LastThreatParticipantAt ?? _timing.CurTime;
        if (_timing.CurTime >= lastParticipantAt + TimeSpan.FromSeconds(template.ThreatAbandonDelay))
            TerminateThreat(instance.Id, KoronusActivityTerminalReason.Evaded);
    }

    private void TerminateThreat(long instanceId, KoronusActivityTerminalReason reason)
    {
        DisableHostileAi(instanceId);
        _director.TryTerminateThreat(instanceId, reason);
    }

    private void DisableHostileAi(long instanceId)
    {
        var hostiles = EntityQueryEnumerator<KoronusActivityHostileComponent, HTNComponent>();
        while (hostiles.MoveNext(out var hostile, out var hostileComponent, out var htn))
        {
            if (hostileComponent.InstanceId == instanceId)
                _htn.SetHTNEnabled((hostile, htn), false);
        }
    }

    private static bool HasApprovedConfiguration(KoronusActivityTemplatePrototype template)
    {
        if (!KoronusActivityRuntimePolicy.IsHostileExecution(template.Execution) ||
            template.ObjectivePrototype is not { } anchor ||
            template.HostilePrototype is not { } hostile ||
            template.MinimumLivingParticipants < 1 ||
            template.MinimumHostiles < 1 ||
            template.MaximumHostiles < template.MinimumHostiles ||
            template.ParticipantRadius <= 0f ||
            template.ThreatLeashRadius <= 0f ||
            template.ThreatAbandonDelay < 0f)
        {
            return false;
        }

        return KoronusActivityRuntimePolicy.IsApprovedHostilePrototype(
            template.Execution,
            anchor.Id,
            hostile.Id);
    }

    private bool TryGetReachedTarget(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        out ThreatTarget target)
    {
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player ||
                !IsLivingAttachedPlayer(player) ||
                !TryComp<TransformComponent>(player, out var transform))
            {
                continue;
            }

            var mapCoordinates = _transform.ToMapCoordinates(transform.Coordinates);
            if (KoronusActivityRuntimePolicy.RequiresSurface(template.Execution))
            {
                if (!TryComp<KoronusPlanetSurfaceMapComponent>(
                        _maps.GetMapOrInvalid(transform.MapID), out var surface) ||
                    surface.SystemId != instance.SystemId ||
                    surface.TerrainGrid == EntityUid.Invalid)
                {
                    continue;
                }

                if (CountLivingParticipants(instance, template, transform.MapID, mapCoordinates.Position) <
                    template.MinimumLivingParticipants)
                {
                    continue;
                }

                target = new ThreatTarget(
                    transform.MapID,
                    mapCoordinates.Position,
                    surface.TerrainGrid,
                    surface.PlayableBounds);
                return true;
            }

            if (!_sector.TryGetSystemMap(instance.SystemId, out var systemMap) || systemMap != transform.MapID ||
                CountLivingParticipants(instance, template, transform.MapID, mapCoordinates.Position) <
                template.MinimumLivingParticipants)
            {
                continue;
            }

            target = new ThreatTarget(transform.MapID, mapCoordinates.Position, null, null);
            return true;
        }

        target = default;
        return false;
    }

    private int CountLivingParticipants(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        MapId mapId,
        Vector2 center)
    {
        var count = 0;
        var radiusSquared = template.ParticipantRadius * template.ParticipantRadius;
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

    private bool TryFindSafeAnchorSpawn(
        KoronusActivityRuntimeInstance instance,
        in ThreatTarget target,
        out MapCoordinates mapCoordinates,
        out EntityCoordinates coordinates)
    {
        for (var attempt = 0; attempt < AnchorSpawnAttempts; attempt++)
        {
            var angle = GetDeterministicUnit(instance.Id, attempt, 0xC01Du) * MathF.Tau;
            var distance = AnchorFirstSpawnDistance + AnchorSpawnDistanceStep * attempt +
                           GetDeterministicUnit(instance.Id, attempt, 0xA11Cu) * AnchorSpawnDistanceStep;
            var position = target.PlayerPosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            if (TryBuildSafeCoordinates(instance.SystemId, target, position, out mapCoordinates, out coordinates))
                return true;
        }

        mapCoordinates = MapCoordinates.Nullspace;
        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private bool TryFindSafeHostileSpawn(
        KoronusActivityRuntimeInstance instance,
        in ThreatTarget target,
        Vector2 anchorPosition,
        int hostileIndex,
        out EntityCoordinates coordinates)
    {
        for (var attempt = 0; attempt < HostileSpawnAttempts; attempt++)
        {
            var sequence = hostileIndex * HostileSpawnAttempts + attempt;
            var angle = GetDeterministicUnit(instance.Id, sequence, 0x9A11u) * MathF.Tau;
            var distance = HostileFirstSpawnDistance + HostileSpawnDistanceStep * attempt +
                           GetDeterministicUnit(instance.Id, sequence, 0xBC77u) * HostileSpawnDistanceStep;
            var position = anchorPosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            if (TryBuildSafeCoordinates(instance.SystemId, target, position, out _, out coordinates))
                return true;
        }

        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private bool TryBuildSafeCoordinates(
        string systemId,
        in ThreatTarget target,
        Vector2 position,
        out MapCoordinates mapCoordinates,
        out EntityCoordinates coordinates)
    {
        mapCoordinates = new MapCoordinates(position, target.MapId);
        coordinates = EntityCoordinates.Invalid;
        if (target.PlayableBounds is { } bounds && !bounds.Contains(position) ||
            HasForeignGrid(mapCoordinates, target.TerrainGrid) ||
            !_safety.TryValidate(new KoronusActivityTarget(systemId, target.MapId, position, target.TerrainGrid), out _))
        {
            return false;
        }

        coordinates = target.TerrainGrid is { } terrain
            ? _transform.ToCoordinates(terrain, mapCoordinates)
            : new EntityCoordinates(_maps.GetMapOrInvalid(target.MapId), position);
        return coordinates != EntityCoordinates.Invalid;
    }

    private bool IsHostilePositionSafe(EntityUid hostile, KoronusActivityHostileComponent component)
    {
        if (!TryComp<TransformComponent>(hostile, out var transform) ||
            transform.MapID != component.MapId ||
            transform.GridUid != component.AllowedGrid)
        {
            return false;
        }

        var coordinates = _transform.ToMapCoordinates(transform.Coordinates);
        return Vector2.DistanceSquared(coordinates.Position, component.AnchorPosition) <=
               component.LeashRadius * component.LeashRadius &&
               _safety.TryValidate(new KoronusActivityTarget(
                   component.SystemId,
                   coordinates.MapId,
                   coordinates.Position,
                   component.AllowedGrid), out _);
    }

    private bool HasForeignGrid(MapCoordinates coordinates, EntityUid? allowedGrid)
    {
        foreach (var grid in _lookup.GetEntitiesInRange<MapGridComponent>(coordinates, 2f))
        {
            if (grid.Owner != allowedGrid)
                return true;
        }

        return false;
    }

    private void AssignOwned(EntityUid entity, long instanceId, KoronusActivityOwnedKind kind)
    {
        var owned = EnsureComp<KoronusActivityOwnedComponent>(entity);
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

        _director.TryTerminateThreat(instanceId, reason);
    }

    private bool IsLivingAttachedPlayer(EntityUid entity)
    {
        if (HasComp<GhostComponent>(entity) ||
            !TryComp<MobStateComponent>(entity, out var mob) ||
            mob.CurrentState != MobState.Alive)
        {
            return false;
        }

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity == entity)
                return true;
        }

        return false;
    }

    private static int GetHostileCount(long instanceId, KoronusActivityTemplatePrototype template)
    {
        var span = template.MaximumHostiles - template.MinimumHostiles + 1;
        return template.MinimumHostiles + (int) (GetDeterministicValue(instanceId, 0xF00Du) % (uint) span);
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

    private readonly record struct ThreatTarget(
        MapId MapId,
        Vector2 PlayerPosition,
        EntityUid? TerrainGrid,
        Robust.Shared.Maths.Box2? PlayableBounds);
}
