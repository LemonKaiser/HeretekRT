using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.Activities.Prototypes;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Materializes the small, opt-in physical objectives from stage three. It is deliberately
/// reactive: a marker cannot load or wake a remote map, create a grid, create an NPC, or offer a
/// ghost role. The first living player who lawfully reaches its map merely makes its one root
/// objective available.
/// </summary>
public sealed class KoronusActivityMaterializationSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private const int SpawnAttempts = 24;
    private const float FirstSpawnDistance = 14f;
    private const float SpawnDistanceStep = 4f;

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

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusActivityObjectiveComponent, InteractHandEvent>(OnInteractHand);
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
                !KoronusActivityRuntimePolicy.IsObjectiveExecution(template.Execution))
            {
                continue;
            }

            if (instance.State == KoronusActivityState.Engaged)
            {
                if (instance.Objective is { } objective && !Exists(objective))
                    _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.OwnedEntityMissing);

                continue;
            }

            if (instance.State != KoronusActivityState.Available ||
                !TryGetReachedTarget(instance, template, out var target))
            {
                continue;
            }

            TryMaterialize(instance, template, target);
        }
    }

    private void OnInteractHand(EntityUid uid, KoronusActivityObjectiveComponent component, InteractHandEvent args)
    {
        // Authored set-piece recoverables use their own extraction gates: items resolve only
        // after reaching a living player's hand and the rescue patient only after a completed
        // carry action. Consuming their initial hand interaction here would archive the location
        // and delete the recoverable before either gate can run.
        if (args.Handled ||
            !KoronusActivityRuntimePolicy.IsObjectiveExecution(component.Execution) &&
            !KoronusActivityRuntimePolicy.IsGridExecution(component.Execution))
        {
            return;
        }

        // A player must be attached to a living body. This rejects regular observers, admin
        // ghosts and any unowned NPC even if an interaction reaches the server.
        if (!IsLivingAttachedPlayer(args.User))
        {
            args.Handled = true;
            return;
        }

        args.Handled = true;
        if (!_director.TryCompleteObjective(component.InstanceId, uid))
        {
            _popup.PopupCursor(Loc.GetString("koronus-activity-objective-unavailable"), args.User, PopupType.SmallCaution);
            return;
        }

        _popup.PopupCursor(Loc.GetString("koronus-activity-objective-completed"), args.User, PopupType.Medium);
    }

    private void TryMaterialize(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        in ReachedTarget target)
    {
        if (template.ObjectivePrototype is not { } prototype ||
            !TryFindSafeSpawn(instance, target, out var mapCoordinates, out var coordinates))
        {
            _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.TargetUnreachable);
            return;
        }

        var root = Spawn(prototype, coordinates);
        if (!_contentAudit.TryValidateEntity(root, out _))
        {
            QueueDel(root);
            _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.ContentAuditFailed);
            return;
        }

        // The map and grid are checked before the spawn and again from the final map position.
        // A future prototype change therefore cannot silently move an objective into Footfall or a
        // protected facility.
        if (!_safety.TryValidate(
                new KoronusActivityTarget(instance.SystemId, mapCoordinates.MapId, mapCoordinates.Position, target.TerrainGrid),
                out _))
        {
            QueueDel(root);
            _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.SafetyViolation);
            return;
        }

        var owned = EnsureComp<KoronusActivityOwnedComponent>(root);
        owned.InstanceId = instance.Id;
        owned.Kind = KoronusActivityOwnedKind.Entity;
        var objective = EnsureComp<KoronusActivityObjectiveComponent>(root);
        objective.InstanceId = instance.Id;
        objective.Execution = template.Execution;

        var requiresSystemLease = KoronusActivityRuntimePolicy.RequiresSystemLease(template.Execution);
        if (_director.TryBeginObjective(instance.Id, root, template.Execution, requiresSystemLease))
            return;

        // No lifecycle state has accepted this root, therefore it has no right to persist.
        QueueDel(root);
    }

    private bool TryGetReachedTarget(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        out ReachedTarget target)
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
            switch (template.Execution)
            {
                case KoronusActivityExecutionKind.SurfaceVoxBeacon:
                case KoronusActivityExecutionKind.SurfaceRescueFlare:
                    if (!TryComp<KoronusPlanetSurfaceMapComponent>(
                            _maps.GetMapOrInvalid(transform.MapID), out var surface) ||
                        surface.SystemId != instance.SystemId ||
                        surface.TerrainGrid == EntityUid.Invalid)
                    {
                        continue;
                    }

                    target = new ReachedTarget(player, transform.MapID, mapCoordinates.Position, surface.TerrainGrid, surface.PlayableBounds);
                    return true;

                case KoronusActivityExecutionKind.OrbitAuspexProbe:
                    if (!_sector.TryGetSystemMap(instance.SystemId, out var systemMap) || systemMap != transform.MapID)
                        continue;

                    target = new ReachedTarget(player, transform.MapID, mapCoordinates.Position, null, null);
                    return true;
            }
        }

        target = default;
        return false;
    }

    private bool TryFindSafeSpawn(
        KoronusActivityRuntimeInstance instance,
        in ReachedTarget target,
        out MapCoordinates mapCoordinates,
        out EntityCoordinates coordinates)
    {
        mapCoordinates = MapCoordinates.Nullspace;
        coordinates = EntityCoordinates.Invalid;

        for (var attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            var angle = GetDeterministicUnit(instance.Id, attempt, 0xA11Cu) * MathF.Tau;
            var distance = FirstSpawnDistance + SpawnDistanceStep * attempt +
                           GetDeterministicUnit(instance.Id, attempt, 0xBEEFu) * SpawnDistanceStep;
            var position = target.PlayerPosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            if (target.PlayableBounds is { } bounds && !bounds.Contains(position))
                continue;

            var candidate = new MapCoordinates(position, target.MapId);
            if (HasForeignGrid(candidate, target.TerrainGrid) ||
                !_safety.TryValidate(new KoronusActivityTarget(
                    instance.SystemId,
                    target.MapId,
                    position,
                    target.TerrainGrid), out _))
            {
                continue;
            }

            mapCoordinates = candidate;
            coordinates = target.TerrainGrid is { } terrain
                ? _transform.ToCoordinates(terrain, candidate)
                : new EntityCoordinates(_maps.GetMapOrInvalid(target.MapId), position);
            return coordinates != EntityCoordinates.Invalid;
        }

        return false;
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

    private static float GetDeterministicUnit(long instanceId, int attempt, uint salt)
    {
        unchecked
        {
            uint value = (uint) instanceId;
            value ^= (uint) (instanceId >> 32);
            value ^= (uint) attempt * 0x9E3779B9u;
            value ^= salt;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value / (float) uint.MaxValue;
        }
    }

    private readonly record struct ReachedTarget(
        EntityUid Player,
        MapId MapId,
        Vector2 PlayerPosition,
        EntityUid? TerrainGrid,
        Box2? PlayableBounds);
}
