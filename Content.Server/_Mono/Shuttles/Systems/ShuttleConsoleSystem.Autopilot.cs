using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._Mono.NPC.HTN.Operators;
using Content.Server._Mono.Shuttles.Components;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Shuttles;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Mono.Shuttles;

public sealed partial class ShuttleConsoleAutopilotSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MoverController _mover = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private ShipSteeringSystem _steering = default!;

    private readonly HashSet<Entity<ShuttleConsoleComponent>> _gridConsoles = new();
    private List<Entity<MapGridComponent>> _intersectingGrids = new();

    private const float AutoDockSearchRange = 300f;
    private const float AutoDockClearance = 12f;
    private const float AutopilotGridAnchorClearance = 16f;
    private const float AutoDockCaptureRange = 5f;
    private const float AutoDockCaptureMaxRelativeSpeed = 2.5f;
    private const float AutoDockCaptureMaxRelativeAngularSpeed = 0.2f;
    private const float TerminalMaxRelativeSpeed = 2.5f;
    private const float TerminalMaxLateralSpeed = 1.25f;
    private const float TerminalAlignmentClosingSpeed = 0.75f;
    private const float TerminalMaxRelativeAngularSpeed = 0.25f;
    private const float TerminalMaxLinearAcceleration = 2f;
    private const float TerminalMaxAngularAcceleration = 0.75f;
    private const float TerminalLateralAlignmentTolerance = 0.35f;
    private const float TerminalAngularAlignmentTolerance = 0.1f;
    private const float TerminalPositionTolerance = 0.075f;
    private const float TerminalControlPositionTolerance = 0.015f;
    private const float TerminalRotationTolerance = 0.00873f;
    private const float TerminalControlRotationTolerance = 0.0025f;
    private const float TerminalRelativeSpeedTolerance = 0.08f;
    private const float TerminalRelativeAngularSpeedTolerance = 0.02f;
    private const float TerminalMaximumDuration = 60f;
    private const byte TerminalRequiredStableTicks = 3;
    private const float AutoDockRotationClearanceStep = MathF.PI / 36f;
    private const float AutoDockTerminalClearanceStep = 0.5f;
    private const float AutoDockContactTolerance = 0.02f;

    private enum AutoDockTargetSearchResult : byte
    {
        NoTarget,
        NoCompatiblePorts,
        Found,
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleConsoleComponent, ShuttleConsoleAutopilotPositionMessage>(OnAutopilotMessage);
        SubscribeLocalEvent<ShuttleConsoleComponent, ShuttleConsoleAutopilotGridMessage>(OnAutopilotGridMessage);
        SubscribeLocalEvent<ShuttleConsoleComponent, ShuttleConsoleAutoDockRequestMessage>(OnAutoDockRequest);
        SubscribeLocalEvent<ShuttleConsoleComponent, ToggleAutoDockRequestMessage>(OnToggleAutoDock);
        SubscribeLocalEvent<ShuttleConsoleComponent, SteeringDoneEvent>(OnSteeringDone);
        SubscribeLocalEvent<ShuttleConsoleAutoDockingComponent, GetShuttleInputsEvent>(OnTerminalAutoDockGetInputs);
        SubscribeLocalEvent<ShuttleConsoleAutoDockingComponent, PilotedShuttleRelayedEvent<StartCollideEvent>>(OnTerminalAutoDockCollision);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShuttleConsoleComponent, ShuttleConsoleAutoDockingComponent>();
        while (query.MoveNext(out var uid, out var console, out var autoDock))
        {
            switch (autoDock.Phase)
            {
                case AutoDockPhase.Approach:
                    if (autoDock.CollisionDetected)
                    {
                        CancelAutoDock((uid, console));
                        _steering.Stop(uid);
                        PopupAutoDockError(uid, "shuttle-console-auto-dock-failed");
                        break;
                    }

                    TryCaptureAutoDockApproach((uid, console), autoDock);
                    break;
                case AutoDockPhase.TerminalApproach:
                    UpdateTerminalAutoDock((uid, console), autoDock, frameTime);
                    break;
            }
        }
    }

    private void OnAutopilotMessage(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleAutopilotPositionMessage args)
    {
        if (RejectPlanetaryAutopilot(ent))
            return;

        CancelAutoDock(ent);
        if (SetAutopilotTarget(ent, args.Coordinates, args.Angle))
        {
            var shuttleGrid = Transform(ent).GridUid;
            if (shuttleGrid != null)
                CancelAutonomousControlOnGrid(shuttleGrid.Value, ent.Owner);

            UndockForAutopilot(shuttleGrid);
        }
    }

    private void OnAutopilotGridMessage(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleAutopilotGridMessage args)
    {
        if (RejectPlanetaryAutopilot(ent))
            return;

        if (!TryGetEntity(args.TargetGrid, out var targetGrid) ||
            !HasComp<MapGridComponent>(targetGrid))
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-no-target");
            return;
        }

        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null || targetGrid == null || targetGrid.Value == shuttleGrid.Value)
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-no-target");
            return;
        }

        if (TryComp<AutoDockComponent>(shuttleGrid.Value, out var setting) && setting.Enabled)
        {
            BeginAutoDock(ent, shuttleGrid.Value, targetGrid.Value);
            return;
        }

        CancelAutoDock(ent);
        if (!SetAutopilotGridTarget(ent, shuttleGrid.Value, targetGrid.Value))
            PopupAutoDockError(ent, "shuttle-console-auto-dock-no-target");
        else
        {
            CancelAutonomousControlOnGrid(shuttleGrid.Value, ent.Owner);
            UndockForAutopilot(shuttleGrid.Value);
        }
    }

    private void OnAutoDockRequest(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleAutoDockRequestMessage args)
    {
        if (RejectPlanetaryAutopilot(ent))
            return;

        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null)
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-no-target");
            return;
        }

        if (!TryComp<AutoDockComponent>(shuttleGrid.Value, out var setting) || !setting.Enabled)
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-off");
            return;
        }

        // A new automatic docking order is also a departure order. Free our own ports before
        // selecting a destination so an already docked shuttle does not make every candidate
        // look occupied. UndockDocks intentionally handles all aligned ports as one group.
        UndockForAutopilot(shuttleGrid.Value);
        var targetResult = FindNearestDockingTarget(shuttleGrid.Value, out var targetGrid, out var targetConfig);
        if (targetResult == AutoDockTargetSearchResult.NoTarget)
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-no-target");
            return;
        }

        if (targetResult == AutoDockTargetSearchResult.NoCompatiblePorts)
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
            return;
        }

        BeginAutoDock(ent, shuttleGrid.Value, targetGrid!.Value, targetConfig);
    }

    private void OnToggleAutoDock(Entity<ShuttleConsoleComponent> ent, ref ToggleAutoDockRequestMessage args)
    {
        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null)
            return;

        EnsureComp<AutoDockComponent>(shuttleGrid.Value).Enabled = args.Enabled;
        if (!args.Enabled)
            CancelAutoDockOnGrid(shuttleGrid.Value);

        _console.RefreshShuttleConsoles(shuttleGrid.Value);
    }

    /// <summary>
    /// A planetary surface and the dedicated atmospheric transit map have no BSS/navigation
    /// signal. This server-side gate prevents a forged console message from starting HTN steering
    /// after the client has hidden the navigation controls.
    /// </summary>
    private bool RejectPlanetaryAutopilot(Entity<ShuttleConsoleComponent> ent)
    {
        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null ||
            !HasComp<KoronusPlanetaryTransitComponent>(shuttleGrid.Value) &&
            !HasComp<KoronusPlanetSurfaceMapComponent>(_maps.GetMapOrInvalid(Transform(shuttleGrid.Value).MapID)))
        {
            return false;
        }

        CancelAutonomousControlOnGrid(shuttleGrid.Value);
        PopupAutoDockError(ent, "koronus-planetary-nav-no-signal");
        return true;
    }

    /// <summary>
    /// A travel command must not try to accelerate a shuttle through its docking joints. The
    /// docking system owns the actual release sequence (doors, bolts, joints and UI refresh), so
    /// call its all-ports method once when any port on our grid is connected.
    /// </summary>
    private void UndockForAutopilot(EntityUid? shuttleGrid)
    {
        if (shuttleGrid == null)
            return;

        foreach (var dock in _docking.GetDocks(shuttleGrid.Value))
        {
            if (dock.Comp.DockedWith == null)
                continue;

            _docking.UndockDocks(shuttleGrid.Value);
            return;
        }
    }

    /// <summary>
    /// Finds the nearest compatible pair of docking ports, rather than first selecting a grid and
    /// accepting the docking system's preferred configuration for it. This keeps a nearby C3 from
    /// being replaced with a more distant B3 just because the latter connects more ports.
    /// </summary>
    private AutoDockTargetSearchResult FindNearestDockingTarget(
        EntityUid shuttleGrid,
        out EntityUid? targetGrid,
        out DockingConfig? targetConfig)
    {
        var shuttleMap = Transform(shuttleGrid).MapID;
        var bestDistance = AutoDockSearchRange * AutoDockSearchRange;
        var foundGridInRange = false;
        targetGrid = null;
        targetConfig = null;

        var grids = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var gridUid, out _, out var gridXform))
        {
            if (gridUid == shuttleGrid || gridXform.MapID != shuttleMap)
                continue;

            if (!TryFindNearestDockingConfig(
                    shuttleGrid,
                    gridUid,
                    bestDistance,
                    out var config,
                    out var distance,
                    out var hasPortInRange))
            {
                foundGridInRange |= hasPortInRange;
                continue;
            }

            foundGridInRange = true;
            bestDistance = distance;
            targetGrid = gridUid;
            targetConfig = config;
        }

        return targetConfig != null
            ? AutoDockTargetSearchResult.Found
            : foundGridInRange
                ? AutoDockTargetSearchResult.NoCompatiblePorts
                : AutoDockTargetSearchResult.NoTarget;
    }

    /// <summary>
    /// Returns the closest usable pair for one target grid. Asking <see cref="DockingSystem"/>
    /// about the exact pair prevents its generic configuration ranking from changing our choice.
    /// </summary>
    private bool TryFindNearestDockingConfig(
        EntityUid shuttleGrid,
        EntityUid targetGrid,
        float maximumDistanceSquared,
        out DockingConfig? targetConfig,
        out float selectedDistanceSquared,
        out bool hasPortInRange)
    {
        targetConfig = null;
        selectedDistanceSquared = maximumDistanceSquared;
        hasPortInRange = false;

        var shuttleDocks = _docking.GetDocks(shuttleGrid);
        var targetDocks = _docking.GetDocks(targetGrid);

        foreach (var shuttleDock in shuttleDocks)
        {
            foreach (var targetDock in targetDocks)
            {
                var distance = GetDockDistanceSquared(shuttleDock.Owner, targetDock.Owner);
                if (distance >= maximumDistanceSquared)
                    continue;

                hasPortInRange = true;
                if (distance >= selectedDistanceSquared ||
                    shuttleDock.Comp.ReceiveOnly ||
                    !_docking.CanShuttleDock(shuttleGrid, shuttleDock.Comp) ||
                    !_docking.CanShuttleDock(targetGrid, targetDock.Comp))
                {
                    continue;
                }

                var config = GetExpandedDockingConfiguration(
                    shuttleGrid,
                    targetGrid,
                    shuttleDock.Owner,
                    shuttleDock.Comp,
                    targetDock.Owner,
                    targetDock.Comp);
                if (config == null ||
                    !AreAutoDockPortsValid(shuttleGrid, targetGrid, config) ||
                    !TryGetAutoDockApproachCoordinates(shuttleGrid, config, out var approachCoordinates) ||
                    !IsAutoDockApproachCorridorClear(
                        shuttleGrid,
                        targetGrid,
                        approachCoordinates,
                        config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId)) ||
                    !IsAutoDockManeuverClear(shuttleGrid, targetGrid, approachCoordinates, config))
                {
                    continue;
                }

                targetConfig = config;
                selectedDistanceSquared = distance;
            }
        }

        return targetConfig != null;
    }

    private float GetDockDistanceSquared(EntityUid firstDock, EntityUid secondDock)
    {
        var firstPosition = _transform.GetMapCoordinates(firstDock);
        var secondPosition = _transform.GetMapCoordinates(secondDock);
        return firstPosition.MapId == secondPosition.MapId
            ? Vector2.DistanceSquared(firstPosition.Position, secondPosition.Position)
            : float.PositiveInfinity;
    }

    /// <summary>
    /// Keeps the user-selected closest port pair as the basis for the approach, then restores all
    /// other ports that share exactly the same final transform. This lets a row of three aligned
    /// ports latch as a row, instead of discarding two valid connections during target selection.
    /// </summary>
    private DockingConfig? GetExpandedDockingConfiguration(
        EntityUid shuttleGrid,
        EntityUid targetGrid,
        EntityUid shuttleDockUid,
        DockingComponent shuttleDock,
        EntityUid targetDockUid,
        DockingComponent targetDock)
    {
        var selectedConfig = _docking.GetDockingConfig(
            shuttleGrid,
            targetGrid,
            shuttleDockUid,
            shuttleDock,
            targetDockUid,
            targetDock);
        if (selectedConfig == null)
            return null;

        // Keep the exact configuration for the overwhelmingly common one-port case. Besides
        // avoiding unnecessary work in the terminal controller, this preserves the established
        // single-port manoeuvre byte-for-byte.
        if (_docking.GetDocks(shuttleGrid).Count <= 1 || _docking.GetDocks(targetGrid).Count <= 1)
            return selectedConfig;

        // The selected pair identifies the closest target grid. Within that grid use the standard
        // docking-system ranking, which deliberately prefers the configuration that joins the
        // most compatible ports. Pair-local configurations cannot reliably be merged afterwards:
        // their origin coordinates differ even when their final hull placement is identical.
        var fullConfiguration = _docking.GetDockingConfig(shuttleGrid, targetGrid);
        // The global ranking may describe a completely different side of a large multi-port
        // shuttle. Substituting it here made every candidate pair reuse that one transform, so a
        // blocked bow configuration could hide clear stern or side ports. Expand only when the
        // globally ranked row actually contains the pair currently being evaluated.
        if (fullConfiguration != null &&
            fullConfiguration.Docks.Count > selectedConfig.Docks.Count &&
            fullConfiguration.Docks.Any(pair =>
                pair.DockAUid == shuttleDockUid && pair.DockBUid == targetDockUid))
        {
            return fullConfiguration;
        }

        selectedConfig.TargetGrid = targetGrid;
        return selectedConfig;
    }

    private void BeginAutoDock(
        Entity<ShuttleConsoleComponent> ent,
        EntityUid shuttleGrid,
        EntityUid targetGrid,
        DockingConfig? config = null)
    {
        if (Transform(shuttleGrid).MapID != Transform(targetGrid).MapID)
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-no-target");
            return;
        }

        UndockForAutopilot(shuttleGrid);

        if (config == null &&
            !TryFindNearestDockingConfig(
                shuttleGrid,
                targetGrid,
                float.PositiveInfinity,
                out config,
                out _,
                out _))
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
            return;
        }

        if (config == null || !AreAutoDockPortsValid(shuttleGrid, targetGrid, config))
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
            return;
        }

        // A shuttle has one physical set of thrusters even if it has several consoles. Remove
        // every previous autonomous source before installing this order, otherwise MoverController
        // averages the controllers and RequireSolo immediately terminates both of them.
        CancelAutonomousControlOnGrid(shuttleGrid);

        var pair = config.Docks[0];

        var autoDock = EnsureComp<ShuttleConsoleAutoDockingComponent>(ent);
        autoDock.TargetGrid = targetGrid;
        autoDock.ShuttleDock = pair.DockAUid;
        autoDock.TargetDock = pair.DockBUid;
        autoDock.Configuration = config;
        autoDock.Phase = AutoDockPhase.Approach;

        if (!SetAutoDockTarget(ent, shuttleGrid, config, approach: true, out var approachCoordinates))
        {
            RemComp<ShuttleConsoleAutoDockingComponent>(ent);
            PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
            return;
        }

        autoDock.ApproachCoordinates = approachCoordinates;
    }

    private bool SetAutoDockTarget(
        Entity<ShuttleConsoleComponent> ent,
        EntityUid shuttleGrid,
        DockingConfig config,
        bool approach,
        out EntityCoordinates target)
    {
        target = config.Coordinates;
        if (!TryComp<HTNComponent>(ent, out var htn) || config.Docks.Count == 0)
            return false;

        var finalRotation = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);

        if (approach && !TryGetAutoDockApproachCoordinates(shuttleGrid, config, out target))
            return false;

        var blackboard = htn.Blackboard;
        // Keep the target on the docking grid. ShipSteeringSystem then deliberately ignores that
        // grid in its collision evasion; targeting the map made it avoid the very station it had
        // to approach and prevented the final docking manoeuvre.
        blackboard.SetValue(ent.Comp.AutoDockTargetKey, target);
        blackboard.SetValue(ent.Comp.AutoDockRotationKey, finalRotation + MathF.PI);
        _htn.Replan(htn);
        return true;
    }

    private bool TryGetAutoDockApproachCoordinates(
        EntityUid shuttleGrid,
        DockingConfig config,
        out EntityCoordinates target)
    {
        target = config.Coordinates;
        if (config.Docks.Count == 0 || !TryComp<MapGridComponent>(shuttleGrid, out _))
            return false;

        var finalPosition = _transform.ToMapCoordinates(config.Coordinates);
        var finalRotation = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);
        var targetDockPosition = _transform.GetMapCoordinates(config.Docks[0].DockBUid);
        if (finalPosition.MapId != targetDockPosition.MapId)
            return false;

        var approachDirection = _transform.GetWorldRotation(config.Docks[0].DockBUid)
            .RotateVec(new Vector2(0f, -1f));
        var clearance = GetAutoDockApproachClearance(
            shuttleGrid,
            finalPosition.Position,
            finalRotation,
            targetDockPosition.Position,
            approachDirection);
        var targetGridRotation = _transform.GetWorldRotation(config.Coordinates.EntityId);
        var localOffset = (-targetGridRotation).RotateVec(approachDirection * clearance);
        target = new EntityCoordinates(config.Coordinates.EntityId, config.Coordinates.Position + localOffset);
        return true;
    }

    /// <summary>
    /// Checks the swept, final-orientation shuttle bounds between its current position and the
    /// staging point. This rejects a geometrically nearer port when reaching it would require the
    /// long-range pilot to cut through another wing of a concave station.
    /// </summary>
    private bool IsAutoDockApproachCorridorClear(
        EntityUid shuttleGrid,
        EntityUid targetGrid,
        EntityCoordinates approachCoordinates,
        Angle finalRotation)
    {
        if (!TryComp<MapGridComponent>(shuttleGrid, out var shuttleGridComp))
            return false;

        var start = _transform.GetMapCoordinates(shuttleGrid);
        var end = _transform.ToMapCoordinates(approachCoordinates);
        if (start.MapId != end.MapId)
            return false;

        var travel = end.Position - start.Position;
        var travelLength = travel.Length();
        if (travelLength <= 0.01f)
            return true;

        var bounds = shuttleGridComp.LocalAABB;
        var center = bounds.Center;
        Span<Vector2> localSamples = stackalloc Vector2[]
        {
            center,
            new(bounds.Left, bounds.Bottom),
            new(bounds.Left, center.Y),
            new(bounds.Left, bounds.Top),
            new(center.X, bounds.Bottom),
            new(center.X, bounds.Top),
            new(bounds.Right, bounds.Bottom),
            new(bounds.Right, center.Y),
            new(bounds.Right, bounds.Top),
        };

        var direction = travel / travelLength;
        foreach (var localSample in localSamples)
        {
            var origin = start.Position + finalRotation.RotateVec(localSample);
            var ray = new CollisionRay(origin, direction, (int) CollisionGroup.Impassable);
            foreach (var hit in _physics.IntersectRay(
                         start.MapId,
                         ray,
                         travelLength,
                         shuttleGrid,
                         returnOnFirstHit: false))
            {
                var hitGrid = Transform(hit.HitEntity).GridUid;
                if (hit.HitEntity == targetGrid || hitGrid == targetGrid)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the distance required to move the whole shuttle clear of the target dock.
    /// The old diagonal-radius calculation used the maximum extent in every direction, which
    /// sent long shuttles hundreds of metres away even when approaching their narrow side.
    /// </summary>
    private float GetAutoDockApproachClearance(
        EntityUid shuttleGrid,
        Vector2 finalPosition,
        Angle finalRotation,
        Vector2 targetDockPosition,
        Vector2 approachDirection)
    {
        var bounds = Comp<MapGridComponent>(shuttleGrid).LocalAABB;
        var localDirection = (-finalRotation).RotateVec(approachDirection);
        var nearestHullPoint = new Vector2(
            localDirection.X >= 0f ? bounds.Left : bounds.Right,
            localDirection.Y >= 0f ? bounds.Bottom : bounds.Top);
        var projectedHullOffset = Vector2.Dot(nearestHullPoint, localDirection);
        var projectedOriginOffset = Vector2.Dot(finalPosition - targetDockPosition, approachDirection);
        return MathF.Max(AutoDockClearance - projectedOriginOffset - projectedHullOffset, 0f);
    }

    /// <summary>
    /// Checks the parts of the manoeuvre which a point/ray route check cannot describe: turning a
    /// non-circular hull in place and translating the complete, finally aligned hull through the
    /// last metres. A port can have a valid docking transform while still being impossible to
    /// reach from the shuttle's current close position without sweeping a bow or stern through the
    /// station. Such a port must be rejected before the steering controller receives an order.
    /// </summary>
    private bool IsAutoDockManeuverClear(
        EntityUid shuttleGrid,
        EntityUid targetGrid,
        EntityCoordinates approachCoordinates,
        DockingConfig config)
    {
        if (!TryComp<FixturesComponent>(shuttleGrid, out var fixtures) || fixtures.Fixtures.Count == 0)
            return false;

        var currentPosition = _transform.GetMapCoordinates(shuttleGrid);
        var approachPosition = _transform.ToMapCoordinates(approachCoordinates);
        var finalPosition = _transform.ToMapCoordinates(config.Coordinates);
        if (currentPosition.MapId != approachPosition.MapId ||
            currentPosition.MapId != finalPosition.MapId)
        {
            return false;
        }

        var currentRotation = _transform.GetWorldRotation(shuttleGrid);
        var finalRotation = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);
        var rotationDelta = (float) Angle.ShortestDistance(currentRotation, finalRotation).Theta;
        var rotationSteps = Math.Max(1, (int) MathF.Ceiling(MathF.Abs(rotationDelta) / AutoDockRotationClearanceStep));

        // ShipMoveTo turns before committing travel thrust. Check the same in-place turn so a
        // nearby station cannot be hit by the rotating ends of a long shuttle.
        for (var step = 0; step <= rotationSteps; step++)
        {
            var rotation = currentRotation + rotationDelta * step / rotationSteps;
            if (!IsAutoDockPoseClear(
                    shuttleGrid,
                    targetGrid,
                    fixtures,
                    currentPosition.MapId,
                    currentPosition.Position,
                    rotation))
            {
                return false;
            }
        }

        // The long-range route ends at approachPosition. From there the terminal controller moves
        // in a straight line at final attitude, so sample the complete physical hull at sub-tile
        // intervals. This catches concave station wings and ports too narrow for the selected hull.
        var terminalEnd = finalPosition.Position;
        var outward = approachPosition.Position - finalPosition.Position;
        if (outward.LengthSquared() > 0.0001f)
            terminalEnd += Vector2.Normalize(outward) * AutoDockContactTolerance;

        var terminalTravel = terminalEnd - approachPosition.Position;
        var terminalDistance = terminalTravel.Length();
        var terminalSteps = Math.Max(1, (int) MathF.Ceiling(terminalDistance / AutoDockTerminalClearanceStep));
        for (var step = 0; step <= terminalSteps; step++)
        {
            var position = Vector2.Lerp(
                approachPosition.Position,
                terminalEnd,
                step / (float) terminalSteps);
            if (!IsAutoDockPoseClear(
                    shuttleGrid,
                    targetGrid,
                    fixtures,
                    currentPosition.MapId,
                    position,
                    finalRotation))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Performs an exact grid-fixture overlap query for a hypothetical shuttle pose. Unlike an
    /// AABB-only test this respects concave hulls and rotated ships. The target station is not
    /// exempt: only touching docking faces are valid, overlap with any station fixture is not.
    /// </summary>
    private bool IsAutoDockPoseClear(
        EntityUid shuttleGrid,
        EntityUid targetGrid,
        FixturesComponent shuttleFixtures,
        MapId mapId,
        Vector2 position,
        Angle rotation)
    {
        var pose = new Robust.Shared.Physics.Transform(position, rotation);
        foreach (var fixture in shuttleFixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            _intersectingGrids.Clear();
            _mapManager.FindGridsIntersecting(
                mapId,
                fixture.Shape,
                pose,
                ref _intersectingGrids,
                approx: false,
                includeMap: false);

            foreach (var grid in _intersectingGrids)
            {
                if (grid.Owner != shuttleGrid &&
                    (grid.Owner == targetGrid || HasComp<PhysicsComponent>(grid.Owner)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool SetAutopilotTarget(Entity<ShuttleConsoleComponent> ent, MapCoordinates coordinates, Angle angle)
    {
        if (!TryComp<HTNComponent>(ent, out var htn))
            return false;

        var consoleMap = Transform(ent).MapID;
        var targetMap = coordinates.MapId;
        var mapUid = _maps.GetMapOrInvalid(targetMap);
        if (targetMap != consoleMap ||
            TryComp<KoronusSystemBoundaryComponent>(mapUid, out var boundary) &&
            (coordinates.Position - boundary.Origin).LengthSquared() > boundary.Radius * boundary.Radius)
        {
            _popup.PopupEntity(Loc.GetString("koronus-boundary-target-denied"), ent, PopupType.Medium);
            return false;
        }

        var blackboard = htn.Blackboard;
        blackboard.SetValue(ent.Comp.AutopilotTargetKey, _transform.ToCoordinates(coordinates));
        blackboard.SetValue(ent.Comp.AutopilotRotationKey, angle + MathF.PI);
        _htn.Replan(htn);
        return true;
    }

    /// <summary>
    /// Creates a normal autopilot target in the selected grid's local space. Its coordinates keep
    /// following the grid when it moves or rotates, while the point remains outside both hulls so
    /// the ordinary autopilot can approach it without becoming an implicit docking manoeuvre.
    /// </summary>
    private bool SetAutopilotGridTarget(
        Entity<ShuttleConsoleComponent> ent,
        EntityUid shuttleGrid,
        EntityUid targetGrid)
    {
        if (!TryComp<HTNComponent>(ent, out var htn) ||
            !TryComp<MapGridComponent>(shuttleGrid, out var shuttleGridComp) ||
            !TryComp<MapGridComponent>(targetGrid, out var targetGridComp))
        {
            return false;
        }

        var shuttlePosition = _transform.GetMapCoordinates(shuttleGrid);
        var targetPosition = _transform.GetMapCoordinates(targetGrid);
        if (shuttlePosition.MapId != targetPosition.MapId || shuttlePosition.MapId != Transform(ent).MapID)
            return false;

        var targetMap = targetPosition.MapId;
        var mapUid = _maps.GetMapOrInvalid(targetMap);
        if (TryComp<KoronusSystemBoundaryComponent>(mapUid, out var boundary) &&
            (targetPosition.Position - boundary.Origin).LengthSquared() > boundary.Radius * boundary.Radius)
        {
            _popup.PopupEntity(Loc.GetString("koronus-boundary-target-denied"), ent, PopupType.Medium);
            return false;
        }

        var worldDirection = shuttlePosition.Position - targetPosition.Position;
        if (worldDirection.LengthSquared() <= float.Epsilon)
            worldDirection = _transform.GetWorldRotation(targetGrid).RotateVec(Vector2.UnitY);
        else
            worldDirection = Vector2.Normalize(worldDirection);

        var targetRotation = _transform.GetWorldRotation(targetGrid);
        var targetLocalDirection = (-targetRotation).RotateVec(worldDirection);
        var targetHullPoint = new Vector2(
            targetLocalDirection.X >= 0f ? targetGridComp.LocalAABB.Right : targetGridComp.LocalAABB.Left,
            targetLocalDirection.Y >= 0f ? targetGridComp.LocalAABB.Top : targetGridComp.LocalAABB.Bottom);

        var shuttleLocalDirection = (-_transform.GetWorldRotation(shuttleGrid)).RotateVec(worldDirection);
        var shuttleHullPoint = new Vector2(
            shuttleLocalDirection.X >= 0f ? shuttleGridComp.LocalAABB.Left : shuttleGridComp.LocalAABB.Right,
            shuttleLocalDirection.Y >= 0f ? shuttleGridComp.LocalAABB.Bottom : shuttleGridComp.LocalAABB.Top);
        var shuttleHullOffset = Vector2.Dot(shuttleHullPoint, shuttleLocalDirection);
        var anchorOffset = MathF.Max(AutopilotGridAnchorClearance - shuttleHullOffset, 0f);
        var target = new EntityCoordinates(
            targetGrid,
            targetHullPoint + targetLocalDirection * anchorOffset);

        var blackboard = htn.Blackboard;
        blackboard.SetValue(ent.Comp.AutopilotTargetKey, target);
        blackboard.SetValue(ent.Comp.AutopilotRotationKey, targetRotation + MathF.PI);
        _htn.Replan(htn);
        return true;
    }

    private void CancelAutoDock(Entity<ShuttleConsoleComponent> ent)
    {
        if (TryComp<HTNComponent>(ent, out var htn))
        {
            htn.Blackboard.Remove<EntityCoordinates>(ent.Comp.AutoDockTargetKey);
            htn.Blackboard.Remove<Angle>(ent.Comp.AutoDockRotationKey);
        }

        RemComp<ShuttleConsoleAutoDockingComponent>(ent);
    }

    /// <summary>
    /// Cancels autonomous controllers attached to consoles on one shuttle. The optional exception
    /// is used after a normal target has already been written to the issuing console.
    /// </summary>
    private void CancelAutonomousControlOnGrid(EntityUid shuttleGrid, EntityUid? except = null)
    {
        _gridConsoles.Clear();
        _lookup.GetChildEntities(shuttleGrid, _gridConsoles);

        foreach (var console in _gridConsoles)
        {
            if (console.Owner == except)
                continue;

            CancelAutoDock(console);
            if (TryComp<HTNComponent>(console, out var htn))
            {
                htn.Blackboard.Remove<EntityCoordinates>(console.Comp.AutopilotTargetKey);
                htn.Blackboard.Remove<Angle>(console.Comp.AutopilotRotationKey);
            }

            _steering.Stop(console.Owner);
        }
    }

    private void CancelAutoDockOnGrid(EntityUid shuttleGrid)
    {
        _gridConsoles.Clear();
        _lookup.GetChildEntities(shuttleGrid, _gridConsoles);

        foreach (var console in _gridConsoles)
        {
            if (!HasComp<ShuttleConsoleAutoDockingComponent>(console))
                continue;

            CancelAutoDock(console);
            _steering.Stop(console.Owner);
        }
    }

    private void OnSteeringDone(Entity<ShuttleConsoleComponent> ent, ref SteeringDoneEvent args)
    {
        if (args.TargetKey == ent.Comp.AutoDockTargetKey)
        {
            if (!TryComp<ShuttleConsoleAutoDockingComponent>(ent, out var autoDock))
                return;

            // Stopping the long-range HTN pilot emits its normal completion event on the next
            // update. The terminal controller has already taken the input source by then.
            if (autoDock.Phase == AutoDockPhase.TerminalApproach)
                return;

            if (!args.Success)
            {
                CancelAutoDock(ent);
                PopupAutoDockError(ent, "shuttle-console-auto-dock-failed");
                return;
            }

            if (!TryBeginTerminalAutoDock(ent, autoDock))
            {
                CancelAutoDock(ent);
                PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
            }

            return;
        }

        if (args.TargetKey != ent.Comp.AutopilotTargetKey ||
            HasComp<ShuttleConsoleAutoDockingComponent>(ent) ||
            !args.Success)
            return;

        _audio.PlayPvs(ent.Comp.AutopilotDoneSound, ent);
        _popup.PopupEntity(Loc.GetString("shuttle-console-autopilot-popup-done"), ent, PopupType.Medium);
    }

    /// <summary>
    /// Transfers control from the generic long-range HTN pilot to the terminal docking controller.
    /// The latter is the only input source for the final approach, so its positional and velocity
    /// tolerances cannot be overridden by the normal autopilot's collision and speed policies.
    /// </summary>
    private bool TryBeginTerminalAutoDock(Entity<ShuttleConsoleComponent> ent, ShuttleConsoleAutoDockingComponent autoDock)
    {
        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null ||
            !TryComp<PhysicsComponent>(shuttleGrid.Value, out var shuttleBody) ||
            !IsAtTerminalStart(shuttleGrid.Value, shuttleBody, autoDock) ||
            !TryGetAutoDockConfiguration(shuttleGrid.Value, autoDock, out var config) ||
            config == null ||
            !AreAutoDockPortsValid(shuttleGrid.Value, autoDock.TargetGrid, config) ||
            !IsFinalDockingAreaClear(shuttleGrid.Value, autoDock.TargetGrid, config))
        {
            return false;
        }

        autoDock.Configuration = config;
        autoDock.Phase = AutoDockPhase.TerminalApproach;
        autoDock.TerminalElapsed = 0f;
        autoDock.StableTerminalTicks = 0;
        autoDock.CollisionDetected = false;

        _steering.Stop(ent.Owner);
        _mover.AddPilot(shuttleGrid.Value, ent.Owner);
        return true;
    }

    /// <summary>
    /// Detects arrival at the long-range approach point. It deliberately does not dock here:
    /// the terminal controller must physically close the remaining distance first.
    /// </summary>
    private void TryCaptureAutoDockApproach(
        Entity<ShuttleConsoleComponent> ent,
        ShuttleConsoleAutoDockingComponent autoDock)
    {
        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null ||
            !TryComp<PhysicsComponent>(shuttleGrid.Value, out var body))
        {
            return;
        }

        if (!IsAtTerminalStart(shuttleGrid.Value, body, autoDock))
            return;

        if (TryBeginTerminalAutoDock(ent, autoDock))
            return;

        CancelAutoDock(ent);
        _steering.Stop(ent.Owner);
        PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
    }

    /// <summary>
    /// Applies a low-speed position/velocity servo through normal physics forces. The generic
    /// shuttle mover remains active solely to retain its pilot/collision relay; its thrust model
    /// normalizes the requested direction and is unsuitable for centimetre-accurate docking.
    /// </summary>
    private void OnTerminalAutoDockGetInputs(
        Entity<ShuttleConsoleAutoDockingComponent> ent,
        ref GetShuttleInputsEvent args)
    {
        if (ent.Comp.Phase != AutoDockPhase.TerminalApproach)
            return;

        args.GotInput = true;
        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null ||
            shuttleGrid.Value != args.ShuttleUid ||
            !TryComp<PhysicsComponent>(shuttleGrid.Value, out var body) ||
            !TryGetAutoDockConfiguration(shuttleGrid.Value, ent.Comp, out var config) ||
            config == null ||
            !TryGetTerminalState(shuttleGrid.Value, body, ent.Comp, config, out var state))
        {
            ent.Comp.CollisionDetected = true;
            args.Input = new ShuttleInput(Vector2.Zero, 0f, 1f);
            return;
        }

        var desiredRelativeVelocity = GetTerminalDesiredRelativeVelocity(state);

        var velocityError = state.TargetVelocity + desiredRelativeVelocity - body.LinearVelocity;
        var linearAcceleration = args.FrameTime <= 0f
            ? Vector2.Zero
            : ClampMagnitude(velocityError / args.FrameTime, TerminalMaxLinearAcceleration);
        _physics.ApplyForce(shuttleGrid.Value, linearAcceleration * body.Mass, body: body);

        var desiredRelativeAngularVelocity = 0f;
        // The controller dead-band is intentionally smaller than the capture tolerance. Using
        // the same value for both makes a discrete controller settle just outside the latch
        // envelope and wait until the terminal timeout.
        var rotationDistance = MathF.Max(MathF.Abs(state.RotationError) - TerminalControlRotationTolerance, 0f);
        if (rotationDistance > 0f)
        {
            desiredRelativeAngularVelocity = MathF.Sign(state.RotationError) * MathF.Min(
                TerminalMaxRelativeAngularSpeed,
                MathF.Sqrt(2f * TerminalMaxAngularAcceleration * rotationDistance));
        }

        var angularVelocityError = state.TargetAngularVelocity + desiredRelativeAngularVelocity - body.AngularVelocity;
        var angularAcceleration = args.FrameTime <= 0f
            ? 0f
            : Math.Clamp(
                angularVelocityError / args.FrameTime,
                -TerminalMaxAngularAcceleration,
                TerminalMaxAngularAcceleration);
        if (body.InvI > 0f)
            _physics.ApplyTorque(shuttleGrid.Value, angularAcceleration / body.InvI, body: body);

        // Keep the input source alive for collision relays, but do not let MoverController add
        // an uncontrolled normalized thrust vector on top of the bounded terminal forces.
        args.Input = new ShuttleInput(Vector2.Zero, 0f, 0f);
        args.AccelMul = 1f;
        args.AngularMul = 1f;
    }

    private static Vector2 ClampMagnitude(Vector2 value, float maximum)
    {
        var length = value.Length();
        return length > maximum && length > 0f
            ? value / length * maximum
            : value;
    }

    /// <summary>
    /// Terminal guidance in the target docking-port frame. Closing motion follows the port axis,
    /// while lateral error gets an independent bounded correction. If attitude or lateral error
    /// is still large, axial closing is gated until the docking corridor is acquired. Each channel
    /// uses a bang-bang stopping envelope, so it stays fast at range and reaches the capture
    /// tolerance with near-zero relative velocity.
    /// </summary>
    private static Vector2 GetTerminalDesiredRelativeVelocity(TerminalDockingState state)
    {
        var axialError = Vector2.Dot(state.PositionError, state.ClosingAxis);
        var lateralError = state.PositionError - state.ClosingAxis * axialError;
        var lateralDistance = lateralError.Length();

        var closingLimit = lateralDistance > TerminalLateralAlignmentTolerance ||
                           MathF.Abs(state.RotationError) > TerminalAngularAlignmentTolerance
            ? TerminalAlignmentClosingSpeed
            : TerminalMaxRelativeSpeed;
        var axialSpeed = GetTerminalStoppingSpeed(
            MathF.Abs(axialError),
            closingLimit,
            TerminalMaxLinearAcceleration);
        var axialVelocity = MathF.Abs(axialError) <= TerminalControlPositionTolerance
            ? Vector2.Zero
            : state.ClosingAxis * MathF.Sign(axialError) * axialSpeed;

        var lateralVelocity = Vector2.Zero;
        if (lateralDistance > TerminalControlPositionTolerance)
        {
            var lateralSpeed = GetTerminalStoppingSpeed(
                lateralDistance,
                TerminalMaxLateralSpeed,
                TerminalMaxLinearAcceleration);
            lateralVelocity = lateralError / lateralDistance * lateralSpeed;
        }

        return ClampMagnitude(axialVelocity + lateralVelocity, TerminalMaxRelativeSpeed);
    }

    private static float GetTerminalStoppingSpeed(float distance, float maximumSpeed, float acceleration)
    {
        var stoppingDistance = MathF.Max(distance - TerminalControlPositionTolerance, 0f);
        return MathF.Min(maximumSpeed, MathF.Sqrt(2f * acceleration * stoppingDistance));
    }

    private void OnTerminalAutoDockCollision(
        Entity<ShuttleConsoleAutoDockingComponent> ent,
        ref PilotedShuttleRelayedEvent<StartCollideEvent> args)
    {
        var other = args.Args.OtherEntity;
        var otherGrid = Transform(other).GridUid;
        if ((other == ent.Comp.TargetGrid || otherGrid == ent.Comp.TargetGrid) &&
            ent.Comp.Phase == AutoDockPhase.TerminalApproach)
        {
            var shuttleGrid = Transform(ent).GridUid;
            if (shuttleGrid != null &&
                TryComp<PhysicsComponent>(shuttleGrid.Value, out var body) &&
                TryGetAutoDockConfiguration(shuttleGrid.Value, ent.Comp, out var config) &&
                config != null &&
                TryGetTerminalState(shuttleGrid.Value, body, ent.Comp, config, out var state) &&
                state.PositionError.LengthSquared() <= 1f &&
                state.RelativeVelocity.LengthSquared() <=
                    TerminalAlignmentClosingSpeed * TerminalAlignmentClosingSpeed &&
                MathF.Abs(state.RelativeAngularVelocity) <= TerminalMaxRelativeAngularSpeed)
            {
                // A sub-metre, low-speed contact at the selected port is inside the capture
                // corridor. Every earlier or faster station impact is a failed approach.
                return;
            }
        }

        ent.Comp.CollisionDetected = true;
    }

    private void UpdateTerminalAutoDock(
        Entity<ShuttleConsoleComponent> ent,
        ShuttleConsoleAutoDockingComponent autoDock,
        float frameTime)
    {
        autoDock.TerminalElapsed += frameTime;
        var shuttleGrid = Transform(ent).GridUid;
        if (autoDock.CollisionDetected ||
            autoDock.TerminalElapsed > TerminalMaximumDuration ||
            shuttleGrid == null ||
            !TryComp<PhysicsComponent>(shuttleGrid.Value, out var body) ||
            !TryGetAutoDockConfiguration(shuttleGrid.Value, autoDock, out var config) ||
            config == null ||
            !AreAutoDockPortsValid(shuttleGrid.Value, autoDock.TargetGrid, config) ||
            !IsFinalDockingAreaClear(shuttleGrid.Value, autoDock.TargetGrid, config) ||
            !TryGetTerminalState(shuttleGrid.Value, body, autoDock, config, out var state))
        {
            CancelAutoDock(ent);
            _steering.Stop(ent.Owner);
            PopupAutoDockError(ent, "shuttle-console-auto-dock-failed");
            return;
        }

        if (!IsTerminalCaptureReady(state))
        {
            autoDock.StableTerminalTicks = 0;
            return;
        }

        autoDock.StableTerminalTicks++;
        if (autoDock.StableTerminalTicks < TerminalRequiredStableTicks)
            return;

        if (!TryCompleteAutoDock(shuttleGrid.Value, autoDock, config, state))
        {
            CancelAutoDock(ent);
            PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
            return;
        }

        CancelAutoDock(ent);
        _audio.PlayPvs(ent.Comp.AutopilotDoneSound, ent);
        _popup.PopupEntity(Loc.GetString("shuttle-console-auto-dock-complete"), ent, PopupType.Medium);
    }

    private bool IsAtTerminalStart(
        EntityUid shuttleGrid,
        PhysicsComponent shuttleBody,
        ShuttleConsoleAutoDockingComponent autoDock)
    {
        var shipPosition = _transform.GetMapCoordinates(shuttleGrid);
        var approachPosition = _transform.ToMapCoordinates(autoDock.ApproachCoordinates);
        var targetVelocity = Vector2.Zero;
        var targetAngularVelocity = 0f;
        if (TryComp<PhysicsComponent>(autoDock.TargetGrid, out var targetBody))
        {
            targetVelocity = targetBody.LinearVelocity;
            targetAngularVelocity = targetBody.AngularVelocity;
        }

        return shipPosition.MapId == approachPosition.MapId &&
               Vector2.DistanceSquared(shipPosition.Position, approachPosition.Position) <= AutoDockCaptureRange * AutoDockCaptureRange &&
               (shuttleBody.LinearVelocity - targetVelocity).LengthSquared() <= AutoDockCaptureMaxRelativeSpeed * AutoDockCaptureMaxRelativeSpeed &&
               MathF.Abs(shuttleBody.AngularVelocity - targetAngularVelocity) <= AutoDockCaptureMaxRelativeAngularSpeed;
    }

    private bool TryGetAutoDockConfiguration(
        EntityUid shuttleGrid,
        ShuttleConsoleAutoDockingComponent autoDock,
        out DockingConfig? config)
    {
        // Docking coordinates are relative to the target grid, so the initial configuration stays
        // valid while that grid moves or rotates. Rebuilding it twice per tick performed several
        // child lookups and intersection queries and could also reject the shuttle itself once it
        // reached the final footprint.
        config = autoDock.Configuration;
        if (config != null)
            return config.TargetGrid == autoDock.TargetGrid && config.Docks.Count > 0;

        if (!TryComp(autoDock.ShuttleDock, out DockingComponent? shuttleDock) ||
            !TryComp(autoDock.TargetDock, out DockingComponent? targetDock))
        {
            return false;
        }

        config = GetExpandedDockingConfiguration(
            shuttleGrid,
            autoDock.TargetGrid,
            autoDock.ShuttleDock,
            shuttleDock,
            autoDock.TargetDock,
            targetDock) ?? autoDock.Configuration;
        return config != null;
    }

    private bool IsFinalDockingAreaClear(EntityUid shuttleGrid, EntityUid targetGrid, DockingConfig config)
    {
        if (!TryComp<FixturesComponent>(shuttleGrid, out var shuttleFixtures) ||
            shuttleFixtures.Fixtures.Count == 0)
        {
            return false;
        }

        var finalPosition = _transform.ToMapCoordinates(config.Coordinates);
        var finalRotation = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);
        var dockingOutward = _transform.GetWorldRotation(config.Docks[0].DockBUid)
            .RotateVec(new Vector2(0f, -1f));
        return IsAutoDockPoseClear(
            shuttleGrid,
            targetGrid,
            shuttleFixtures,
            finalPosition.MapId,
            finalPosition.Position + dockingOutward * AutoDockContactTolerance,
            finalRotation);
    }

    private bool TryGetTerminalState(
        EntityUid shuttleGrid,
        PhysicsComponent shuttleBody,
        ShuttleConsoleAutoDockingComponent autoDock,
        DockingConfig config,
        out TerminalDockingState state)
    {
        state = default;
        var shipPosition = _transform.GetMapCoordinates(shuttleGrid);
        var finalPosition = _transform.ToMapCoordinates(config.Coordinates);
        if (shipPosition.MapId != finalPosition.MapId)
            return false;

        var targetVelocity = Vector2.Zero;
        var targetAngularVelocity = 0f;
        if (TryComp<PhysicsComponent>(autoDock.TargetGrid, out var targetBody))
        {
            targetVelocity = targetBody.LinearVelocity;
            targetAngularVelocity = targetBody.AngularVelocity;
        }

        var finalRotation = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);
        var approachPosition = _transform.ToMapCoordinates(autoDock.ApproachCoordinates);
        var closingVector = finalPosition.Position - approachPosition.Position;
        var closingAxis = closingVector.LengthSquared() > 0.0001f
            ? Vector2.Normalize(closingVector)
            : _transform.GetWorldRotation(autoDock.TargetDock).RotateVec(Vector2.UnitY);
        state = new TerminalDockingState(
            finalPosition.Position - shipPosition.Position,
            (float) Angle.ShortestDistance(_transform.GetWorldRotation(shuttleGrid), finalRotation).Theta,
            shuttleBody.LinearVelocity - targetVelocity,
            shuttleBody.AngularVelocity - targetAngularVelocity,
            targetVelocity,
            targetAngularVelocity,
            closingAxis);
        return true;
    }

    private static bool IsTerminalCaptureReady(TerminalDockingState state)
    {
        return state.PositionError.LengthSquared() <= TerminalPositionTolerance * TerminalPositionTolerance &&
               MathF.Abs(state.RotationError) <= TerminalRotationTolerance &&
               state.RelativeVelocity.LengthSquared() <= TerminalRelativeSpeedTolerance * TerminalRelativeSpeedTolerance &&
               MathF.Abs(state.RelativeAngularVelocity) <= TerminalRelativeAngularSpeedTolerance;
    }

    /// <summary>
    /// The physical controller has already brought the grids within centimetres at a very low
    /// relative speed. FTLDock is now only the engine-level latch that links the two airlocks.
    /// </summary>
    private bool TryCompleteAutoDock(
        EntityUid shuttleGrid,
        ShuttleConsoleAutoDockingComponent autoDock,
        DockingConfig config,
        TerminalDockingState state)
    {
        if (!IsTerminalCaptureReady(state) ||
            !AreAutoDockPortsValid(shuttleGrid, autoDock.TargetGrid, config) ||
            !IsFinalDockingAreaClear(shuttleGrid, autoDock.TargetGrid, config))
        {
            return false;
        }

        if (TryComp<PhysicsComponent>(shuttleGrid, out var body))
        {
            // This removes at most TerminalRelativeSpeedTolerance of residual drift, analogous to
            // a docking latching mechanism. It is not used to move the shuttle into position.
            _physics.SetLinearVelocity(shuttleGrid, state.TargetVelocity, body: body);
            _physics.SetAngularVelocity(shuttleGrid, state.TargetAngularVelocity, body: body);
        }

        _shuttle.FTLDock((shuttleGrid, Transform(shuttleGrid)), config);
        return true;
    }

    private readonly record struct TerminalDockingState(
        Vector2 PositionError,
        float RotationError,
        Vector2 RelativeVelocity,
        float RelativeAngularVelocity,
        Vector2 TargetVelocity,
        float TargetAngularVelocity,
        Vector2 ClosingAxis);

    /// <summary>
    /// A docking configuration can connect several aligned ports at once. Re-check every pair
    /// immediately before docking, as another shuttle may have occupied one while we approached.
    /// </summary>
    private bool AreAutoDockPortsValid(EntityUid shuttleGrid, EntityUid targetGrid, DockingConfig config)
    {
        if (config.TargetGrid != targetGrid || config.Docks.Count == 0)
            return false;

        foreach (var pair in config.Docks)
        {
            if (!TryComp(pair.DockAUid, out DockingComponent? shuttleDock) ||
                !TryComp(pair.DockBUid, out DockingComponent? targetDock) ||
                shuttleDock.ReceiveOnly ||
                !_docking.CanShuttleDock(shuttleGrid, shuttleDock) ||
                !_docking.CanShuttleDock(targetGrid, targetDock) ||
                shuttleDock.DockedWith != null ||
                targetDock.DockedWith != null)
            {
                return false;
            }
        }

        return true;
    }

    private void PopupAutoDockError(EntityUid entity, string key)
    {
        _popup.PopupEntity(Loc.GetString(key), entity, PopupType.Medium);
    }
}
