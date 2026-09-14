using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Carrying;
using Content.Server.Shuttles.Components;
using Content.Server._WH40K.Activities.Components;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.Activities.Prototypes;
using Content.Shared.Carrying;
using Content.Shared.Ghost;
using Content.Shared.Hands;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Materializes the nine authored Koronus locations admitted by stages two through five. The
/// executor never reads a path, system, surface, NPC, docking target, role or reward from a
/// prototype: all of those boundaries are fixed by its small code catalogue and audited again
/// after loading. Only one fixed profile is permitted to retain one receive-only docking port.
/// </summary>
public sealed class KoronusActivitySetPieceExecutorSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);
    private const int SpawnAttempts = 24;
    private const float ProtectedAreaMargin = 18f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private ActivitySafetyGuard _safety = default!;
    [Dependency] private ActivityContentAudit _contentAudit = default!;
    [Dependency] private KoronusActivityDirectorSystem _director = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusActivityObjectiveComponent, GotEquippedHandEvent>(OnObjectiveEquipped);
        SubscribeLocalEvent<KoronusActivityRecoveryPatientComponent, CarryDoAfterEvent>(OnPatientCarried,
            after: [typeof(CarryingSystem)]);
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
                !TryGetProfile(template, out var profile))
            {
                continue;
            }

            if (instance.State == KoronusActivityState.Engaged)
            {
                MonitorSetPiece(instance, profile);
                continue;
            }

            if (instance.State == KoronusActivityState.Available &&
                TryGetReachedTarget(instance, template, profile, out var context))
            {
                TryMaterialize(instance, template, profile, context);
            }
        }
    }

    private void OnObjectiveEquipped(
        EntityUid uid,
        KoronusActivityObjectiveComponent component,
        GotEquippedHandEvent args)
    {
        if (!KoronusActivityRuntimePolicy.IsSetPieceExecution(component.Execution) ||
            !IsLivingAttachedPlayer(args.User))
        {
            return;
        }

        if (!_director.TryCompleteExtractedObjective(component.InstanceId, uid))
            return;

        _popup.PopupCursor(Loc.GetString("koronus-activity-objective-completed"), args.User, PopupType.Medium);
    }

    private void OnPatientCarried(
        EntityUid uid,
        KoronusActivityRecoveryPatientComponent component,
        CarryDoAfterEvent args)
    {
        // CarryingSystem handles this event first. A cancelled attempt, or a synthetic event that
        // did not actually put the patient in the living player's care, cannot resolve a rescue.
        if (args.Cancelled || !args.Handled || component.InstanceId == 0 || !IsLivingAttachedPlayer(args.Args.User))
            return;

        if (!_director.TryCompleteExtractedObjective(component.InstanceId, uid))
            return;

        component.InstanceId = 0;
        _popup.PopupCursor(Loc.GetString("koronus-activity-rescue-extracted"), args.Args.User, PopupType.Medium);
    }

    private void TryMaterialize(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        in SetPieceProfile profile,
        in SetPieceSpawnContext context)
    {
        if (!TryBuildSetPiece(instance, profile, context, out var grid))
        {
            _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.ContentAuditFailed);
            return;
        }

        TryCompleteMaterialization(instance, template, profile, context, grid);
    }

    private void TryCompleteMaterialization(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        in SetPieceProfile profile,
        in SetPieceSpawnContext context,
        EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComponent) ||
            !TryValidateSetPiece(grid, instance.SystemId, profile, context) ||
            !TryFindObjectivePosition(grid, gridComponent, out var objectivePosition))
        {
            QueueDel(grid);
            _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.SafetyViolation);
            return;
        }

        var objective = Spawn(template.ObjectivePrototype!.Value,
            new EntityCoordinates(grid, objectivePosition));
        if (!_contentAudit.TryValidateSetPieceEntity(objective, out _) ||
            !TryValidateOwnedEntity(objective, instance.SystemId, context.MapId, grid, context.Center,
                profile.LeashRadius))
        {
            QueueDel(grid);
            _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.ContentAuditFailed);
            return;
        }

        AssignOwned(objective, instance.Id, KoronusActivityOwnedKind.Entity);
        var activityObjective = EnsureComp<KoronusActivityObjectiveComponent>(objective);
        activityObjective.InstanceId = instance.Id;
        activityObjective.Execution = template.Execution;
        if (TryComp<KoronusActivityRecoveryPatientComponent>(objective, out var patient))
            patient.InstanceId = instance.Id;

        if (_director.TryBeginObjective(instance.Id, objective, template.Execution, requiresSystemLease: true))
            return;

        QueueDel(grid);
    }

    private bool TryBuildSetPiece(
        KoronusActivityRuntimeInstance instance,
        in SetPieceProfile profile,
        in SetPieceSpawnContext context,
        out EntityUid grid)
    {
        grid = EntityUid.Invalid;
        if (!_mapLoader.TryLoadGrid(context.MapId, profile.MapPath, out var loadedGrid, offset: context.Center))
            return false;

        grid = loadedGrid.Value.Owner;
        return TryPrepareSetPieceGrid(grid, loadedGrid.Value.Comp, instance.Id, profile, context);
    }

    private bool TryPrepareSetPieceGrid(
        EntityUid grid,
        MapGridComponent gridComponent,
        long instanceId,
        in SetPieceProfile profile,
        in SetPieceSpawnContext context)
    {
        if (!TryComp<PhysicsComponent>(grid, out var physics))
        {
            QueueDel(grid);
            return false;
        }

        _physics.SetLinearVelocity(grid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(grid, 0f, body: physics);
        _physics.SetBodyType(grid, BodyType.Static, body: physics);
        _physics.SetFixedRotation(grid, true, body: physics);

        if (!_contentAudit.TrySanitizeLoadedSetPieceGrid(grid, out _) ||
            !_contentAudit.TrySanitizeImportedSetPieceContents(grid, out _) ||
            !TryClaimImportedContents(grid, instanceId) ||
            !TryCenterSetPieceGrid(grid, gridComponent, context.Center) ||
            !TryBuildDockingPort(grid, gridComponent, instanceId, profile))
        {
            QueueDel(grid);
            return false;
        }

        AssignOwned(grid, instanceId, KoronusActivityOwnedKind.Grid);
        var setPiece = EnsureComp<KoronusActivitySetPieceComponent>(grid);
        setPiece.InstanceId = instanceId;
        setPiece.SystemId = context.SystemId;
        setPiece.MapId = context.MapId;
        setPiece.SpawnPosition = context.Center;
        setPiece.LeashRadius = profile.LeashRadius;
        setPiece.AllowedSurfaceGrid = context.SurfaceTerrainGrid;
        return true;
    }

    private bool TryCenterSetPieceGrid(EntityUid grid, MapGridComponent gridComponent, Vector2 center)
    {
        if (gridComponent.LocalAABB.Size.LengthSquared() <= 0f)
            return false;

        _transform.SetWorldPosition(grid, center - gridComponent.LocalAABB.Center);
        return true;
    }

    private bool TryClaimImportedContents(EntityUid grid, long instanceId)
    {
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform))
        {
            // Imported maps are always static. The one profile that offers docking adds its
            // collar only after this audit and only through the fixed code path below.
            if (transform.GridUid != grid && transform.ParentUid != grid)
                continue;

            if (!TryValidateImportedEntity(uid, allowsDocking: false, out _))
                return false;

            AssignOwned(uid, instanceId, KoronusActivityOwnedKind.Entity);
        }

        return true;
    }

    private bool TryBuildDockingPort(
        EntityUid grid,
        MapGridComponent gridComponent,
        long instanceId,
        in SetPieceProfile profile)
    {
        if (!profile.AllowsDocking)
            return true;

        if (!TryOpenBottomDockingTile(grid, gridComponent, out var position))
            return false;

        // This is intentionally a code-selected prototype rather than map-authored content.
        // It is the only exception to the static-map audit, and it is placed only after a floor
        // tile exists below it so normal anchoring and docking rules still apply.
        var port = Spawn("KoronusAivoriusSurveyCutterDockingPort", new EntityCoordinates(grid, position));
        if (!_contentAudit.TryValidateDockableSetPieceEntity(port, out _))
        {
            QueueDel(port);
            return false;
        }

        AssignOwned(port, instanceId, KoronusActivityOwnedKind.Entity);
        return true;
    }

    /// <summary>
    /// The stock dropship has a sealed hull. The receive-only collar replaces one bottom-facing
    /// hull blocker over an existing floor tile, reproducing a real exterior airlock rather than
    /// placing a dock in the middle of a room. No map-authored docking component is retained.
    /// </summary>
    private bool TryOpenBottomDockingTile(EntityUid grid, MapGridComponent gridComponent, out Vector2 position)
    {
        position = default;
        var candidateIndex = default(Vector2i);
        var bestScore = float.MaxValue;
        var tiles = _maps.GetAllTilesEnumerator(grid, gridComponent);
        while (tiles.MoveNext(out var tileRef))
        {
            if (tileRef == null || tileRef.Value.Tile.IsEmpty || _turf.IsSpace(tileRef.Value))
                continue;

            var below = tileRef.Value.GridIndices + new Vector2i(0, -1);
            if (_maps.TryGetTileRef(grid, gridComponent, below, out var belowTile) && !belowTile.Tile.IsEmpty)
                continue;

            var candidate = _maps.ToCenterCoordinates(tileRef.Value, gridComponent).Position;
            var score = MathF.Abs(candidate.X - gridComponent.LocalAABB.Center.X);
            if (score >= bestScore)
                continue;

            bestScore = score;
            candidateIndex = tileRef.Value.GridIndices;
            position = candidate;
        }

        if (bestScore == float.MaxValue)
            return false;

        var blockers = new List<EntityUid>();
        var transforms = EntityQueryEnumerator<TransformComponent>();
        while (transforms.MoveNext(out var uid, out var transform))
        {
            if (transform.GridUid == grid &&
                Vector2.DistanceSquared(transform.Coordinates.Position, position) < 0.01f &&
                HasComp<PhysicsComponent>(uid))
            {
                blockers.Add(uid);
            }
        }

        foreach (var blocker in blockers)
            Del(blocker);

        return _maps.TryGetTileRef(grid, gridComponent, candidateIndex, out var candidateTile) &&
               !_turf.IsTileBlocked(candidateTile, CollisionGroup.MobMask);
    }

    private bool TryFindObjectivePosition(EntityUid grid, MapGridComponent gridComponent, out Vector2 position)
    {
        return TryFindFreeFloorPosition(grid, gridComponent, preferEdge: false, out position);
    }

    /// <summary>
    /// Stock ruins do not share a stable local origin. Objectives therefore select a real
    /// traversable tile nearest the authored layout's centre. The single docking collar uses the
    /// dedicated bottom-hull opening routine above instead.
    /// </summary>
    private bool TryFindFreeFloorPosition(
        EntityUid grid,
        MapGridComponent gridComponent,
        bool preferEdge,
        out Vector2 position)
    {
        position = default;
        var bestScore = float.MaxValue;
        var found = false;
        var tiles = _maps.GetAllTilesEnumerator(grid, gridComponent);
        while (tiles.MoveNext(out var tileRef))
        {
            if (tileRef == null || tileRef.Value.Tile.IsEmpty || _turf.IsSpace(tileRef.Value) ||
                _turf.IsTileBlocked(tileRef.Value, CollisionGroup.MobMask))
            {
                continue;
            }

            var candidate = _maps.ToCenterCoordinates(tileRef.Value, gridComponent).Position;
            var score = Vector2.DistanceSquared(candidate, gridComponent.LocalAABB.Center);
            if (preferEdge)
            {
                var bounds = gridComponent.LocalAABB;
                var edgeDistance = MathF.Min(
                    MathF.Min(candidate.X - bounds.Left, bounds.Right - candidate.X),
                    MathF.Min(candidate.Y - bounds.Bottom, bounds.Top - candidate.Y));
                score += edgeDistance * edgeDistance * 256f;
            }

            if (score >= bestScore)
                continue;

            bestScore = score;
            position = candidate;
            found = true;
        }

        if (found)
            return true;

        position = default;
        return false;
    }

    private void MonitorSetPiece(KoronusActivityRuntimeInstance instance, in SetPieceProfile profile)
    {
        if (!TryGetSetPieceGrid(instance.Id, out var grid, out var component) ||
            !TryValidateSetPiece(grid, instance.SystemId, profile,
                new SetPieceSpawnContext(component.MapId, component.SystemId, component.SpawnPosition,
                    component.AllowedSurfaceGrid)) ||
            (profile.AllowsDocking && !HasExactlyOneApprovedDockingPort(grid)) ||
            instance.Objective is not { } objective ||
            !Exists(objective) ||
            !TryValidateOwnedEntity(objective, instance.SystemId, component.MapId, grid,
                component.SpawnPosition, profile.LeashRadius))
        {
            _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.SafetyViolation);
            return;
        }

        var owned = EntityQueryEnumerator<KoronusActivityOwnedComponent>();
        while (owned.MoveNext(out var uid, out var marker))
        {
            if (marker.InstanceId != instance.Id || marker.Kind == KoronusActivityOwnedKind.Grid)
                continue;

            if (!TryValidateImportedEntity(uid, profile.AllowsDocking, out _) ||
                !TryValidateOwnedEntity(uid, instance.SystemId, component.MapId, grid,
                    component.SpawnPosition, profile.LeashRadius))
            {
                _director.TryTerminateObjective(instance.Id, KoronusActivityTerminalReason.SafetyViolation);
                return;
            }
        }
    }

    private bool TryGetReachedTarget(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        in SetPieceProfile profile,
        out SetPieceSpawnContext context)
    {
        context = default;
        if (!KoronusActivityRuntimePolicy.TryGetSetPieceSystem(template.Execution, out var expectedSystem) ||
            expectedSystem != instance.SystemId)
        {
            return false;
        }

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player ||
                !IsLivingAttachedPlayer(player) ||
                !TryComp<TransformComponent>(player, out var transform))
            {
                continue;
            }

            var position = _transform.ToMapCoordinates(transform.Coordinates).Position;
            EntityUid? terrainGrid = null;
            Box2? bounds = null;
            if (profile.SurfaceId == null)
            {
                if (!_sector.TryGetSystemMap(instance.SystemId, out var systemMap) || transform.MapID != systemMap)
                    continue;
            }
            else
            {
                var mapUid = _maps.GetMapOrInvalid(transform.MapID);
                if (!TryComp<KoronusPlanetSurfaceMapComponent>(mapUid, out var surface) ||
                    surface.SystemId != instance.SystemId ||
                    surface.SurfaceId != profile.SurfaceId)
                {
                    continue;
                }

                terrainGrid = surface.TerrainGrid;
                bounds = surface.PlayableBounds;
            }

            if (CountLivingParticipants(transform.MapID, position, template.ParticipantRadius) <
                template.MinimumLivingParticipants ||
                !TryFindSafeSpawn(instance, profile, transform.MapID, position, terrainGrid, bounds, out var center))
            {
                continue;
            }

            context = new SetPieceSpawnContext(transform.MapID, instance.SystemId, center, terrainGrid);
            return true;
        }

        return false;
    }

    private bool TryFindSafeSpawn(
        KoronusActivityRuntimeInstance instance,
        in SetPieceProfile profile,
        MapId mapId,
        Vector2 playerPosition,
        EntityUid? allowedTerrainGrid,
        Box2? bounds,
        out Vector2 center)
    {
        for (var attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            var angle = GetDeterministicUnit(instance.Id, attempt, 0xC0DEu) * MathF.Tau;
            var distance = profile.FirstSpawnDistance + profile.SpawnDistanceStep * attempt +
                           GetDeterministicUnit(instance.Id, attempt, 0x51CEu) * profile.SpawnDistanceStep;
            var candidate = playerPosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            if (!IsInsideBounds(candidate, profile.ClearanceSize, bounds) ||
                HasForeignGrid(new MapCoordinates(candidate, mapId), profile.ClearanceRadius, allowedTerrainGrid) ||
                !IsProtectedBandClear(instance.SystemId, mapId, candidate))
            {
                continue;
            }

            center = candidate;
            return true;
        }

        center = default;
        return false;
    }

    private bool TryValidateSetPiece(
        EntityUid grid,
        string systemId,
        in SetPieceProfile profile,
        in SetPieceSpawnContext context)
    {
        if (!TryComp<TransformComponent>(grid, out var transform) ||
            transform.MapID != context.MapId ||
            !TryComp<MapGridComponent>(grid, out var gridComponent) ||
            !TryComp<PhysicsComponent>(grid, out var physics) ||
            physics.BodyType != BodyType.Static ||
            !_contentAudit.TryValidateStaticActivityGrid(grid, out _))
        {
            return false;
        }

        var expectedOrigin = context.Center - gridComponent.LocalAABB.Center;
        var actualOrigin = _transform.ToMapCoordinates(transform.Coordinates).Position;
        return Vector2.DistanceSquared(actualOrigin, expectedOrigin) <= 1f &&
               _safety.TryValidate(new KoronusActivityTarget(systemId, context.MapId, context.Center, grid), out _) &&
               IsProtectedBandClear(systemId, context.MapId, context.Center) &&
               !HasForeignGrid(new MapCoordinates(context.Center, context.MapId), profile.LeashRadius,
                   context.SurfaceTerrainGrid, grid);
    }

    private bool TryValidateOwnedEntity(
        EntityUid uid,
        string systemId,
        MapId mapId,
        EntityUid grid,
        Vector2 center,
        float leashRadius)
    {
        if (!TryComp<TransformComponent>(uid, out var transform) ||
            transform.MapID != mapId ||
            transform.GridUid != grid)
        {
            return false;
        }

        var position = _transform.ToMapCoordinates(transform.Coordinates).Position;
        return Vector2.DistanceSquared(position, center) <= leashRadius * leashRadius &&
               _safety.TryValidate(new KoronusActivityTarget(systemId, mapId, position, grid), out _);
    }

    private bool TryValidateImportedEntity(
        EntityUid uid,
        bool allowsDocking,
        out KoronusActivityRejectReason reason)
    {
        return allowsDocking
            ? _contentAudit.TryValidateDockableSetPieceEntity(uid, out reason)
            : _contentAudit.TryValidateSetPieceEntity(uid, out reason);
    }

    private bool HasExactlyOneApprovedDockingPort(EntityUid grid)
    {
        var ports = 0;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform))
        {
            if ((transform.GridUid != grid && transform.ParentUid != grid) || !HasComp<DockingComponent>(uid))
                continue;

            if (!_contentAudit.TryValidateDockableSetPieceEntity(uid, out _) || ++ports > 1)
                return false;
        }

        return ports == 1;
    }

    private bool TryGetSetPieceGrid(
        long instanceId,
        out EntityUid grid,
        out KoronusActivitySetPieceComponent component)
    {
        var grids = EntityQueryEnumerator<KoronusActivitySetPieceComponent>();
        while (grids.MoveNext(out var uid, out var candidate))
        {
            if (candidate.InstanceId != instanceId)
                continue;

            grid = uid;
            component = candidate;
            return true;
        }

        grid = EntityUid.Invalid;
        component = default!;
        return false;
    }

    private bool HasForeignGrid(
        MapCoordinates coordinates,
        float radius,
        EntityUid? allowedTerrainGrid,
        EntityUid? allowedGrid = null)
    {
        return _lookup.GetEntitiesInRange<MapGridComponent>(coordinates, radius)
            .Any(grid => grid.Owner != allowedTerrainGrid && grid.Owner != allowedGrid);
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

    private void AssignOwned(EntityUid uid, long instanceId, KoronusActivityOwnedKind kind)
    {
        var owned = EnsureComp<KoronusActivityOwnedComponent>(uid);
        owned.InstanceId = instanceId;
        owned.Kind = kind;
    }

    private static bool IsInsideBounds(Vector2 center, Vector2i size, Box2? bounds)
    {
        if (bounds == null)
            return true;

        var half = new Vector2(size.X / 2f, size.Y / 2f);
        return bounds.Value.Contains(center - half) && bounds.Value.Contains(center + half);
    }

    private static bool TryGetProfile(KoronusActivityTemplatePrototype template, out SetPieceProfile profile)
    {
        profile = default;
        if (!KoronusActivityRuntimePolicy.IsSetPieceExecution(template.Execution) ||
            template.ObjectivePrototype is not { } objective ||
            !KoronusActivityRuntimePolicy.IsApprovedSetPieceObjective(template.Execution, objective.Id) ||
            template.HostilePrototype != null ||
            template.MinimumLivingParticipants < 1 ||
            template.ParticipantRadius <= 0f)
        {
            return false;
        }

        switch (template.Execution)
        {
            case KoronusActivityExecutionKind.AivoriusBlackBoxSetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/chunked_tcomms.yml"),
                    null,
                    new Vector2i(48, 48),
                    50f,
                    46f,
                    120f,
                    12f);
                return true;
            case KoronusActivityExecutionKind.FoulstoneSurveySetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/old_ai_sat.yml"),
                    "FoulstoneSurface",
                    new Vector2i(56, 56),
                    110f,
                    120f,
                    48f,
                    8f);
                return true;
            case KoronusActivityExecutionKind.SottosIceRescueSetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/djstation.yml"),
                    "SottosTombIceSurface",
                    new Vector2i(56, 56),
                    110f,
                    120f,
                    48f,
                    8f);
                return true;
            case KoronusActivityExecutionKind.AivoriusCargoSetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/syndicate_dropship.yml"),
                    null,
                    new Vector2i(56, 56),
                    60f,
                    54f,
                    120f,
                    12f);
                return true;
            case KoronusActivityExecutionKind.FoulstoneMineshaftSetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/old_ai_sat.yml"),
                    "FoulstoneSurface",
                    new Vector2i(64, 64),
                    110f,
                    120f,
                    52f,
                    8f);
                return true;
            case KoronusActivityExecutionKind.SottosColdRelaySetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/old_ai_sat.yml"),
                    "SottosTombIceSurface",
                    new Vector2i(56, 56),
                    110f,
                    120f,
                    52f,
                    8f);
                return true;
            case KoronusActivityExecutionKind.FoulstoneAssayAnnexSetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/old_ai_sat.yml"),
                    "FoulstoneSurface",
                    new Vector2i(64, 64),
                    110f,
                    120f,
                    56f,
                    8f);
                return true;
            case KoronusActivityExecutionKind.SottosCryoArchiveSetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/djstation.yml"),
                    "SottosTombIceSurface",
                    new Vector2i(64, 64),
                    110f,
                    120f,
                    56f,
                    8f);
                return true;
            case KoronusActivityExecutionKind.AivoriusSurveyCutterDockSetPiece:
                profile = new SetPieceProfile(
                    new ResPath("/Maps/Ruins/syndicate_dropship.yml"),
                    null,
                    new Vector2i(56, 56),
                    60f,
                    54f,
                    120f,
                    12f,
                    AllowsDocking: true);
                return true;
            default:
                return false;
        }
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

    private readonly record struct SetPieceSpawnContext(
        MapId MapId,
        string SystemId,
        Vector2 Center,
        EntityUid? SurfaceTerrainGrid);

    private readonly record struct SetPieceProfile(
        ResPath MapPath,
        string? SurfaceId,
        Vector2i ClearanceSize,
        float ClearanceRadius,
        float LeashRadius,
        float FirstSpawnDistance,
        float SpawnDistanceStep,
        bool AllowsDocking = false);
}
