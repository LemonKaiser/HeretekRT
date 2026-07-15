using System.Numerics;
using Content.Server.Power.EntitySystems; // Mono
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._Mono.Ships;
using Content.Shared._WH40K.SectorMap.BUI;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Events;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Shuttles.UI.MapObjects;
using Content.Shared.Station.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private KoronusSectorResidencySystem _koronusResidency = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const float ShuttleFTLRange = 512f;
    private const float ShuttleFTLMassThreshold = 100f; // Mono: now a soft limit, ships under the limit just stop you from shorter distance

    private const float MassConstant = 50f; // Arbitrary, at this value massMultiplier = 0.65
    private const float MassMultiplierMin = 0.5f;
    private const float MassMultiplierMax = 5f;
    private readonly Dictionary<EntityUid, KoronusPendingJump> _pendingKoronusJumps = new();

    private sealed record KoronusPendingJump(string SourceSystem, string TargetSystem);

    private void InitializeFTL()
    {
        SubscribeLocalEvent<FTLBeaconComponent, ComponentStartup>(OnBeaconStartup);
        SubscribeLocalEvent<FTLBeaconComponent, AnchorStateChangedEvent>(OnBeaconAnchorChanged);

        SubscribeLocalEvent<FTLExclusionComponent, ComponentStartup>(OnExclusionStartup);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnKoronusEntityTerminating);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnKoronusRoundRestart);
    }

    private void OnExclusionStartup(Entity<FTLExclusionComponent> ent, ref ComponentStartup args)
    {
        RefreshShuttleConsoles();
    }

    private void OnBeaconStartup(Entity<FTLBeaconComponent> ent, ref ComponentStartup args)
    {
        RefreshShuttleConsoles();
    }

    private void OnBeaconAnchorChanged(Entity<FTLBeaconComponent> ent, ref AnchorStateChangedEvent args)
    {
        RefreshShuttleConsoles();
    }

    private void OnBeaconFTLMessage(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleFTLBeaconMessage args)
    {
        var beaconEnt = GetEntity(args.Beacon);
        if (!_xformQuery.TryGetComponent(beaconEnt, out var targetXform))
        {
            return;
        }

        var nCoordinates = new NetCoordinates(GetNetEntity(targetXform.ParentUid), targetXform.LocalPosition);
        if (targetXform.ParentUid == EntityUid.Invalid)
        {
            nCoordinates = new NetCoordinates(GetNetEntity(beaconEnt), targetXform.LocalPosition);
        }

        // Check target exists
        if (!_shuttle.CanFTLBeacon(nCoordinates))
        {
            return;
        }

        var angle = args.Angle.Reduced();
        var targetCoordinates = new EntityCoordinates(targetXform.MapUid!.Value, _transform.GetWorldPosition(targetXform));

        ConsoleFTL(ent, targetCoordinates, angle, targetXform.MapID);
    }

    private void OnPositionFTLMessage(Entity<ShuttleConsoleComponent> entity, ref ShuttleConsoleFTLPositionMessage args)
    {
        var mapUid = _mapSystem.GetMap(args.Coordinates.MapId);

        // If it's beacons only block all position messages.
        if (!Exists(mapUid) || _shuttle.IsBeaconMap(mapUid))
        {
            return;
        }

        var targetCoordinates = new EntityCoordinates(mapUid, args.Coordinates.Position);
        var angle = args.Angle.Reduced();
        ConsoleFTL(entity, targetCoordinates, angle, args.Coordinates.MapId);
    }

    private void OnSectorJumpMessage(Entity<ShuttleConsoleComponent> entity, ref KoronusSectorJumpMessage args)
    {
        var consoleUid = GetDroneConsole(entity.Owner);
        if (consoleUid == null || !_xformQuery.TryGetComponent(consoleUid.Value, out var consoleTransform) ||
            consoleTransform.GridUid is not { } shuttleUid ||
            !_xformQuery.TryGetComponent(shuttleUid, out var shuttleTransform))
        {
            return;
        }

        if (!_koronusSector.TryGetSystemId(shuttleTransform.MapID, out var sourceSystem) ||
            !_koronusSector.TryGetSystemPrototype(args.TargetSystemId, out var targetSystem) ||
            !targetSystem.Enabled ||
            sourceSystem == targetSystem.ID ||
            !_koronusSector.HasWarpRoute(sourceSystem, targetSystem.ID) ||
            !_koronusSector.TryEnsureSystemMapLoaded(targetSystem.ID, out var targetMap))
        {
            _popup.PopupEntity(Loc.GetString("koronus-sector-jump-denied"), entity.Owner, PopupType.Medium);
            return;
        }

        var targetMapUid = _mapSystem.GetMapOrInvalid(targetMap);
        if (!Exists(targetMapUid))
        {
            _popup.PopupEntity(Loc.GetString("koronus-sector-jump-denied"), entity.Owner, PopupType.Medium);
            return;
        }

        ConsoleFTL(
            entity,
            new EntityCoordinates(targetMapUid, GetKoronusArrivalPosition(targetSystem, shuttleUid)),
            Angle.Zero,
            targetMap,
            sectorJumpTarget: targetSystem.ID,
            sectorJumpSource: sourceSystem);
    }

    private Vector2 GetKoronusArrivalPosition(KoronusSystemPrototype targetSystem, EntityUid shuttleUid)
    {
        if (!TryComp<MapGridComponent>(shuttleUid, out var grid) ||
            !TryComp<PhysicsComponent>(shuttleUid, out var physics))
        {
            return targetSystem.NavigationCenter;
        }

        return GetKoronusArrivalPosition(
            targetSystem,
            grid.LocalAABB,
            physics.LocalCenter,
            _random.NextAngle());
    }

    internal static Vector2 GetKoronusArrivalPosition(
        KoronusSystemPrototype targetSystem,
        Box2 localBounds,
        Vector2 physicsLocalCenter,
        Angle angle)
    {
        var gridHalfDiagonal = (localBounds.TopRight - localBounds.Center).Length();
        var safeRadius = MathF.Max(0f, targetSystem.BoundaryRadius - MathF.Max(5f, gridHalfDiagonal));
        var arrivalDistance = Math.Clamp(targetSystem.ArrivalDistance, 0f, safeRadius);
        var desiredGridCenter = targetSystem.NavigationCenter +
                                angle.RotateVec(Vector2.UnitX * arrivalDistance);

        // ConsoleFTL later converts the requested physics centre into the grid transform origin.
        // Compensate for asymmetric grids so their AABB centre, not world zero or an arbitrary local
        // origin, is the point guaranteed to remain inside the authored system boundary.
        return desiredGridCenter - localBounds.Center + physicsLocalCenter;
    }

    private void GetBeacons(ref List<ShuttleBeaconObject>? beacons)
    {
        var beaconQuery = AllEntityQuery<FTLBeaconComponent>();

        while (beaconQuery.MoveNext(out var destUid, out _))
        {
            var meta = _metaQuery.GetComponent(destUid);
            var name = meta.EntityName;

            if (string.IsNullOrEmpty(name))
                name = Loc.GetString("shuttle-console-unknown");

            // Can't travel to same map (yet)
            var destXform = _xformQuery.GetComponent(destUid);
            beacons ??= new List<ShuttleBeaconObject>();
            beacons.Add(new ShuttleBeaconObject(GetNetEntity(destUid), GetNetCoordinates(destXform.Coordinates), name));
        }
    }

    private void GetExclusions(ref List<ShuttleExclusionObject>? exclusions)
    {
        var query = AllEntityQuery<FTLExclusionComponent, TransformComponent>();

        while (query.MoveNext(out var comp, out var xform))
        {
            if (!comp.Enabled)
                continue;

            exclusions ??= new List<ShuttleExclusionObject>();
            exclusions.Add(new ShuttleExclusionObject(GetNetCoordinates(xform.Coordinates), comp.Range, Loc.GetString("shuttle-console-exclusion")));
        }
    }

    /// <summary>
    /// Handles shuttle console FTLs.
    /// </summary>
    private void ConsoleFTL(
        Entity<ShuttleConsoleComponent> ent,
        EntityCoordinates targetCoordinates,
        Angle targetAngle,
        MapId targetMap,
        string? sectorJumpTarget = null,
        string? sectorJumpSource = null)
    {
        var consoleUid = GetDroneConsole(ent.Owner);

        if (consoleUid == null)
            return;

        var shuttleUid = _xformQuery.GetComponent(consoleUid.Value).GridUid;

        if (shuttleUid == null || !TryComp(shuttleUid.Value, out ShuttleComponent? shuttleComp))
            return;

        if (shuttleComp.Enabled == false)
            return;

        // Check shuttle can even FTL
        if (!_shuttle.CanFTL(shuttleUid.Value, out var reason))
        {
            // TODO: Session popup
            return;
        }

        // Check shuttle can FTL to this target.
        if (sectorJumpTarget == null && !_shuttle.CanFTLTo(shuttleUid.Value, targetMap, ent))
        {
            return;
        }

        if (sectorJumpTarget == null)
            targetCoordinates = _shuttle.ClampCoordinatesToFTLRange(shuttleUid.Value, targetCoordinates);

        List<ShuttleExclusionObject>? exclusions = null;
        GetExclusions(ref exclusions);

        if (!_shuttle.FTLFree(shuttleUid.Value, targetCoordinates, targetAngle, exclusions))
        {
            return;
        }

        if (!TryComp(shuttleUid.Value, out PhysicsComponent? shuttlePhysics))
        {
            return;
        }

        // Check for nearby grids that are above the mass threshold
        var xform = Transform(shuttleUid.Value);
        var bounds = xform.WorldMatrix.TransformBox(Comp<MapGridComponent>(shuttleUid.Value).LocalAABB).Enlarged(ShuttleFTLRange);
        var bodyQuery = GetEntityQuery<PhysicsComponent>();
        // Keep track of docked grids to exclude them from the proximity check
        var dockedGrids = new HashSet<EntityUid>();

        // Find all docked grids by looking for DockingComponents on the shuttle
        _shuttle.GetAllDockedShuttlesIgnoringFTLLock(shuttleUid.Value, dockedGrids);

        // Mono
        foreach (var (console, consoleComp) in _lookup.GetEntitiesInRange<ShuttleConsoleComponent>(_transform.GetMapCoordinates(xform), ShuttleFTLRange))
        {
            var consoleXform = Transform(console);
            var consGrid = consoleXform.GridUid;
            if (consGrid == null ||
                consGrid == shuttleUid ||
                dockedGrids.Contains(consGrid.Value) || // Skip grids that are docked to us or to the same parent grid
                !bodyQuery.TryGetComponent(consGrid, out var body) ||
                body.Mass < ShuttleFTLMassThreshold
                    && (_transform.GetWorldPosition(consGrid.Value) - _transform.GetWorldPosition(consoleXform)).Length() > ShuttleFTLRange * body.Mass / ShuttleFTLMassThreshold ||
                !this.IsPowered(console, EntityManager))
            {
                continue;
            }

            _popup.PopupEntity(Loc.GetString("shuttle-ftl-proximity"), ent.Owner, PopupType.Medium);
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg"), ent.Owner);
            UpdateConsoles(shuttleUid.Value);
            return;
        }

        // Client sends the "adjusted" coordinates and we adjust it back to get the actual transform coordinates.
        var adjustedCoordinates = targetCoordinates.Offset(targetAngle.RotateVec(-shuttlePhysics.LocalCenter));

        if (!IsInsideKoronusSystemBoundary(shuttleUid.Value, adjustedCoordinates, targetAngle, targetMap))
        {
            _popup.PopupEntity(Loc.GetString("koronus-boundary-target-denied"), ent.Owner, PopupType.Medium);
            return;
        }

        var tagEv = new FTLTagEvent();
        RaiseLocalEvent(shuttleUid.Value, ref tagEv);

        var ev = new ShuttleConsoleFTLTravelStartEvent(ent.Owner);
        RaiseLocalEvent(ref ev);
        if (_shuttle.TryGetFTLDrive(shuttleUid.Value, out _, out var drive)) // Mono Begin
        {
            if (sectorJumpTarget != null && !_koronusResidency.BeginIncomingSectorJump(sectorJumpTarget))
            {
                _popup.PopupEntity(Loc.GetString("koronus-sector-jump-denied"), ent.Owner, PopupType.Medium);
                return;
            }

            MassAdjustFTLStart(shuttlePhysics,
                drive,
                out var massAdjustedStartupTime,
                out var massAdjustedHyperSpaceTime);

            if (sectorJumpSource != null && sectorJumpTarget != null)
            {
                massAdjustedHyperSpaceTime *=
                    _koronusSector.GetWarpTravelTimeMultiplier(sectorJumpSource, sectorJumpTarget);
            }

            _shuttle.FTLToCoordinates(shuttleUid.Value, shuttleComp, adjustedCoordinates, targetAngle, massAdjustedStartupTime, massAdjustedHyperSpaceTime);

            if (sectorJumpTarget == null)
                return;

            if (sectorJumpSource != null && HasComp<FTLComponent>(shuttleUid.Value))
            {
                _pendingKoronusJumps[shuttleUid.Value] = new KoronusPendingJump(sectorJumpSource, sectorJumpTarget);
                // FTLToCoordinates refreshes once before the Koronus context exists. Refresh again so the
                // sector UI immediately receives the departure/destination pair for the travel animation.
                UpdateConsoles(shuttleUid.Value);
            }
            else
                _koronusResidency.EndIncomingSectorJump(sectorJumpTarget);
        }
    }

    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        ReleaseKoronusJumpReservation(args.Entity);
    }

    private void OnKoronusEntityTerminating(ref EntityTerminatingEvent args)
    {
        // MapGridComponent already has an exclusive termination subscriber in GridDeletionContainerSystem.
        // The reservation dictionary is keyed by shuttle grid, so the common event is precise without that subscription.
        ReleaseKoronusJumpReservation(args.Entity.Owner);
    }

    private void OnKoronusRoundRestart(RoundRestartCleanupEvent args)
    {
        foreach (var jump in _pendingKoronusJumps.Values)
        {
            _koronusResidency.EndIncomingSectorJump(jump.TargetSystem);
        }

        _pendingKoronusJumps.Clear();
    }

    /// <summary>
    /// Called by ShuttleSystem's exclusive FTL shutdown subscription when a jump is interrupted.
    /// </summary>
    public void ReleaseKoronusJumpReservation(EntityUid shuttleUid)
    {
        if (_pendingKoronusJumps.Remove(shuttleUid, out var jump))
            _koronusResidency.EndIncomingSectorJump(jump.TargetSystem);
    }

    /// <summary>
    /// Keeps the Koronus sector map available during the technical FTL-map phase.
    /// </summary>
    private KoronusSectorInterfaceState GetKoronusSectorState(
        EntityUid shuttleUid,
        MapId shuttleMap,
        ShuttleMapInterfaceState mapState)
    {
        if (_pendingKoronusJumps.TryGetValue(shuttleUid, out var jump) &&
            mapState.FTLState is FTLState.Starting or FTLState.Travelling or FTLState.Arriving)
        {
            var travel = new KoronusSectorTravelState(
                jump.SourceSystem,
                jump.TargetSystem,
                mapState.FTLState,
                mapState.FTLTime);
            return _koronusSector.GetInterfaceState(jump.SourceSystem, false, travel);
        }

        return _koronusSector.GetInterfaceState(shuttleMap, mapState.FTLState == FTLState.Available);
    }

    private bool IsInsideKoronusSystemBoundary(
        EntityUid shuttleUid,
        EntityCoordinates targetCoordinates,
        Angle targetAngle,
        MapId targetMap)
    {
        var mapUid = _mapSystem.GetMapOrInvalid(targetMap);
        if (!TryComp<KoronusSystemBoundaryComponent>(mapUid, out var boundary))
            return true;

        var target = _transform.ToMapCoordinates(targetCoordinates);
        if (target.MapId != targetMap || !TryComp<MapGridComponent>(shuttleUid, out var grid))
            return false;

        var gridCenter = target.Position + targetAngle.RotateVec(grid.LocalAABB.Center);
        var gridHalfDiagonal = (grid.LocalAABB.TopRight - grid.LocalAABB.Center).Length();
        var safeRadius = boundary.Radius - MathF.Max(5f, gridHalfDiagonal);
        return safeRadius > 0f &&
               (gridCenter - boundary.Origin).LengthSquared() <= safeRadius * safeRadius;
    }

    // Mono Begin
    private void MassAdjustFTLStart(PhysicsComponent shuttlePhysics, FTLDriveComponent drive, out float massAdjustedStartupTime, out float massAdjustedHyperSpaceTime)
    {
        if (drive.MassAffectedDrive == false)
        {
            massAdjustedHyperSpaceTime = drive.HyperSpaceTime;
            massAdjustedStartupTime = drive.StartupTime;
            return;
        }
        var adjustedMass = shuttlePhysics.Mass * drive.DriveMassMultiplier;
        var massMultiplier = float.Log(float.Sqrt(adjustedMass / MassConstant + float.E));
        massMultiplier = float.Clamp(massMultiplier, MassMultiplierMin, MassMultiplierMax);
        massAdjustedStartupTime = drive.StartupTime * massMultiplier;
        massAdjustedHyperSpaceTime = drive.HyperSpaceTime * massMultiplier;
    }
    // Mono End
    private void UpdateConsoles(EntityUid uid, ShuttleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        // Update pilot consoles
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();

        while (query.MoveNext(out var consoleUid, out var console, out var xform))
        {
            if (xform.GridUid != uid)
                continue;

            UpdateConsoleState(consoleUid, console);
        }
    }

    private void UpdateConsoleState(EntityUid uid, ShuttleConsoleComponent component)
    {
        UpdateState(uid);
    }
}
