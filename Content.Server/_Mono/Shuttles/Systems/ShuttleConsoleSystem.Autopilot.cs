using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._Mono.NPC.HTN.Operators;
using Content.Server._Mono.Shuttles.Components;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Components;
using Content.Server.Physics.Controllers;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Shuttles;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
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
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MoverController _mover = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private ShipSteeringSystem _steering = default!;
    [Dependency] private PowerReceiverSystem _power = default!;

    private readonly HashSet<Entity<ShuttleConsoleComponent>> _gridConsoles = new();
    private List<Entity<MapGridComponent>> _intersectingGrids = new();

    private const float AutoDockSearchRange = 300f;
    private const float AutoDockClearance = 12f;
    private const float AutopilotGridAnchorClearance = 16f;
    private const float AutoDockCaptureRange = 1.85f;
    private const float AutoDockCaptureMaxRelativeSpeed = 0.75f;
    private const float AutoDockCaptureMaxRelativeAngularSpeed = 0.35f;
    private const float AutoDockCorridorCaptureExtraAxial = 10f;
    private const float AutoDockCorridorMaxLateral = 1.25f;
    private const float AutoDockCorridorMaxLateralSpeed = 0.75f;
    private const float AutoDockCorridorMaxRotationError = 0.16f;
    private const float TerminalMaxRelativeSpeed = 5f;
    private const float TerminalMaxLateralSpeed = 2.5f;
    private const float TerminalContactSpeed = 0.75f;
    private const float TerminalMaxRelativeAngularSpeed = 2.2f;
    private const float TerminalContactAngularSpeed = 0.25f;
    private const float TerminalMaxLinearAcceleration = 6f;
    private const float TerminalMaxAngularAcceleration = 6f;
    private const float TerminalLateralAlignmentTolerance = 0.35f;
    private const float TerminalAngularAlignmentTolerance = 0.12f;
    private const float TerminalPositionTolerance = 0.10f;
    private const float TerminalControlPositionTolerance = 0.035f;
    private const float TerminalRotationTolerance = 0.00873f;
    private const float TerminalControlRotationTolerance = 0.0025f;
    private const float TerminalRelativeSpeedTolerance = 0.08f;
    private const float TerminalRelativeAngularSpeedTolerance = 0.02f;
    private const float TerminalMaximumDuration = 60f;
    // Critically damped cascade: stopping response ≈ 4 * velocity response (ζ ≈ 1.0).
    private const float TerminalVelocityResponseTime = 0.10f;
    private const float TerminalStoppingResponseTime = 0.38f;
    private const float TerminalAngularVelocityResponseTime = 0.08f;
    private const float TerminalAngularStoppingResponseTime = 0.28f;
    private const float TerminalClearanceInterval = 0.4f;
    private const float TerminalLateralCorrectionClearance = 6f;
    private const byte TerminalRequiredStableTicks = 3;
    private const float AutoDockRotationClearanceStep = MathF.PI / 36f;
    private const float AutoDockTerminalClearanceStep = 0.5f;
    // Keep the clearance probe outside both fixtures' collision margins at a flush port.
    private const float AutoDockContactTolerance = 0.045f;

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
            var ent = new Entity<ShuttleConsoleComponent>(uid, console);
            if (!CanControlShuttle(ent) || Transform(ent).GridUid is not { } shuttleGrid ||
                TryComp<PilotedShuttleComponent>(shuttleGrid, out var piloted) && piloted.ManualInput)
            {
                AbortAutoDock(ent);
                continue;
            }

            switch (autoDock.Phase)
            {
                case AutoDockPhase.Approach:
                    if (!TryGetAutoDockConfiguration(shuttleGrid, autoDock, out var config) || config == null ||
                        !AreAutoDockPortsValid(shuttleGrid, autoDock.TargetGrid, config))
                    {
                        if (!TryRestartAutoDock(ent, autoDock))
                            AbortAutoDock(ent);
                        break;
                    }

                    if (autoDock.CollisionDetected)
                    {
                        // The cruise controller can steer away from contact. Recheck the actual
                        // ports and hull before handing control to the close-range controller.
                        autoDock.CollisionDetected = false;
                        autoDock.StableTerminalTicks = 0;
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
        if (RejectPlanetaryAutopilot(ent) || !CanControlShuttle(ent))
            return;

        if (SetAutopilotTarget(ent, args.Coordinates, args.Angle))
        {
            CancelAutoDock(ent);
            var shuttleGrid = Transform(ent).GridUid;
            if (shuttleGrid != null)
                CancelAutonomousControlOnGrid(shuttleGrid.Value, ent.Owner);

            UndockForAutopilot(shuttleGrid);
        }
    }

    private void OnAutopilotGridMessage(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleAutopilotGridMessage args)
    {
        if (RejectPlanetaryAutopilot(ent) || !CanControlShuttle(ent))
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

        if (IsAutoDockEnabled(shuttleGrid.Value))
        {
            // Selecting a station explicitly must still choose a configuration whose approach
            // corridor and complete hull manoeuvre are clear. GetExpandedDockingConfiguration,
            // used by this search, restores every port that shares the selected final pose.
            if (!TryFindNearestDockingConfig(
                    shuttleGrid.Value,
                    targetGrid.Value,
                    float.PositiveInfinity,
                    out var config,
                    out _,
                    out _))
            {
                PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
                return;
            }

            BeginAutoDock(ent, shuttleGrid.Value, targetGrid.Value, config);
            return;
        }

        if (!SetAutopilotGridTarget(ent, shuttleGrid.Value, targetGrid.Value))
            PopupAutoDockError(ent, "shuttle-console-auto-dock-no-target");
        else
        {
            CancelAutoDock(ent);
            CancelAutonomousControlOnGrid(shuttleGrid.Value, ent.Owner);
            UndockForAutopilot(shuttleGrid.Value);
        }
    }

    private void OnAutoDockRequest(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleAutoDockRequestMessage args)
    {
        if (RejectPlanetaryAutopilot(ent) || !CanControlShuttle(ent))
            return;

        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null)
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-no-target");
            return;
        }

        if (!IsAutoDockEnabled(shuttleGrid.Value))
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-off");
            return;
        }

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

    private bool CanControlShuttle(Entity<ShuttleConsoleComponent> console)
    {
        var xform = Transform(console);
        return xform.Anchored && _power.IsPowered(console) &&
               TryComp<ShuttleComponent>(xform.GridUid, out var shuttle) && shuttle.Enabled &&
               !HasComp<PreventPilotComponent>(xform.GridUid) &&
               (!TryComp<FTLComponent>(xform.GridUid, out var ftl) || ftl.State == FTLState.Cooldown);
    }

    private bool IsAutoDockEnabled(EntityUid shuttleGrid)
    {
        return !TryComp<AutoDockComponent>(shuttleGrid, out var setting) || setting.Enabled;
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
        if (shuttleGrid == null)
            return false;

        var map = Transform(shuttleGrid.Value).MapUid;
        if (!HasComp<KoronusPlanetaryTransitComponent>(shuttleGrid.Value) &&
            !HasComp<KoronusPlanetSurfaceMapComponent>(map) &&
            !HasComp<KoronusLowOrbitMapComponent>(map))
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

        var grids = new List<Entity<MapGridComponent>>();
        var bounds = _physics.GetWorldAABB(shuttleGrid).Enlarged(AutoDockSearchRange);
        _maps.FindGridsIntersecting(shuttleMap, bounds, ref grids, approx: true, includeMap: false);
        foreach (var grid in grids)
        {
            var gridUid = grid.Owner;
            if (gridUid == shuttleGrid)
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
                    !AreAutoDockPortsValid(shuttleGrid, targetGrid, config, allowDeparture: true) ||
                    !TryGetAutoDockApproachCoordinates(shuttleGrid, config, out var approachCoordinates) ||
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
            targetDock,
            allowShuttleDeparture: true);
        if (selectedConfig == null)
            return null;

        // Keep the exact configuration for the overwhelmingly common one-port case. Besides
        // avoiding unnecessary work in the terminal controller, this preserves the established
        // single-port manoeuvre byte-for-byte.
        if (_docking.GetDocks(shuttleGrid).Count <= 1 || _docking.GetDocks(targetGrid).Count <= 1)
            return selectedConfig;

        // Pair-local configurations cannot be merged afterwards: their origin coordinates differ
        // even when their final hull placement is identical. Ask the docking system for every
        // compatible port at exactly the selected pose instead of using its globally best pose,
        // which may belong to another side of a large shuttle.
        var expandedConfiguration = _docking.GetDockingConfigAt(
            shuttleGrid,
            targetGrid,
            selectedConfig.Coordinates,
            selectedConfig.Angle,
            fallback: false,
            allowShuttleDeparture: true);
        if (expandedConfiguration != null &&
            expandedConfiguration.Docks.Count > selectedConfig.Docks.Count)
        {
            expandedConfiguration.TargetGrid = targetGrid;
            return expandedConfiguration;
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

        if (config == null || !AreAutoDockPortsValid(shuttleGrid, targetGrid, config, allowDeparture: true) ||
            !HasComp<HTNComponent>(ent) || !TryGetAutoDockApproachCoordinates(shuttleGrid, config, out var approachCoordinates))
        {
            PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
            return;
        }

        // A shuttle has one physical set of thrusters even if it has several consoles. Remove
        // every previous autonomous source before installing this order, otherwise MoverController
        // averages the controllers and RequireSolo immediately terminates both of them.
        CancelAutonomousControlOnGrid(shuttleGrid);
        UndockForAutopilot(shuttleGrid);

        var pair = config.Docks[0];

        var autoDock = EnsureComp<ShuttleConsoleAutoDockingComponent>(ent);
        autoDock.TargetGrid = targetGrid;
        autoDock.ShuttleDock = pair.DockAUid;
        autoDock.TargetDock = pair.DockBUid;
        autoDock.Configuration = config;
        autoDock.Phase = AutoDockPhase.Approach;

        autoDock.ApproachCoordinates = approachCoordinates;
        SetAutoDockTarget(ent, config, approachCoordinates);
    }

    private void SetAutoDockTarget(
        Entity<ShuttleConsoleComponent> ent,
        DockingConfig config,
        EntityCoordinates target)
    {
        var htn = Comp<HTNComponent>(ent);
        var blackboard = htn.Blackboard;
        // Keep both the staging point and the arrival angle relative to the docking grid.
        blackboard.SetValue(ent.Comp.AutoDockTargetKey, target);
        blackboard.SetValue(ent.Comp.AutoDockRotationKey, config.Angle + MathF.PI);
        _htn.Replan(htn);
    }

    private bool TryGetAutoDockApproachCoordinates(
        EntityUid shuttleGrid,
        DockingConfig config,
        out EntityCoordinates target)
    {
        target = config.Coordinates;
        if (config.Docks.Count == 0 || !TryComp<MapGridComponent>(shuttleGrid, out var shuttleGridComp))
            return false;

        var finalPosition = _transform.ToMapCoordinates(config.Coordinates);
        var finalRotation = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);
        var targetDockPosition = _transform.GetMapCoordinates(config.Docks[0].DockBUid);
        if (finalPosition.MapId != targetDockPosition.MapId)
            return false;

        var approachDirection = _transform.GetWorldRotation(config.Docks[0].DockBUid)
            .RotateVec(new Vector2(0f, -1f));
        var baseClearance = GetAutoDockApproachClearance(
            shuttleGrid,
            finalPosition.Position,
            finalRotation,
            targetDockPosition.Position,
            approachDirection);
        var clearance = baseClearance;

        // If the dock sits inside a concave station bay or next to a protruding wall, step staging
        // outward until an elongated shuttle can finish any arrival turn around COM without clipping.
        if (TryComp<FixturesComponent>(shuttleGrid, out var fixtures) && fixtures.Fixtures.Count > 0 &&
            TryComp<PhysicsComponent>(shuttleGrid, out var body))
        {
            var localCenter = body.LocalCenter;
            var maxCornerRadius = ShuttleFlightControl.GetMaxCornerRadius(shuttleGridComp.LocalAABB, localCenter);
            var maxClearance = baseClearance + maxCornerRadius + 24f;
            var targetGrid = config.TargetGrid != EntityUid.Invalid ? config.TargetGrid : config.Coordinates.EntityId;
            for (var testClearance = baseClearance; testClearance <= maxClearance + 0.01f; testClearance += 4f)
            {
                var testOrigin = finalPosition.Position + approachDirection * testClearance;
                var testCenter = testOrigin + finalRotation.RotateVec(localCenter);
                if (!IsFullRotationClear(shuttleGrid, targetGrid, fixtures, finalPosition.MapId, localCenter, testCenter))
                    continue;

                clearance = testClearance;
                break;
            }
        }

        var targetGridRotation = _transform.GetWorldRotation(config.Coordinates.EntityId);
        var localOffset = (-targetGridRotation).RotateVec(approachDirection * clearance);
        target = new EntityCoordinates(config.Coordinates.EntityId, config.Coordinates.Position + localOffset);
        return true;
    }

    private bool IsFullRotationClear(
        EntityUid shuttleGrid,
        EntityUid targetGrid,
        FixturesComponent fixtures,
        MapId mapId,
        Vector2 localCenter,
        Vector2 centerPosition)
    {
        const int steps = 24;
        for (var i = 0; i < steps; i++)
        {
            var rotation = new Angle(i * MathF.Tau / steps);
            var origin = centerPosition - rotation.RotateVec(localCenter);
            if (!IsAutoDockPoseClear(shuttleGrid, targetGrid, fixtures, mapId, origin, rotation))
                return false;
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
        var localCenter = TryComp<PhysicsComponent>(shuttleGrid, out var body) ? body.LocalCenter : Vector2.Zero;
        var approachCenter = approachPosition.Position + finalRotation.RotateVec(localCenter);
        var rotationDelta = (float) Angle.ShortestDistance(currentRotation, finalRotation).Theta;
        var rotationSteps = Math.Max(1, (int) MathF.Ceiling(MathF.Abs(rotationDelta) / AutoDockRotationClearanceStep));

        // The cruise controller finds a path to staging. Check that there is room to finish
        // aligning around COM at staging even when a damaged shuttle arrives at a different heading.
        for (var step = 0; step <= rotationSteps; step++)
        {
            var rotation = currentRotation + rotationDelta * step / rotationSteps;
            var testOrigin = approachCenter - rotation.RotateVec(localCenter);
            if (!IsAutoDockPoseClear(
                    shuttleGrid,
                    targetGrid,
                    fixtures,
                    currentPosition.MapId,
                    testOrigin,
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
            _maps.FindGridsIntersecting(
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
        if (!TryComp<HTNComponent>(ent, out var htn) || !float.IsFinite(coordinates.Position.X) ||
            !float.IsFinite(coordinates.Position.Y) || !double.IsFinite(angle.Theta))
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

        var shuttleLocalCenter = TryComp<PhysicsComponent>(shuttleGrid, out var shuttleBody)
            ? shuttleBody.LocalCenter
            : shuttleGridComp.LocalAABB.Center;
        var shuttleSweepRadius = ShuttleFlightControl.GetMaxCornerRadius(shuttleGridComp.LocalAABB, shuttleLocalCenter);
        var anchorOffset = MathF.Max(AutopilotGridAnchorClearance + shuttleSweepRadius, 24f);
        var target = new EntityCoordinates(
            targetGrid,
            targetHullPoint + targetLocalDirection * anchorOffset);

        var blackboard = htn.Blackboard;
        blackboard.SetValue(ent.Comp.AutopilotTargetKey, target);
        blackboard.SetValue(ent.Comp.AutopilotRotationKey, (Angle) MathF.PI);
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

        if (args.TargetKey != ent.Comp.AutopilotTargetKey || HasComp<ShuttleConsoleAutoDockingComponent>(ent))
            return;

        if (!args.Success)
        {
            PopupAutoDockError(ent, "shuttle-console-autopilot-failed");
            return;
        }

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
            !TryGetAutoDockConfiguration(shuttleGrid.Value, autoDock, out var config) ||
            config == null ||
            !IsAtTerminalStart(shuttleGrid.Value, shuttleBody, autoDock, config) ||
            !AreAutoDockPortsValid(shuttleGrid.Value, autoDock.TargetGrid, config) ||
            !IsFinalDockingAreaClear(shuttleGrid.Value, autoDock.TargetGrid, config))
        {
            return false;
        }

        autoDock.Configuration = config;
        autoDock.Phase = AutoDockPhase.TerminalApproach;
        autoDock.TerminalElapsed = 0f;
        autoDock.NextClearanceCheck = 0f;
        ResetTerminalProgress(autoDock);
        autoDock.LateralCorrectionAngle = null;
        autoDock.RetreatingForLateralCorrection = false;
        autoDock.LateralTurnStarted = false;
        autoDock.StableTerminalTicks = 0;
        autoDock.CollisionDetected = false;

        _steering.Stop(ent.Owner);
        _mover.AddPilot(shuttleGrid.Value, ent.Owner);
        return true;
    }

    /// <summary>
    /// Detects arrival at the long-range approach point or seamless alignment in the final approach
    /// corridor. It deliberately does not dock here: the terminal controller must physically close
    /// the remaining distance first.
    /// </summary>
    private void TryCaptureAutoDockApproach(
        Entity<ShuttleConsoleComponent> ent,
        ShuttleConsoleAutoDockingComponent autoDock)
    {
        var shuttleGrid = Transform(ent).GridUid;
        if (shuttleGrid == null ||
            !TryComp<PhysicsComponent>(shuttleGrid.Value, out var body) ||
            !TryGetAutoDockConfiguration(shuttleGrid.Value, autoDock, out var config) ||
            config == null)
        {
            return;
        }

        if (!IsAtTerminalStart(shuttleGrid.Value, body, autoDock, config))
            return;

        if (TryBeginTerminalAutoDock(ent, autoDock))
            return;

        CancelAutoDock(ent);
        _steering.Stop(ent.Owner);
        PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
    }

    /// <summary>
    /// Tracks the docking pose with proportional input to the currently available thrusters.
    /// </summary>
    private void OnTerminalAutoDockGetInputs(
        Entity<ShuttleConsoleAutoDockingComponent> ent,
        ref GetShuttleInputsEvent args)
    {
        if (ent.Comp.Phase != AutoDockPhase.TerminalApproach)
            return;

        args.GotInput = true;
        var shuttleGrid = Transform(ent).GridUid;
        if (!TryComp<ShuttleConsoleComponent>(ent, out var console) || !CanControlShuttle((ent.Owner, console)) ||
            shuttleGrid == null ||
            shuttleGrid.Value != args.ShuttleUid ||
            !TryComp<ShuttleComponent>(shuttleGrid.Value, out var shuttle) ||
            !TryComp<PhysicsComponent>(shuttleGrid.Value, out var body) ||
            !TryGetAutoDockConfiguration(shuttleGrid.Value, ent.Comp, out var config) ||
            config == null ||
            !TryGetTerminalState(shuttleGrid.Value, body, ent.Comp, config, out var state))
        {
            ent.Comp.CollisionDetected = true;
            return;
        }

        var heading = _transform.GetWorldRotation(shuttleGrid.Value);
        var control = new ShuttleFlightControl(shuttle, body, heading, 1f);
        var portHeading = heading + state.RotationError;
        var alignedControl = new ShuttleFlightControl(shuttle, body, portHeading, 1f);
        var lateralAxis = new Vector2(-state.ClosingAxis.Y, state.ClosingAxis.X);
        var canCorrectLaterally = Vector2.Dot(alignedControl.LimitAcceleration(lateralAxis), lateralAxis) >= 0.025f &&
                                  Vector2.Dot(alignedControl.LimitAcceleration(-lateralAxis), -lateralAxis) >= 0.025f;
        var desiredRelativeVelocity = GetTerminalDesiredRelativeVelocity(state, control, canCorrectLaterally);
        var desiredRotationError = state.RotationError;
        var maxRotationRate = TerminalMaxRelativeAngularSpeed;
        var axialError = Vector2.Dot(state.CenterPositionError, state.ClosingAxis);
        var lateralError = state.CenterPositionError - state.ClosingAxis * axialError;
        var lateralSpeed = MathF.Abs(Vector2.Dot(state.RelativeCenterVelocity, lateralAxis));
        var axialVelocity = Vector2.Dot(state.RelativeCenterVelocity, state.ClosingAxis);
        var lateralClearance = GetLateralCorrectionClearance(shuttleGrid.Value);

        if (ent.Comp.LateralCorrectionAngle != null)
        {
            // Exit lateral correction only once both lateral offset and lateral drift are tiny
            // (so residual drift doesn't immediately push the ship back outside tolerance once
            // it turns back to port heading), without waiting on unbrakeable axial drift.
            if (lateralError.LengthSquared() < 0.018f * 0.018f && lateralSpeed < 0.004f)
            {
                ent.Comp.LateralCorrectionAngle = null;
                ent.Comp.RetreatingForLateralCorrection = false;
                ent.Comp.LateralTurnStarted = false;
                ResetTerminalProgress(ent.Comp);
            }
        }
        else if (!canCorrectLaterally && lateralError.LengthSquared() > 0.065f * 0.065f && control.AngularAcceleration > 0f)
        {
            var direction = Vector2.Normalize(lateralError);
            var correctionHeading = alignedControl.GetNearestThrustHeading(direction, requireReverse: alignedControl.HasReversibleThrust);
            ent.Comp.LateralCorrectionAngle = Angle.ShortestDistance(portHeading, correctionHeading);
            ent.Comp.RetreatingForLateralCorrection = axialError < lateralClearance;
            ent.Comp.LateralTurnStarted = false;
            ResetTerminalProgress(ent.Comp);
        }

        if (ent.Comp.LateralCorrectionAngle is { } correction)
        {
            // Correct the offset with a working thruster. Hold that attitude until lateral error
            // and lateral drift are small, then rotate back to port heading where axial thrusters
            // can cancel any residual axial motion.
            if (ent.Comp.RetreatingForLateralCorrection && axialError >= lateralClearance)
            {
                ent.Comp.RetreatingForLateralCorrection = false;
                ResetTerminalProgress(ent.Comp);
            }
            else if (!ent.Comp.RetreatingForLateralCorrection && axialError < MathF.Max(3f, lateralClearance * 0.5f))
            {
                ent.Comp.RetreatingForLateralCorrection = true;
                ent.Comp.LateralTurnStarted = false;
                ResetTerminalProgress(ent.Comp);
            }

            if (ent.Comp.RetreatingForLateralCorrection)
            {
                // Hold portHeading while retreating so the working axial thrusters push away cleanly.
                desiredRelativeVelocity = MathF.Abs(state.RotationError) < 0.25f
                    ? -state.ClosingAxis * TerminalMaxLateralSpeed
                    : Vector2.Zero;
            }
            else if (!ent.Comp.LateralTurnStarted &&
                     (MathF.Abs(state.RotationError) > 0.2f || MathF.Abs(axialVelocity) > 0.06f))
            {
                // Stop along the docking axis at portHeading before turning, so axial drift is
                // minimal once the damaged axis rotates onto the docking axis.
                desiredRelativeVelocity = Vector2.Zero;
            }
            else
            {
                ent.Comp.LateralTurnStarted = true;
                desiredRotationError = (float) Angle.ShortestDistance(heading, portHeading + correction).Theta;
                maxRotationRate = 2.2f;
                if (MathF.Abs(desiredRotationError) < 0.16f && MathF.Abs(state.RelativeAngularVelocity) < 0.25f)
                {
                    var lateralDist = lateralError.Length();
                    if (lateralDist > 0.003f)
                    {
                        var latDir = lateralError / lateralDist;
                        var latSpeed = ShuttleFlightControl.StoppingSpeed(
                            lateralDist - 0.003f,
                            GetTerminalBrake(control, latDir),
                            TerminalMaxLateralSpeed,
                            TerminalStoppingResponseTime,
                            safetyFactor: 0.86f);
                        desiredRelativeVelocity = latDir * latSpeed;
                    }
                    else
                    {
                        desiredRelativeVelocity = Vector2.Zero;
                    }
                }
                else
                {
                    desiredRelativeVelocity = Vector2.Zero;
                }
            }
        }

        // Guard terminal turns against sweeping bow/stern into concave station bays or outcrops.
        if (MathF.Abs(desiredRotationError) > 0.04f && axialError > 0.8f && control.AngularAcceleration > 0f &&
            TryComp<FixturesComponent>(shuttleGrid.Value, out var terminalFixtures) && terminalFixtures.Fixtures.Count > 0)
        {
            var shipMap = _transform.GetMapCoordinates(shuttleGrid.Value);
            var shipCenter = shipMap.Position + heading.RotateVec(body.LocalCenter);
            if (!IsTerminalRotationArcClear(shuttleGrid.Value, ent.Comp.TargetGrid, terminalFixtures, shipMap.MapId,
                    body.LocalCenter, shipCenter, heading, desiredRotationError))
            {
                var turnedOtherWay = false;
                if (MathF.Abs(desiredRotationError) > 0.15f)
                {
                    var altDelta = desiredRotationError > 0f
                        ? desiredRotationError - MathF.Tau
                        : desiredRotationError + MathF.Tau;
                    if (IsTerminalRotationArcClear(shuttleGrid.Value, ent.Comp.TargetGrid, terminalFixtures, shipMap.MapId,
                            body.LocalCenter, shipCenter, heading, altDelta))
                    {
                        desiredRotationError = altDelta;
                        turnedOtherWay = true;
                    }
                }

                if (!turnedOtherWay)
                {
                    // Hold attitude and back out along the port normal until there is room to turn.
                    desiredRotationError = 0f;
                    desiredRelativeVelocity = -state.ClosingAxis * TerminalMaxLateralSpeed;
                    if (ent.Comp.LateralCorrectionAngle != null)
                    {
                        ent.Comp.RetreatingForLateralCorrection = true;
                        ent.Comp.LateralTurnStarted = false;
                    }
                }
            }
        }

        var maxLinear = MathF.Min(control.MaximumAcceleration, TerminalMaxLinearAcceleration);
        var linearAcceleration = ClampMagnitude(
            (desiredRelativeVelocity - state.RelativeCenterVelocity) / TerminalVelocityResponseTime +
            state.RotationFeedForward,
            maxLinear);
        if (!canCorrectLaterally && control.HasReversibleThrust)
        {
            // While a lateral thruster is missing, never fire the surviving unbalanced thruster
            // (and do not fire linear thrusters mid-turn while returning to port heading), or it
            // will inject sideways drift that cannot be cancelled at port heading.
            linearAcceleration = ent.Comp.LateralCorrectionAngle == null && MathF.Abs(state.RotationError) > 0.05f
                ? Vector2.Zero
                : control.LimitToReversibleAxes(linearAcceleration);
        }
        // Without full lateral thrusters, hold port heading tightly so axial burns do not inject
        // uncorrectable sideways drift over the closing distance.
        var rotationDeadband = canCorrectLaterally ? TerminalControlRotationTolerance : 0.0004f;
        var rotationError = MathF.CopySign(MathF.Max(MathF.Abs(desiredRotationError) - rotationDeadband, 0f),
            desiredRotationError);
        var maxAngular = MathF.Min(control.AngularAcceleration, TerminalMaxAngularAcceleration);
        var angularAcceleration = Math.Clamp(ShuttleFlightControl.GetAngularCommand(
            rotationError,
            state.RelativeAngularVelocity,
            maxAngular,
            maxRotationRate,
            TerminalAngularVelocityResponseTime,
            TerminalAngularStoppingResponseTime), -maxAngular, maxAngular);
        args.Input = control.GetInput(linearAcceleration, angularAcceleration);
    }

    private float GetLateralCorrectionClearance(EntityUid shuttleGrid)
    {
        if (!TryComp<MapGridComponent>(shuttleGrid, out var grid))
            return TerminalLateralCorrectionClearance;

        var halfDiagonal = grid.LocalAABB.Size.Length() * 0.5f;
        return MathF.Max(TerminalLateralCorrectionClearance, halfDiagonal + 2.5f);
    }

    private static void ResetTerminalProgress(ShuttleConsoleAutoDockingComponent order)
    {
        order.BestTerminalError = float.PositiveInfinity;
        order.SecondsWithoutProgress = 0f;
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
    /// uses a critically damped stopping envelope so it stays fast at range and settles cleanly
    /// within latch tolerances without an exponential tail.
    /// </summary>
    private static Vector2 GetTerminalDesiredRelativeVelocity(TerminalDockingState state, ShuttleFlightControl control, bool canCorrectLaterally)
    {
        var axialError = Vector2.Dot(state.CenterPositionError, state.ClosingAxis);
        var lateralError = state.CenterPositionError - state.ClosingAxis * axialError;
        var lateralDistance = lateralError.Length();

        // With a missing lateral thruster, even a small heading error during a long axial
        // burn can create drift that the shuttle cannot counteract at the docking attitude.
        var headingTolerance = canCorrectLaterally ? TerminalAngularAlignmentTolerance : 0.003f;
        var rateTolerance = canCorrectLaterally ? TerminalContactAngularSpeed : 0.012f;
        var closingLimit = MathF.Abs(state.RotationError) <= headingTolerance &&
                           MathF.Abs(state.RelativeAngularVelocity) <= rateTolerance && lateralDistance <= TerminalLateralAlignmentTolerance
            ? canCorrectLaterally ? TerminalMaxRelativeSpeed : 2.6f
            : 0f;
        var axialDirection = state.ClosingAxis * MathF.Sign(axialError);
        var axialBrake = GetTerminalBrake(control, axialDirection);
        // Permit backing out while misaligned, even when forward closing is gated.
        if (axialError < 0f)
            closingLimit = TerminalMaxLateralSpeed;
        var axialSpeed = GetTerminalStoppingSpeed(MathF.Abs(axialError), closingLimit, axialBrake);
        var axialVelocity = MathF.Abs(axialError) <= TerminalControlPositionTolerance
            ? Vector2.Zero
            : state.ClosingAxis * MathF.Sign(axialError) * axialSpeed;

        var lateralVelocity = Vector2.Zero;
        if (canCorrectLaterally && lateralDistance > TerminalControlPositionTolerance)
        {
            var lateralDirection = lateralError / lateralDistance;
            var lateralSpeed = GetTerminalStoppingSpeed(lateralDistance, TerminalMaxLateralSpeed,
                GetTerminalBrake(control, lateralDirection));
            lateralVelocity = lateralDirection * lateralSpeed;
        }

        return ClampMagnitude(axialVelocity + lateralVelocity, TerminalMaxRelativeSpeed);
    }

    private static float GetTerminalStoppingSpeed(float distance, float maximumSpeed, float acceleration)
    {
        var stoppingDistance = MathF.Max(distance - TerminalControlPositionTolerance, 0f);
        if (stoppingDistance <= 0f)
            return 0f;

        return ShuttleFlightControl.StoppingSpeed(
            stoppingDistance,
            acceleration,
            maximumSpeed,
            TerminalStoppingResponseTime,
            safetyFactor: 0.86f);
    }

    private static float GetTerminalBrake(ShuttleFlightControl control, Vector2 direction) =>
        MathF.Max(0.015f, Vector2.Dot(control.LimitAcceleration(-direction * TerminalMaxLinearAcceleration), -direction));

    private void OnTerminalAutoDockCollision(
        Entity<ShuttleConsoleAutoDockingComponent> ent,
        ref PilotedShuttleRelayedEvent<StartCollideEvent> args)
    {
        if (!args.Args.OurFixture.Hard || !args.Args.OtherFixture.Hard)
            return;

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
                TryGetTerminalState(shuttleGrid.Value, body, ent.Comp, config, out var state))
            {
                var onFace = IsContactAtDock(config, args.Args);
                if (state.PositionError.LengthSquared() <= 1f && onFace &&
                    state.RelativeVelocity.LengthSquared() <= TerminalContactSpeed * TerminalContactSpeed &&
                    MathF.Abs(state.RelativeAngularVelocity) <= TerminalContactAngularSpeed)
                {
                    return;
                }

                Log.Debug($"Auto-dock rejected target contact. On port face {onFace}. State {state}");
            }
        }

        ent.Comp.CollisionDetected = true;
        if (ent.Comp.Phase == AutoDockPhase.TerminalApproach)
            Log.Debug($"Auto-dock contact rejected for {ToPrettyString(ent)} with {ToPrettyString(other)}");
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
            !TryGetTerminalState(shuttleGrid.Value, body, autoDock, config, out var state))
        {
            Log.Debug($"Auto-dock terminal validation failed for {ToPrettyString(ent)} after {autoDock.TerminalElapsed:F1}s. Collision {autoDock.CollisionDetected}");
            if (!TryRestartAutoDock(ent, autoDock))
                AbortAutoDock(ent);
            return;
        }

        if (!TryComp<ShuttleComponent>(shuttleGrid.Value, out var shuttle) ||
            shuttle.AngularThrust * body.InvI <= 0f && (MathF.Abs(state.RotationError) > TerminalRotationTolerance ||
                MathF.Abs(state.RelativeAngularVelocity) > TerminalRelativeAngularSpeedTolerance))
        {
            AbortAutoDock(ent);
            return;
        }

        var error = state.CenterPositionError.Length() + MathF.Abs(state.RotationError) * 0.4f;
        if (autoDock.LateralCorrectionAngle is { } correction)
        {
            var axialError = Vector2.Dot(state.CenterPositionError, state.ClosingAxis);
            var lateralClearance = GetLateralCorrectionClearance(shuttleGrid.Value);
            error = autoDock.RetreatingForLateralCorrection
                ? MathF.Max(lateralClearance - axialError, 0f)
                : (state.CenterPositionError - state.ClosingAxis * axialError).Length() +
                  MathF.Abs((float) Angle.ShortestDistance(Angle.Zero, state.RotationError + correction).Theta) * 0.4f;
        }
        if (error + 0.02f < autoDock.BestTerminalError)
        {
            autoDock.BestTerminalError = error;
            autoDock.SecondsWithoutProgress = 0f;
        }

        autoDock.SecondsWithoutProgress += frameTime;
        autoDock.NextClearanceCheck -= frameTime;
        if (autoDock.NextClearanceCheck <= 0f)
        {
            autoDock.NextClearanceCheck = TerminalClearanceInterval;
            var finalClear = IsFinalDockingAreaClear(shuttleGrid.Value, autoDock.TargetGrid, config);
            var lookaheadClear = IsTerminalLookaheadClear(shuttleGrid.Value, autoDock.TargetGrid, config, body, state);
            if (autoDock.SecondsWithoutProgress > 12f || !finalClear || !lookaheadClear)
            {
                Log.Debug($"Auto-dock clearance or progress failed for {ToPrettyString(ent)}. Final clear {finalClear}. Lookahead clear {lookaheadClear}. No progress for {autoDock.SecondsWithoutProgress:F1}s. State {state}");
                if (!TryRestartAutoDock(ent, autoDock))
                    AbortAutoDock(ent);
                return;
            }
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
            Log.Debug($"Auto-dock latch rejected for {ToPrettyString(ent)}. State {state}");
            CancelAutoDock(ent);
            PopupAutoDockError(ent, "shuttle-console-auto-dock-invalid-ports");
            return;
        }

        CancelAutoDock(ent);
        _audio.PlayPvs(ent.Comp.AutopilotDoneSound, ent);
        _popup.PopupEntity(Loc.GetString("shuttle-console-auto-dock-complete"), ent, PopupType.Medium);
    }

    private void AbortAutoDock(Entity<ShuttleConsoleComponent> console)
    {
        CancelAutoDock(console);
        _steering.Stop(console.Owner);
        PopupAutoDockError(console, "shuttle-console-auto-dock-failed");
    }

    private bool TryRestartAutoDock(Entity<ShuttleConsoleComponent> console, ShuttleConsoleAutoDockingComponent order)
    {
        if (order.Retries >= 2 || Transform(console).GridUid is not { } shuttle ||
            !TryComp<HTNComponent>(console, out var htn) || !HasComp<MapGridComponent>(order.TargetGrid) ||
            Transform(shuttle).MapID != Transform(order.TargetGrid).MapID ||
            !TryFindNearestDockingConfig(shuttle, order.TargetGrid, float.PositiveInfinity, out var config, out _, out _) ||
            config == null || !TryGetAutoDockApproachCoordinates(shuttle, config, out var approach))
        {
            return false;
        }

        // A stale approach task can clear the blackboard when it shuts down. Finish it before
        // installing the replacement order and keep the old phase until that notification ends.
        order.Phase = AutoDockPhase.TerminalApproach;
        _htn.ShutdownPlan(htn);
        order.Retries++;
        order.Configuration = config;
        order.ShuttleDock = config.Docks[0].DockAUid;
        order.TargetDock = config.Docks[0].DockBUid;
        order.ApproachCoordinates = approach;
        order.Phase = AutoDockPhase.Approach;
        order.CollisionDetected = false;
        SetAutoDockTarget(console, config, approach);
        return true;
    }

    private bool IsTerminalLookaheadClear(EntityUid shuttle, EntityUid target, DockingConfig config,
        PhysicsComponent body, TerminalDockingState state)
    {
        if (!TryComp<FixturesComponent>(shuttle, out var fixtures))
            return false;

        const float lookahead = TerminalClearanceInterval;
        if (state.PositionError.LengthSquared() <= 0.25f * 0.25f)
            return true;

        var relativeMotion = state.RelativeVelocity * lookahead;
        var axialToFinal = Vector2.Dot(state.PositionError, state.ClosingAxis);
        var axialMotion = Vector2.Dot(relativeMotion, state.ClosingAxis);
        if (Vector2.Dot(relativeMotion, state.PositionError) > 0f &&
            (relativeMotion.LengthSquared() >= state.PositionError.LengthSquared() ||
             axialToFinal > 0f && axialToFinal - axialMotion <= AutoDockContactTolerance + 0.05f))
        {
            // Final footprint is checked separately with the dock-face skin contact allowance.
            // Do not project constant-velocity motion into the 0.045 m fixture margin while braking.
            return true;
        }

        var position = _transform.GetMapCoordinates(shuttle);
        var currentRotation = _transform.GetWorldRotation(shuttle);
        var currentCenter = position.Position + currentRotation.RotateVec(body.LocalCenter);
        var futureRotation = currentRotation + body.AngularVelocity * lookahead;
        var futureCenter = currentCenter + state.RelativeCenterVelocity * lookahead;
        var futureOrigin = futureCenter - futureRotation.RotateVec(body.LocalCenter);
        return IsAutoDockPoseClear(shuttle, target, fixtures, position.MapId, futureOrigin, futureRotation);
    }

    private bool IsTerminalRotationArcClear(
        EntityUid shuttleGrid,
        EntityUid targetGrid,
        FixturesComponent fixtures,
        MapId mapId,
        Vector2 localCenter,
        Vector2 centerPosition,
        Angle startRotation,
        float deltaRadians)
    {
        var absDelta = MathF.Abs(deltaRadians);
        if (absDelta <= 0.01f)
            return true;

        var steps = Math.Clamp((int) MathF.Ceiling(absDelta / AutoDockRotationClearanceStep), 1, 72);
        for (var i = 1; i <= steps; i++)
        {
            var rotation = startRotation + deltaRadians * (i / (float) steps);
            var origin = centerPosition - rotation.RotateVec(localCenter);
            if (!IsAutoDockPoseClear(shuttleGrid, targetGrid, fixtures, mapId, origin, rotation))
                return false;
        }

        return true;
    }

    private bool IsContactAtDock(DockingConfig config, StartCollideEvent collision)
    {
        var contactPoints = collision.WorldPoints;
        foreach (var pair in config.Docks)
        {
            var normal = _transform.GetWorldRotation(pair.DockBUid).RotateVec(-Vector2.UnitY);
            var targetFace = _transform.GetMapCoordinates(pair.DockBUid).Position + normal * 0.5f;
            foreach (var point in contactPoints)
            {
                // Flush hulls usually report the corners of the contact face. The port can
                // be in the middle of that face, several tiles away from those corners.
                if (MathF.Abs(Vector2.Dot(point - targetFace, normal)) <= 0.15f)
                    return true;
            }
        }

        return false;
    }

    private bool IsAtTerminalStart(
        EntityUid shuttleGrid,
        PhysicsComponent shuttleBody,
        ShuttleConsoleAutoDockingComponent autoDock,
        DockingConfig config)
    {
        var shipPosition = _transform.GetMapCoordinates(shuttleGrid);
        var approachPosition = _transform.ToMapCoordinates(autoDock.ApproachCoordinates);
        if (shipPosition.MapId != approachPosition.MapId)
            return false;

        var finalRotation = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);
        var shipRotation = _transform.GetWorldRotation(shuttleGrid);
        var shipCenter = shipPosition.Position + shipRotation.RotateVec(shuttleBody.LocalCenter);
        var approachCenter = approachPosition.Position + finalRotation.RotateVec(shuttleBody.LocalCenter);

        var targetCenterVelocity = Vector2.Zero;
        var targetAngularVelocity = 0f;
        if (TryComp<PhysicsComponent>(autoDock.TargetGrid, out var targetBody))
        {
            var localApproachCenter = autoDock.ApproachCoordinates.Position + config.Angle.RotateVec(shuttleBody.LocalCenter);
            targetCenterVelocity = _physics.GetLinearVelocity(autoDock.TargetGrid, localApproachCenter, targetBody);
            targetAngularVelocity = targetBody.AngularVelocity;
        }

        var relativeCenterVelocity = shuttleBody.LinearVelocity - targetCenterVelocity;
        var relativeAngularSpeed = MathF.Abs(shuttleBody.AngularVelocity - targetAngularVelocity);

        if (!TryComp<ShuttleComponent>(shuttleGrid, out var shuttle) ||
            !TryGetTerminalState(shuttleGrid, shuttleBody, autoDock, config, out var state))
        {
            return false;
        }

        var fullLinearThrust = Array.TrueForAll(shuttle.LinearThrust, static thrust => thrust > 0f);
        var stagingRange = fullLinearThrust ? AutoDockCaptureRange : 0.65f;
        var stagingSpeed = fullLinearThrust ? AutoDockCaptureMaxRelativeSpeed : 0.35f;
        var stagingAligned = shuttle.AngularThrust <= 0f ||
                             MathF.Abs(state.RotationError) <= (fullLinearThrust ? 0.25f : 0.2f);

        // 1. Standard staging capture (handles damaged ships, side/far-side arrivals, or misaligned turns).
        var atStaging =
            stagingAligned &&
            Vector2.DistanceSquared(shipCenter, approachCenter) <= stagingRange * stagingRange &&
            relativeCenterVelocity.LengthSquared() <= stagingSpeed * stagingSpeed &&
            relativeAngularSpeed <= AutoDockCaptureMaxRelativeAngularSpeed &&
            IsTerminalLookaheadClear(shuttleGrid, autoDock.TargetGrid, config, shuttleBody, state);
        if (atStaging)
            return true;

        // 2. Seamless rolling corridor handoff: when an intact shuttle is already aligned with the
        // port axis and within the terminal stopping envelope, hand off in motion instead of coming
        // to a dead stop at staging and accelerating all over again.
        if (!fullLinearThrust)
            return false;

        var axialError = Vector2.Dot(state.CenterPositionError, state.ClosingAxis);
        var approachAxialSpan = Vector2.Distance(
            _transform.ToMapCoordinates(config.Coordinates).Position,
            approachPosition.Position);
        var maxCorridorAxial = MathF.Max(AutoDockClearance, approachAxialSpan) + AutoDockCorridorCaptureExtraAxial;
        if (axialError < 3f || axialError > maxCorridorAxial)
            return false;

        var maxCornerRadius = TryComp<MapGridComponent>(shuttleGrid, out var shuttleGridComp)
            ? ShuttleFlightControl.GetMaxCornerRadius(shuttleGridComp.LocalAABB, shuttleBody.LocalCenter)
            : 6f;
        var maxCorridorRotError = MathF.Min(AutoDockCorridorMaxRotationError, 1.4f / MathF.Max(6f, maxCornerRadius));

        var lateralError = state.CenterPositionError - state.ClosingAxis * axialError;
        if (lateralError.LengthSquared() > AutoDockCorridorMaxLateral * AutoDockCorridorMaxLateral ||
            MathF.Abs(state.RotationError) > maxCorridorRotError ||
            MathF.Abs(state.RelativeAngularVelocity) > AutoDockCaptureMaxRelativeAngularSpeed)
        {
            return false;
        }

        var axialSpeed = Vector2.Dot(state.RelativeCenterVelocity, state.ClosingAxis);
        var lateralSpeedSq = (state.RelativeCenterVelocity - state.ClosingAxis * axialSpeed).LengthSquared();
        if (lateralSpeedSq > AutoDockCorridorMaxLateralSpeed * AutoDockCorridorMaxLateralSpeed)
            return false;

        var control = new ShuttleFlightControl(shuttle, shuttleBody, shipRotation, 1f);
        var axialBrake = GetTerminalBrake(control, state.ClosingAxis);
        var maxSafeClosing = GetTerminalStoppingSpeed(axialError, TerminalMaxRelativeSpeed, axialBrake);
        return axialSpeed >= -0.5f &&
               axialSpeed <= maxSafeClosing &&
               IsTerminalLookaheadClear(shuttleGrid, autoDock.TargetGrid, config, shuttleBody, state);
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
        var targetCenterVelocity = Vector2.Zero;
        var targetAngularVelocity = 0f;
        var finalRotation = config.Angle + _transform.GetWorldRotation(config.Coordinates.EntityId);
        var shipRotation = _transform.GetWorldRotation(shuttleGrid);
        var rotationFeedForward = Vector2.Zero;
        if (TryComp<PhysicsComponent>(autoDock.TargetGrid, out var targetBody))
        {
            targetAngularVelocity = targetBody.AngularVelocity;
            targetVelocity = _physics.GetLinearVelocity(autoDock.TargetGrid, config.Coordinates.Position, targetBody);
            var center = config.Coordinates.Position + config.Angle.RotateVec(shuttleBody.LocalCenter);
            targetCenterVelocity = _physics.GetLinearVelocity(autoDock.TargetGrid, center, targetBody);
            var targetOffset = _transform.GetWorldRotation(autoDock.TargetGrid)
                .RotateVec(center - targetBody.LocalCenter);
            rotationFeedForward = -targetAngularVelocity * targetAngularVelocity * targetOffset;
        }

        var approachPosition = _transform.ToMapCoordinates(autoDock.ApproachCoordinates);
        var closingVector = finalPosition.Position - approachPosition.Position;
        var closingAxis = closingVector.LengthSquared() > 0.0001f
            ? Vector2.Normalize(closingVector)
            : _transform.GetWorldRotation(autoDock.TargetDock).RotateVec(Vector2.UnitY);
        state = new TerminalDockingState(
            finalPosition.Position - shipPosition.Position,
            finalPosition.Position + finalRotation.RotateVec(shuttleBody.LocalCenter) -
                (shipPosition.Position + shipRotation.RotateVec(shuttleBody.LocalCenter)),
            (float) Angle.ShortestDistance(shipRotation, finalRotation).Theta,
            _physics.GetLinearVelocity(shuttleGrid, Vector2.Zero, shuttleBody) - targetVelocity,
            shuttleBody.LinearVelocity - targetCenterVelocity,
            shuttleBody.AngularVelocity - targetAngularVelocity,
            targetCenterVelocity,
            targetAngularVelocity,
            closingAxis,
            rotationFeedForward);
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
            _physics.SetLinearVelocity(shuttleGrid, state.TargetCenterVelocity, body: body);
            _physics.SetAngularVelocity(shuttleGrid, state.TargetAngularVelocity, body: body);
        }

        _shuttle.FTLDock((shuttleGrid, Transform(shuttleGrid)), config);
        return true;
    }

    private readonly record struct TerminalDockingState(
        Vector2 PositionError,
        Vector2 CenterPositionError,
        float RotationError,
        Vector2 RelativeVelocity,
        Vector2 RelativeCenterVelocity,
        float RelativeAngularVelocity,
        Vector2 TargetCenterVelocity,
        float TargetAngularVelocity,
        Vector2 ClosingAxis,
        Vector2 RotationFeedForward);

    /// <summary>
    /// A docking configuration can connect several aligned ports at once. Re-check every pair
    /// immediately before docking, as another shuttle may have occupied one while we approached.
    /// </summary>
    private bool AreAutoDockPortsValid(EntityUid shuttleGrid, EntityUid targetGrid, DockingConfig config, bool allowDeparture = false)
    {
        if (config.TargetGrid != targetGrid || config.Docks.Count == 0)
            return false;

        foreach (var pair in config.Docks)
        {
            if (!TryComp(pair.DockAUid, out DockingComponent? shuttleDock) ||
                !TryComp(pair.DockBUid, out DockingComponent? targetDock) ||
                !TryComp(pair.DockAUid, out TransformComponent? shuttleXform) || !shuttleXform.Anchored || shuttleXform.GridUid != shuttleGrid ||
                !TryComp(pair.DockBUid, out TransformComponent? targetXform) || !targetXform.Anchored || targetXform.GridUid != targetGrid ||
                (shuttleDock.DockType & targetDock.DockType) == DockType.None ||
                shuttleDock.ReceiveOnly ||
                !_docking.CanShuttleDock(shuttleGrid, shuttleDock) ||
                !_docking.CanShuttleDock(targetGrid, targetDock) ||
                !allowDeparture && shuttleDock.DockedWith != null ||
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
