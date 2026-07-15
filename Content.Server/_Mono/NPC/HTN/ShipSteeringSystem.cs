using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server.Emp;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using System.Numerics;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipSteeringSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MoverController _mover = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private TargetSeekingSystem _seeking = default!;

    [Dependency] private EntityQuery<MapGridComponent> _gridQuery;
    [Dependency] private EntityQuery<ProjectileGridPhaseComponent> _phaseQuery;
    [Dependency] private EntityQuery<PhysicsComponent> _physQuery;
    [Dependency] private EntityQuery<ShuttleComponent> _shuttleQuery;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery;
    [Dependency] private EntityQuery<EmpOnTriggerComponent> _empQuery;
    [Dependency] private EntityQuery<ExplosiveComponent> _explosiveQuery;
    [Dependency] private EntityQuery<TimedDespawnComponent> _timedQuery;

    private List<Entity<MapGridComponent>> _avoidGrids = new();
    private List<Entity<MapGridComponent>> _scannedGrids = new();
    private HashSet<EntityUid> _scannedGridUids = new();
    private HashSet<Entity<ShipWeaponProjectileComponent>> _avoidProjs = new();
    private List<(EntityUid Uid, bool IsGrid)> _avoidPotentialEnts = new();
    private List<ObstacleCandidate> _avoidEnts = new();

    // collision evasion input consideration sectors: 24 outer, 12 inner, 1 zero-input
    private List<EvadeCandidate> _sectors = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipSteererComponent, GetShuttleInputsEvent>(OnSteererGetInputs);
        SubscribeLocalEvent<ShipSteererComponent, PilotedShuttleRelayedEvent<StartCollideEvent>>(OnShuttleStartCollide);
    }

    private void OnSteererGetInputs(Entity<ShipSteererComponent> ent, ref GetShuttleInputsEvent args)
    {
        var pilotXform = Transform(ent);
        var shipUid = pilotXform.GridUid;

        var target = ent.Comp.Coordinates;
        var targetUid = target.EntityId;

        if (shipUid == null
            || TerminatingOrDeleted(targetUid)
            || !_shuttleQuery.TryComp(shipUid, out var shuttle)
            || !_physQuery.TryComp(shipUid, out var shipBody)
            || !_gridQuery.TryComp(shipUid, out var shipGrid))
        {
            ent.Comp.Status = ShipSteeringStatus.InRange;
            return;
        }
        ent.Comp.Status = ShipSteeringStatus.Moving;

        var shipXform = Transform(shipUid.Value);
        args.GotInput = true;

        var targetXform = Transform(targetUid);
        var targetGrid = targetXform.GridUid;
        var mapTarget = _transform.ToMapCoordinates(target);
        var shipPos = _transform.GetMapCoordinates(shipXform);

        // we or target might just be in FTL so don't count us as finished
        if (mapTarget.MapId != shipPos.MapId)
            return;

        // gather context
        var shipNorthAngle = _transform.GetWorldRotation(shipXform);
        var toTargetVec = mapTarget.Position - shipPos.Position;
        var distance = toTargetVec.Length();
        var linVel = shipBody.LinearVelocity;
        var angVel = shipBody.AngularVelocity;

        var targetVel = Vector2.Zero;
        // if target doesn't have physcomp it's likely the map so keep vector as zero
        if (ent.Comp.LeadingEnabled && _physQuery.TryComp(targetGrid ?? targetUid, out var targetBody))
            targetVel = targetBody.LinearVelocity;
        var relVel = linVel - targetVel;

        // get the actual destination we will move to
        var (destMapPos, inRange) = ResolveDestination(ent.Comp, mapTarget, shipPos, shipNorthAngle, toTargetVec, distance, relVel, angVel);

        // ResolveDestination says we're all good
        if (ent.Comp.Status == ShipSteeringStatus.InRange)
            return;

        var alignForArrival = ent.Comp.RotateBeforeArrival &&
                              (ent.Comp.RotationAlignmentDistance <= 0f ||
                               distance <= ent.Comp.RotationAlignmentDistance);
        Angle? targetAngle = (inRange || alignForArrival) && ent.Comp.InRangeRotation is { } rot
            ? rot
            : ent.Comp.AlwaysFaceTarget
                ? toTargetVec.ToWorldAngle()
                : null;

        var config = new SteeringConfig
        {
            MaxArrivedVel = ent.Comp.InRangeMaxSpeed ?? float.PositiveInfinity,
            BrakeThreshold = ent.Comp.BrakeThreshold,

            BaseEvasionTime = ent.Comp.BaseEvasionTime,
            AvoidanceMaxSpeed = ent.Comp.AvoidanceMaxSpeed,
            AvoidCollisions = ent.Comp.AvoidCollisions,
            AvoidProjectiles = ent.Comp.AvoidProjectiles,
            AvoidanceNoRotate = ent.Comp.AvoidanceNoRotate,
            EvasionSectorCount = ent.Comp.EvasionSectorCount,
            EvasionSectorDepth = ent.Comp.EvasionSectorDepth,
            MaxObstructorDistance = ent.Comp.MaxObstructorDistance,
            MinObstructorDistance = ent.Comp.MinObstructorDistance,
            AnchorMaxVelocity = ent.Comp.AnchorMaxVelocity,
            EvasionBuffer = ent.Comp.EvasionBuffer,
            SearchBuffer = ent.Comp.GridSearchBuffer,
            ScanDistanceBuffer = ent.Comp.GridSearchDistanceBuffer,
            MinimumObstacleScanDistance = ent.Comp.MinimumObstacleScanDistance,
            ProjectileSearchBounds = ent.Comp.ProjectileSearchBounds,
            EmpThreat = ent.Comp.EmpThreat,
            GridThreat = ent.Comp.GridThreat,

            ForwardFlight = ent.Comp.ForwardFlight,
            ForwardFlightEnterAngle = ent.Comp.ForwardFlightEnterAngle,
            ForwardFlightExitAngle = ent.Comp.ForwardFlightExitAngle,
            ForwardFlightEmergencyTime = ent.Comp.ForwardFlightEmergencyTime,
            UseArrivalAngle = inRange || alignForArrival,

            RotationCompensationGain = ent.Comp.RotationCompensationGain,
            TargetAngleOffset = Angle.FromDegrees(ent.Comp.TargetRotation),
            AngleOverride = targetAngle
        };
        var context = new SteeringContext
        {
            ShipUid = shipUid.Value,
            ShipXform = shipXform,
            ShipBody = shipBody,
            Shuttle = shuttle,
            ShipGrid = shipGrid,
            ShipPos = shipPos,
            ShipNorthAngle = shipNorthAngle,

            DestMapPos = destMapPos,
            TargetVel = targetVel,
            TargetUid = targetUid,
            TargetEntPos = mapTarget,
            TargetGridUid = targetGrid,

            RotationCompensation = ref ent.Comp.RotationCompensation,
            ForwardFlightAligned = ref ent.Comp.ForwardFlightAligned,
            AvoidanceWaypoint = ref ent.Comp.AvoidanceWaypoint,

            FrameTime = args.FrameTime
        };

        var destinationDistance = Vector2.Distance(shipPos.Position, destMapPos.Position);
        var brakeContext = GetBrakeContext(
            ref context,
            config.MaxArrivedVel,
            ent.Comp.AutopilotAccelerationMultiplier);
        args.Input = ProcessMovement(ref context, config, brakeContext);
        args.AccelMul = ent.Comp.AutopilotAccelerationMultiplier;
        args.SetMaxVelocity = GetRequestedMaxVelocity(
            ent.Comp,
            destinationDistance,
            brakeContext.BrakeAccel,
            targetVel.Length());
    }

    /// <summary>
    /// Builds the deceleration half of a trapezoidal motion profile. A linear interpolation makes
    /// low-acceleration ships crawl for the entire slowdown region and high-acceleration ships
    /// brake much earlier than necessary. The kinematic envelope v² = vFinal² + 2ad instead uses
    /// the braking acceleration that this exact shuttle can currently produce.
    /// </summary>
    private static float? GetRequestedMaxVelocity(
        ShipSteererComponent comp,
        float distance,
        float brakingAcceleration,
        float targetSpeed)
    {
        if (comp.SetMaxVelocity is not { } maximum ||
            comp.MinLinearVelocity is not { } minimum ||
            comp.SlowdownDistance <= 0f)
        {
            return comp.SetMaxVelocity;
        }

        float relativeLimit;
        if (brakingAcceleration > 0f)
        {
            // Keep a margin for the discrete physics step and changing thrust availability.
            const float brakingSafetyFactor = 0.8f;
            relativeLimit = MathF.Sqrt(
                minimum * minimum +
                2f * brakingAcceleration * brakingSafetyFactor * MathF.Max(distance, 0f));
        }
        else
        {
            // A damaged ship with no measurable braking authority still gets a finite and smooth
            // fallback instead of NaN/Infinity entering MoverController.
            var progress = Math.Clamp(distance / comp.SlowdownDistance, 0f, 1f);
            relativeLimit = minimum + (maximum - minimum) * progress;
        }

        // MoverController limits absolute velocity, while docking is solved in target-relative
        // velocity. Preserve enough headroom to follow a moving station without exceeding the
        // order's global safety cap.
        return Math.Clamp(targetSpeed + relativeLimit, minimum, maximum);
    }

    /// <summary>
    /// Set our status and destination.
    /// </summary>
    private (MapCoordinates, bool) ResolveDestination(
        ShipSteererComponent comp,
        MapCoordinates mapTarget,
        MapCoordinates shipPos,
        Angle shipNorthAngle,
        Vector2 toTargetVec,
        float distance,
        Vector2 relVel,
        float angVel)
    {
        var maxArrivedVel = comp.InRangeMaxSpeed ?? float.PositiveInfinity;
        var maxArrivedAngVel = comp.MaxRotateRate ?? float.PositiveInfinity;
        var targetAngleOffset = Angle.FromDegrees(comp.TargetRotation);

        var highRange = comp.Range + (comp.RangeTolerance ?? 0f);
        var lowRange = (comp.Range - comp.RangeTolerance) ?? 0f;
        var midRange = (highRange + lowRange) / 2f;

        switch (comp.Mode)
        {
            case ShipSteeringMode.GoToRange:
            {
                if (!comp.NoFinish
                    && distance >= lowRange && distance <= highRange
                    && relVel.Length() < maxArrivedVel
                    && MathF.Abs(angVel) < maxArrivedAngVel)
                {
                    var good = true;
                    if (comp.InRangeRotation is { } targetWorldRot)
                    {
                        var wishRotateBy = ShortestAngleDistance(shipNorthAngle + new Angle(Math.PI), targetWorldRot);
                        good = MathF.Abs((float)wishRotateBy.Theta) < comp.RotationTolerance;
                    }
                    else if (comp.AlwaysFaceTarget)
                    {
                        var wishRotateBy = ShortestAngleDistance(shipNorthAngle + new Angle(Math.PI) - targetAngleOffset, toTargetVec.ToWorldAngle());
                        good = MathF.Abs((float)wishRotateBy.Theta) < comp.RotationTolerance;
                    }
                    if (good)
                    {
                        comp.Status = ShipSteeringStatus.InRange;
                        return (mapTarget, true); // will be ignored
                    }
                }

                if (distance < lowRange || distance > highRange)
                    return (mapTarget.Offset(NormalizedOrZero(-toTargetVec) * midRange), false);

                return (shipPos, true);
            }
            case ShipSteeringMode.OrbitCW:
            case ShipSteeringMode.Orbit:
            {
                // take our position, project onto our target radius, rotate by desired orbit offset
                var invert = comp.Mode == ShipSteeringMode.OrbitCW;
                var rotateAngle = new Angle(comp.OrbitOffset * (invert ? -1 : 1));
                return (mapTarget.Offset(NormalizedOrZero(rotateAngle.RotateVec(-toTargetVec)) * midRange), false);
            }
        }

        return (mapTarget, false);
    }

    /// <summary>
    /// Handle getting our inputs.
    /// </summary>
    private ShuttleInput ProcessMovement(
        ref SteeringContext ctx,
        in SteeringConfig config,
        in BrakeContext brakeCtx)
    {
        // A waypoint that was selected on an earlier tick is already known to be our active
        // destination. Apply it before scanning so we do not query the same grids first along the
        // obsolete direct course and then along the waypoint course on every frame.
        var followingAvoidanceWaypoint = config.ForwardFlight &&
                                         TryApplyAvoidanceWaypoint(ref ctx, config, createIfMissing: false);
        var navVec = CalculateNavigationVector(ref ctx, brakeCtx);

        // check obstacle avoidance
        ScanForObstacles(ref ctx, config, brakeCtx, navVec);
        if (config.ForwardFlight &&
            !followingAvoidanceWaypoint &&
            TryApplyAvoidanceWaypoint(ref ctx, config, createIfMissing: true))
        {
            navVec = CalculateNavigationVector(ref ctx, brakeCtx);
            ScanForObstacles(ref ctx, config, brakeCtx, navVec);
        }
        var avoidanceRes = CalculateAvoidanceVector(ref ctx, config, brakeCtx, navVec);
        var avoidanceVec = avoidanceRes.AvoidVec;

        // use avoidance vector if available or proceed with thrust as normal
        var wasNav = navVec;
        var wishInputVec = avoidanceVec ?? navVec;

        var rotWish = wishInputVec;
        if (avoidanceVec != null && config.AvoidanceNoRotate)
            rotWish = wasNav;

        // process angular input
        var rotControl = CalculateRotationControl(ref ctx, config, rotWish);

        // A normal shuttle can strafe in every direction. That is useful for manual piloting,
        // but makes an AI ship visibly slide sideways while its hull points somewhere else.
        // Forward-flight orders instead rotate first, damp any existing drift, and only then
        // accelerate along the selected navigation or evasion course. An imminent collision is
        // the explicit safety exception: immediate lateral thrust is better than a collision.
        if (config.ForwardFlight)
        {
            var heading = ctx.ShipNorthAngle + new Angle(Math.PI);
            var headingError = MathF.Abs((float) ShortestAngleDistance(heading, rotControl.WishAngleActual).Theta);
            var wasAligned = ctx.ForwardFlightAligned;
            var alignmentLimit = wasAligned
                ? config.ForwardFlightExitAngle
                : config.ForwardFlightEnterAngle;
            ctx.ForwardFlightAligned = headingError <= alignmentLimit;

            var emergencyAvoidance = avoidanceRes.AllBad ||
                                     avoidanceRes.WishImpactTime is { } impactTime &&
                                     impactTime <= config.ForwardFlightEmergencyTime;
            if (!ctx.ForwardFlightAligned && !emergencyAvoidance)
                wishInputVec = GetDriftDampingVector(ctx.ShipBody.LinearVelocity - ctx.TargetVel);
        }
        else
        {
            ctx.ForwardFlightAligned = false;
        }

        // process brake input
        var brakeInput = CalculateBrake(ref ctx, config, wishInputVec, rotControl, brakeCtx, avoidanceRes.AllBad);
        if (brakeInput > 0f && !avoidanceRes.AllBad)
        {
            // MoverController already converts brake input into thrust opposite to the current
            // velocity and clamps that impulse so it cannot cross through zero. Applying the
            // navigation reverse-thrust at the same time bypasses that clamp and can launch a
            // high-thrust shuttle backwards in one tick. Brake to zero first; the next tick can
            // accelerate in the newly desired direction.
            wishInputVec = Vector2.Zero;
        }

        if (avoidanceVec != null && ctx.ShipBody.LinearVelocity.Length() > config.AvoidanceMaxSpeed)
            brakeInput = 1f;

        // convert wish-input to ship context
        var strafeInput = (-ctx.ShipNorthAngle).RotateVec(wishInputVec);
        strafeInput = GetGoodThrustVector(strafeInput, ctx.Shuttle) * MathF.Min(1f, wishInputVec.Length());

        // also set us to anchor dampening if we wish to brake
        if (brakeInput == 1f && ctx.ShipBody.LinearVelocity.Length() >= config.AnchorMaxVelocity)
            _shuttle.SetInertiaDampening(ctx.ShipUid, ctx.ShipBody, ctx.Shuttle, ctx.ShipXform, InertiaDampeningMode.Anchor);
        else
            _shuttle.SetInertiaDampening(ctx.ShipUid, ctx.ShipBody, ctx.Shuttle, ctx.ShipXform, InertiaDampeningMode.Off);

        return new ShuttleInput(strafeInput, rotControl.RotationInput, brakeInput);
    }

    private static Vector2 GetDriftDampingVector(Vector2 relativeVelocity)
    {
        return relativeVelocity.LengthSquared() > 0.01f
            ? -Vector2.Normalize(relativeVelocity)
            : Vector2.Zero;
    }

    private BrakeContext GetBrakeContext(
        ref SteeringContext ctx,
        float maxArrivedVel,
        float requestedAccelerationMultiplier)
    {
        // check our brake thrust
        var brakeVec = GetGoodThrustVector((-ctx.ShipNorthAngle).RotateVec(-ctx.ShipBody.LinearVelocity), ctx.Shuttle);
        var brakeAccelVec = _mover.GetDirectionAccel(brakeVec, ctx.Shuttle, ctx.ShipBody, ctx.ShipXform);
        // GetDirectionAccel observes the multiplier from the previous mover tick. Scale it to the
        // multiplier requested by this order, which MoverController applies before this frame's
        // force is calculated.
        var multiplierScale = ctx.Shuttle.AccelerationMultiplier > 0.001f
            ? requestedAccelerationMultiplier / ctx.Shuttle.AccelerationMultiplier
            : 0f;
        // Routine braking deliberately suppresses navigation reverse-thrust (see ProcessMovement),
        // so only the mover's bounded brake impulse is available. Counting an additional normal
        // thrust vector here made real asymmetric ships brake much later than physically possible.
        var brakeAccel = brakeAccelVec.Length() * multiplierScale * ShuttleComponent.BrakeCoefficient;

        if (brakeAccel <= 0f)
            return new BrakeContext(0f, 0f, 0f);

        var linVelLenSq = ctx.ShipBody.LinearVelocity.LengthSquared();

        // s = v^2 / 2a
        var brakePath = linVelLenSq / (2f * brakeAccel);
        // path we will pass if we keep braking until we reach our desired max velocity
        var innerBrakePath = maxArrivedVel*maxArrivedVel / (2f * brakeAccel);

        // negative if we're already slow enough
        var leftoverBrakePath = brakeAccel == 0f ? 0f : brakePath - innerBrakePath;

        return new BrakeContext(brakeAccel, brakePath, leftoverBrakePath);
    }

    private void ScanForObstacles(
        ref SteeringContext ctx,
        in SteeringConfig config,
        in BrakeContext brake,
        Vector2 navigationVector)
    {
        var shipPosVec = ctx.ShipPos.Position;
        var shipVel = ctx.ShipBody.LinearVelocity;
        var shipAABB = ctx.ShipGrid.LocalAABB;

        var scanDistance = brake.BrakeAccel == 0f ?
                               config.MaxObstructorDistance
                               : MathF.Min(config.MaxObstructorDistance, brake.BrakePath * 4f);
        scanDistance = MathF.Min(
            config.MaxObstructorDistance,
            MathF.Max(scanDistance, config.MinimumObstacleScanDistance));
        scanDistance += shipAABB.Size.Length() * 0.5f + config.ScanDistanceBuffer;

        // Scan in both the present velocity direction and the intended course when they differ.
        // The old velocity-only scan did not see an obstacle directly ahead after a turn or from
        // rest, which is exactly when a newly issued autopilot order needs to plan its first move.
        _avoidGrids.Clear();
        _scannedGridUids.Clear();
        if (config.AvoidCollisions)
        {
            var velocityDirection = NormalizedOrZero(shipVel);
            var navigationDirection = NormalizedOrZero(navigationVector);
            var primaryDirection = velocityDirection.LengthSquared() > 0f
                ? velocityDirection
                : navigationDirection;

            if (primaryDirection.LengthSquared() > 0f)
            {
                AddObstacleScan(
                    ctx.ShipPos.MapId,
                    shipPosVec,
                    shipAABB,
                    scanDistance,
                    config.SearchBuffer,
                    primaryDirection.ToWorldAngle());
            }

            if (navigationDirection.LengthSquared() > 0f &&
                (velocityDirection.LengthSquared() == 0f ||
                 Vector2.Dot(velocityDirection, navigationDirection) < 0.98f))
            {
                AddObstacleScan(
                    ctx.ShipPos.MapId,
                    shipPosVec,
                    shipAABB,
                    scanDistance,
                    config.SearchBuffer,
                    navigationDirection.ToWorldAngle());
            }
        }

        _avoidProjs.Clear();
        if (config.AvoidProjectiles)
            _avoidProjs = _lookup.GetEntitiesInRange<ShipWeaponProjectileComponent>(
                ctx.ShipPos,
                config.ProjectileSearchBounds,
                LookupFlags.Approximate | LookupFlags.Dynamic | LookupFlags.Sensors);

        // pool all queried ents
        _avoidPotentialEnts.Clear();
        foreach (var grid in _avoidGrids)
            _avoidPotentialEnts.Add((grid, true));

        foreach (var proj in _avoidProjs)
            if (!_phaseQuery.TryComp(proj, out var phase) || phase.SourceGrid != ctx.ShipUid)
                _avoidPotentialEnts.Add((proj, false));

        _avoidEnts.Clear();
        foreach (var (ent, isGrid) in _avoidPotentialEnts)
        {
            // don't avoid ourselves or the target
            if (ent == ctx.ShipUid || ent == ctx.TargetUid || ent == ctx.TargetGridUid || !_physQuery.TryComp(ent, out var obstacleBody))
                continue;

            var otherXform = Transform(ent);
            _gridQuery.TryComp(ent, out var obsGrid);
            var aabb = _physics.GetWorldAABB(ent, body: obstacleBody, xform: otherXform);
            var obsPos = aabb.Center;
            var obsRadius = (obsGrid?.LocalAABB ?? aabb).Size.Length() * 0.5f;

            var threat = 0f;
            if (isGrid)
            {
                var deltaVel = obstacleBody.LinearVelocity - shipVel;
                // const * dV^2 * tilecount
                threat = config.GridThreat * deltaVel.LengthSquared() * (obstacleBody.FixturesMass / ShuttleSystem.TileDensityMultiplier);
            }
            else
            {
                if (_projectileQuery.TryComp(ent, out var proj))
                    threat += (float)proj.Damage.GetTotal();

                if (_empQuery.TryComp(ent, out var emp))
                    threat += config.EmpThreat * emp.DisableDuration * emp.Range * emp.Range;

                if (_explosiveQuery.TryComp(ent, out var exp))
                    threat += exp.TotalIntensity * (float)_proto.Index(exp.ExplosionType).DamagePerIntensity.GetTotal();

                // untagged ship weapon projectile? avoid it anyway just in case
                if (threat == 0f)
                    threat = 1f;
            }

            _avoidEnts.Add(new((ent, otherXform, obstacleBody), obsPos, obsRadius, threat));
        }

    }

    private void AddObstacleScan(
        MapId mapId,
        Vector2 shipPosition,
        Box2 shipAabb,
        float scanDistance,
        float searchBuffer,
        Angle scanAngle)
    {
        var scanBoundsLocal = shipAabb
            .Enlarged(searchBuffer)
            .ExtendToContain(new Vector2(0f, scanDistance));
        var scanBounds = new Box2(
            scanBoundsLocal.BottomLeft + shipPosition,
            scanBoundsLocal.TopRight + shipPosition);
        var scanBoundsWorld = new Box2Rotated(scanBounds, scanAngle - new Angle(Math.PI), shipPosition);

        _scannedGrids.Clear();
        _mapMan.FindGridsIntersecting(mapId, scanBoundsWorld, ref _scannedGrids, approx: true, includeMap: false);
        foreach (var grid in _scannedGrids)
        {
            if (_scannedGridUids.Add(grid.Owner))
                _avoidGrids.Add(grid);
        }
    }

    /// <summary>
    /// The sector solver is intentionally reactive. For a large grid directly on a long course,
    /// however, changing the preferred side every tick can graze a corner before the solver has
    /// room to react. Keep one map-space waypoint beyond the obstructing hull so the shuttle
    /// commits to a clear left or right passage, then return to the original destination.
    /// </summary>
    private bool TryApplyAvoidanceWaypoint(
        ref SteeringContext ctx,
        in SteeringConfig config,
        bool createIfMissing)
    {
        var shipPosition = ctx.ShipPos.Position;
        if (ctx.AvoidanceWaypoint is { } waypoint)
        {
            if (waypoint.MapId != ctx.ShipPos.MapId ||
                Vector2.DistanceSquared(shipPosition, waypoint.Position) <= GetWaypointArrivalDistanceSquared(ctx.ShipGrid))
            {
                ctx.AvoidanceWaypoint = null;
            }
            else
            {
                ctx.DestMapPos = waypoint;
                return true;
            }
        }

        if (!createIfMissing)
            return false;

        var toDestination = ctx.DestMapPos.Position - shipPosition;
        var destinationDirection = NormalizedOrZero(toDestination);
        if (destinationDirection.LengthSquared() == 0f)
            return false;

        ObstacleCandidate? blockingObstacle = null;
        var nearestForwardDistance = float.PositiveInfinity;
        var shipRadius = ctx.ShipGrid.LocalAABB.Size.Length() * 0.5f;
        foreach (var obstacle in _avoidEnts)
        {
            var toObstacle = obstacle.Pos - shipPosition;
            var forwardDistance = Vector2.Dot(toObstacle, destinationDirection);
            if (forwardDistance <= 0f || forwardDistance >= nearestForwardDistance)
                continue;

            var lateralDistance = MathF.Abs(Cross(destinationDirection, toObstacle));
            var corridorRadius = obstacle.Radius + shipRadius + config.EvasionBuffer;
            if (lateralDistance > corridorRadius)
                continue;

            blockingObstacle = obstacle;
            nearestForwardDistance = forwardDistance;
        }

        if (blockingObstacle == null)
            return false;

        var obstacleCandidate = blockingObstacle.Value;
        var sideDirection = new Vector2(-destinationDirection.Y, destinationDirection.X);
        var currentSide = Vector2.Dot(shipPosition - obstacleCandidate.Pos, sideDirection);
        if (MathF.Abs(currentSide) < 0.1f)
        {
            var velocitySide = Vector2.Dot(ctx.ShipBody.LinearVelocity, sideDirection);
            currentSide = MathF.Abs(velocitySide) >= 0.1f
                ? velocitySide
                : (ctx.ShipUid.GetHashCode() & 1) == 0 ? 1f : -1f;
        }

        sideDirection *= MathF.Sign(currentSide);
        // Leave a full buffer beside the obstacle and enough forward progress that the next
        // direct-course check sees the obstruction behind the shuttle instead of changing sides.
        var clearance = obstacleCandidate.Radius + shipRadius + config.EvasionBuffer + 8f;
        var waypointPosition = obstacleCandidate.Pos + sideDirection * clearance + destinationDirection * clearance;
        ctx.AvoidanceWaypoint = new MapCoordinates(waypointPosition, ctx.ShipPos.MapId);
        ctx.DestMapPos = ctx.AvoidanceWaypoint.Value;
        return true;
    }

    private static float GetWaypointArrivalDistanceSquared(MapGridComponent shipGrid)
    {
        var distance = MathF.Max(4f, shipGrid.LocalAABB.Size.Length());
        return distance * distance;
    }

    private static float Cross(Vector2 first, Vector2 second)
    {
        return first.X * second.Y - first.Y * second.X;
    }

    private record struct AvoidanceResult(Vector2? AvoidVec, bool AllBad, float? WishImpactTime);

    private AvoidanceResult CalculateAvoidanceVector(
        ref SteeringContext ctx,
        in SteeringConfig config,
        in BrakeContext brake,
        Vector2 wishDir)
    {
        var shipPos = ctx.ShipPos.Position;
        var shipVel = ctx.ShipBody.LinearVelocity;
        // we have to take radius so that if we rotate it doesn't clip us into the obstacle
        var shipRadius = ctx.ShipGrid.LocalAABB.Size.Length() / 2f + config.EvasionBuffer;

        var targetVec = ctx.DestMapPos.Position - shipPos;
        var normTarget = NormalizedOrZero(targetVec);

        // ignore collisions more than this far into the future
        var simTime = brake.BrakeAccel == 0f ? 10f : 2f * ctx.ShipBody.LinearVelocity.Length() / brake.BrakeAccel;
        simTime += config.BaseEvasionTime;

        var forwardAccelVec = _mover.GetDirectionAccel(new Vector2(0f, 1f), ctx.Shuttle, ctx.ShipBody, ctx.ShipXform);
        forwardAccelVec = ctx.ShipNorthAngle.RotateVec(forwardAccelVec);
        var forwardAccelDir = NormalizedOrZero(forwardAccelVec);
        var forwardAccel = forwardAccelVec.Length();

        _sectors.Clear();
        for (var i = 0; i < config.EvasionSectorCount; i++)
        {
            var angle = Angle.FromDegrees(360f * i / (float)config.EvasionSectorCount);
            var dir = angle.ToVec();

            var dirAccel = _mover.GetWorldDirectionAccel(dir, ctx.Shuttle, ctx.ShipBody, ctx.ShipXform);
            if (dirAccel.LengthSquared() == 0f) {
                dirAccel = dir * forwardAccel * (Vector2.Dot(dir, forwardAccelDir) + 1) * 0.5f;
            }

            for (var depth = 1; depth <= config.EvasionSectorDepth; depth++)
            {
                if (i % depth == 0)
                    // ship accel does not preserve input direction, so record original input
                    _sectors.Add(new(dir, dirAccel / depth, 1f / depth));
            }
        }
        // set scale to -1 to mark it as the wish-sector
        var wishDirThrust = _mover.GetWorldDirectionAccel(wishDir, ctx.Shuttle, ctx.ShipBody, ctx.ShipXform);
        var wishI = _sectors.Count;
        _sectors.Add(new(wishDir, wishDirThrust, -1f));

        foreach (var obstacle in _avoidEnts)
        {
            var obsRadius = obstacle.Radius;
            var sumRadius = obsRadius + shipRadius;
            var obsXform = obstacle.Ent.Comp1;
            var obsPos = obstacle.Pos;
            var obsVel = obstacle.Ent.Comp2.LinearVelocity;
            var relVel = shipVel - obsVel;
            var toObsVec = obsPos - shipPos;
            var toObsDir = toObsVec.Normalized();
            // see if it's a temporary-lived obstacle
            var lifetime = float.PositiveInfinity;
            if (_timedQuery.TryComp(obstacle.Ent, out var timed))
                lifetime = timed.Lifetime;

            // Distances to bounding planes
            var obsDistanceFront = MathF.Max(toObsVec.Length() - sumRadius, 1f);
            var obsDistanceCenter = toObsVec.Length();

            var l = Vector2.Dot(toObsDir, relVel);
            for (var i = 0; i < _sectors.Count; i++)
            {
                var sector = _sectors[i];

                var accel = sector.Accel;
                var k = 0.5f * Vector2.Dot(toObsDir, accel);

                // 1. Solve crossing the Front Tangent Plane (Entering the obstacle bounds)
                float t_f;
                var desc_f = l * l + 4f * k * obsDistanceFront;
                if (desc_f < 0f)
                    t_f = -1f;
                else if (l > 0f)
                    t_f = (2f * obsDistanceFront) / (l + MathF.Sqrt(desc_f));
                else
                    t_f = k > 0f ? (-l + MathF.Sqrt(desc_f)) / (2f * k) : -1f;

                if (t_f < 0f || t_f > simTime)
                    continue;

                // 2. Resolve longitudinal exit/stop trajectory
                Vector2 p_end;
                var desc_c = l * l + 4f * k * obsDistanceCenter;
                if (desc_c < 0f)
                {
                    // The ship stops longitudinally inside the front half of the obstacle
                    var t_stop = -l / (2f * k);
                    p_end = relVel * t_stop + 0.5f * accel * t_stop * t_stop;
                }
                else
                {
                    float t_c = l > 0f ?
                        (2f * obsDistanceCenter) / (l + MathF.Sqrt(desc_c))
                        : k > 0f ?
                            (-l + MathF.Sqrt(desc_c)) / (2f * k)
                            : -1f;

                    if (t_c < 0f) t_c = t_f; // Failsafe bounds
                    p_end = relVel * t_c + 0.5f * accel * t_c * t_c;
                }

                // 3. Line-segment to Circle-Center intersection.
                // Represents exact path traveled while navigating across the physical dimension of the obstacle.
                var p_start = relVel * t_f + 0.5f * accel * t_f * t_f;
                var seg = p_end - p_start;
                var l2 = seg.LengthSquared();
                var hits = false;

                if (l2 == 0f)
                {
                    if ((p_start - toObsVec).LengthSquared() <= sumRadius * sumRadius)
                        hits = true;
                }
                else
                {
                    // Find closest point on the segment to the obstacle's actual center point
                    var t_seg = Math.Clamp(Vector2.Dot(toObsVec - p_start, seg) / l2, 0f, 1f);
                    var proj = p_start + seg * t_seg;
                    if ((proj - toObsVec).LengthSquared() <= sumRadius * sumRadius)
                        hits = true;
                }

                if (!hits)
                    continue;

                var t = MathF.Max(0f, t_f - ctx.FrameTime);
                // ignore if it despawns before we can collide with it
                if (lifetime < t)
                    continue;

                var ctime = sector.ImpactTime is { } st ? MathF.Min(st, t) : t;
                _sectors[i] = new(sector.Input, sector.Accel, sector.Scale, ctime, sector.Threat + obstacle.Threat);
            }
        }

        // choose wish if clear
        var wishSector = _sectors[wishI];
        if (wishSector.ImpactTime == null)
            return new(null, false, null);

        // neither is clear, search for something that is
        var closestSector = (int?)null;
        var closestDistance = float.PositiveInfinity;

        var bestSector = 0;
        var bestScore = 0f;
        for (var i = 0; i < _sectors.Count; i++)
        {
            var sector = _sectors[i];
            if (sector.ImpactTime == null)
            {
                var toWishSq = (wishDir - NormalizedOrZero(sector.Accel) * sector.Scale).LengthSquared();
                if (toWishSq < closestDistance)
                {
                    closestDistance = toWishSq;
                    closestSector = i;
                }
            }
            else
            {
                var score = sector.ImpactTime.Value / sector.Threat;
                if (score > bestScore)
                {
                    bestSector = i;
                    bestScore = score;
                }
            }
        }

        var chosenI = closestSector ?? bestSector;
        var chosen = _sectors[chosenI];

        return new(
            NormalizedOrZero(chosen.Input) * chosen.Scale,
            closestSector == null,
            wishSector.ImpactTime);
    }

    // navigation for if we aren't avoiding a collision
    private Vector2 CalculateNavigationVector(ref SteeringContext ctx, in BrakeContext brake)
    {
        var toDestVec = ctx.DestMapPos.Position - ctx.ShipPos.Position;
        var destDistance = toDestVec.Length();
        var toDestDir = NormalizedOrZero(toDestVec);
        var relVel = ctx.ShipBody.LinearVelocity - ctx.TargetVel;

        // we're good
        if (brake.LeftoverBrakePath < 0f && destDistance == 0f)
            return Vector2.Zero;

        var linVelDir = NormalizedOrZero(relVel);

        // check if we should just brake
        if (brake.LeftoverBrakePath > destDistance)
            return -linVelDir;

        var accelEstVec = _mover.GetWorldDirectionAccel(toDestVec, ctx.Shuttle, ctx.ShipBody, ctx.ShipXform);
        var accelEst = accelEstVec.Length();
        // fallback to forward accel
        if (accelEst == 0f)
        {
            var forwardAccelVec = _mover.GetDirectionAccel(new Vector2(0f, 1f), ctx.Shuttle, ctx.ShipBody, ctx.ShipXform);
            accelEst = forwardAccelVec.Length();
        }
        // takes target relative to us
        var wishAngle = _seeking.CalculateAdvancedTracking(toDestVec, -relVel, accelEst);

        // do not yet process whether we can actually accelerate well in that direction
        return wishAngle.ToWorldVec();
    }

    private readonly record struct RotationResult(float RotationInput, float WishAngleVel, Angle WishAngleActual);

    private RotationResult CalculateRotationControl(
        ref SteeringContext ctx,
        in SteeringConfig config,
        Vector2 wishInputVec)
    {
        Angle wishAngleActual;
        if (config.AngleOverride != null && (!config.ForwardFlight || config.UseArrivalAngle))
            wishAngleActual = config.AngleOverride.Value;
        else if (wishInputVec.Length() > 0)
            wishAngleActual = wishInputVec.ToWorldAngle();
        else
            wishAngleActual = (ctx.DestMapPos.Position - ctx.ShipPos.Position).ToWorldAngle();

        wishAngleActual += config.TargetAngleOffset;
        var wishAngle = wishAngleActual + ctx.RotationCompensation;

        var angAccel = _mover.GetAngularAcceleration(ctx.Shuttle, ctx.ShipBody);

        // process the PID
        var wishRotateByActual = ShortestAngleDistance(ctx.ShipNorthAngle + new Angle(Math.PI), wishAngleActual);
        ctx.RotationCompensation += (float)wishRotateByActual * config.RotationCompensationGain * ctx.FrameTime * MathF.Sqrt(angAccel);

        // process how we want to rotate
        var wishRotateBy = ShortestAngleDistance(ctx.ShipNorthAngle + new Angle(Math.PI), wishAngle);
        var wishAngleVel = MathF.Sqrt(MathF.Abs((float)wishRotateBy) * 2f * angAccel) * Math.Sign(wishRotateBy);

        // check by how much our desired angular velocity would rotate us in a frame
        var wishFrameRotate = wishAngleVel * ctx.FrameTime;
        // if that would overshoot the target, wish to rotate slower
        if (MathF.Abs(wishFrameRotate) > MathF.Abs((float)wishRotateBy) && wishFrameRotate != 0f)
            wishAngleVel *= MathF.Abs((float)wishRotateBy / wishFrameRotate);

        var wishDeltaAngleVel = wishAngleVel - ctx.ShipBody.AngularVelocity;
        // this is clamped to [-1, 1] downstream, but need to invert input
        var rotationInput = angAccel == 0f ? 0f : -wishDeltaAngleVel / angAccel / ctx.FrameTime;

        return new RotationResult(rotationInput, wishAngleVel, wishAngleActual);
    }

    private float CalculateBrake(
        ref SteeringContext ctx,
        in SteeringConfig config,
        Vector2 wishInputVec,
        RotationResult rot,
        in BrakeContext brake,
        bool aggressive)
    {
        var brakeInput = 0f;
        var linVel = ctx.ShipBody.LinearVelocity;
        var angleVel = ctx.ShipBody.AngularVelocity;
        var needRotate = !aggressive
            && MathF.Abs(rot.RotationInput) >= 1f
            && -rot.RotationInput * angleVel >= 0f;

        // brake if we're moving opposite to desired direction
        var dotThreshold = aggressive ? 0f : config.BrakeThreshold;
        if (!needRotate && Vector2.Dot(NormalizedOrZero(wishInputVec), NormalizedOrZero(-linVel)) > dotThreshold)
            brakeInput = 1f;

        return brakeInput;
    }

    private void OnShuttleStartCollide(Entity<ShipSteererComponent> ent, ref PilotedShuttleRelayedEvent<StartCollideEvent> outerArgs)
    {
        var args = outerArgs.Args;
        var targetEnt = ent.Comp.Coordinates.EntityId;
        var targetGrid = Transform(targetEnt).GridUid;

        // if we want to finish movement on collide with target, do so
        if (ent.Comp.FinishOnCollide && (args.OtherEntity == targetGrid || args.OtherEntity == targetEnt))
            ent.Comp.Status = ShipSteeringStatus.InRange;
    }

    // RT's equivalent method is broken so have to use this
    public static Angle ShortestAngleDistance(Angle from, Angle to)
    {
        var diff = (to - from) % Math.Tau;
        return diff + Math.Tau * (diff < -Math.PI ? 1 : diff > Math.PI ? -1 : 0);
    }

    public static Vector2 NormalizedOrZero(Vector2 vec)
    {
        return vec.LengthSquared() == 0 ? Vector2.Zero : vec.Normalized();
    }

    /// <summary>
    /// Checks if thrust in any direction this vector wants to go to is blocked, and zeroes it out in that direction if necessary.
    /// </summary>
    public Vector2 GetGoodThrustVector(Vector2 wish, ShuttleComponent shuttle, float threshold = 0.125f)
    {
        var res = NormalizedOrZero(wish);

        var horizIndex = wish.X > 0 ? 1 : 3; // east else west
        var vertIndex = wish.Y > 0 ? 2 : 0; // north else south
        var horizThrust = shuttle.LinearThrust[horizIndex];
        var vertThrust = shuttle.LinearThrust[vertIndex];

        var wishX = MathF.Abs(res.X);
        var wishY = MathF.Abs(res.Y);

        if (horizThrust * wishX < vertThrust * threshold * wishY)
            res.X = 0f;
        if (vertThrust * wishY < horizThrust * threshold * wishX)
            res.Y = 0f;

        return NormalizedOrZero(res);
    }

    /// <summary>
    /// Adds the AI to the steering system to move towards a specific target.
    /// Returns null on failure.
    /// </summary>
    public ShipSteererComponent? Steer(Entity<ShipSteererComponent?> ent, EntityCoordinates coordinates)
    {
        var xform = Transform(ent);
        var shipUid = xform.GridUid;
        if (_shuttleQuery.TryComp(shipUid, out _))
            _mover.AddPilot(shipUid.Value, ent);
        else
            return null;

        if (!Resolve(ent, ref ent.Comp, false))
            ent.Comp = AddComp<ShipSteererComponent>(ent);

        ent.Comp.Coordinates = coordinates;

        return ent.Comp;
    }

    /// <summary>
    /// Stops the steering behavior for the AI and cleans up.
    /// </summary>
    public void Stop(Entity<ShipSteererComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        RemComp<ShipSteererComponent>(ent);
    }

    private ref struct SteeringContext
    {
        // ship
        public EntityUid ShipUid;
        public TransformComponent ShipXform;
        public PhysicsComponent ShipBody;
        // TODO: get rid of Shuttle and ShipGrid so this can be reused for non-grid piloting
        public ShuttleComponent Shuttle;
        public MapGridComponent ShipGrid;
        public MapCoordinates ShipPos;
        public Angle ShipNorthAngle;
        public MapCoordinates DestMapPos;
        // target
        public Vector2 TargetVel;
        public EntityUid TargetUid;
        public EntityUid? TargetGridUid;
        public MapCoordinates TargetEntPos;
        // navigation
        public ref float RotationCompensation;
        public ref bool ForwardFlightAligned;
        public ref MapCoordinates? AvoidanceWaypoint;
        // misc
        public float FrameTime;
    }

    private record struct SteeringConfig
    {
        // movement
        public float MaxArrivedVel;
        public float BrakeThreshold;
        // avoidance
        public bool AvoidCollisions;
        public bool AvoidProjectiles;
        public bool AvoidanceNoRotate;
        public int EvasionSectorCount;
        public int EvasionSectorDepth;
        public float AnchorMaxVelocity;
        public float BaseEvasionTime;
        public float AvoidanceMaxSpeed;
        public float MaxObstructorDistance;
        public float MinObstructorDistance;
        public float EvasionBuffer;
        public float SearchBuffer;
        public float ScanDistanceBuffer;
        public float MinimumObstacleScanDistance;
        public float ProjectileSearchBounds;
        public float EmpThreat;
        public float GridThreat;
        // forward-flight behaviour
        public bool ForwardFlight;
        public float ForwardFlightEnterAngle;
        public float ForwardFlightExitAngle;
        public float ForwardFlightEmergencyTime;
        public bool UseArrivalAngle;
        // PID
        public float RotationCompensationGain;
        // rotation
        public Angle TargetAngleOffset;
        public Angle? AngleOverride;
    }

    private readonly record struct BrakeContext(float BrakeAccel, float BrakePath, float LeftoverBrakePath);

    private readonly record struct ObstacleCandidate(Entity<TransformComponent, PhysicsComponent> Ent, Vector2 Pos, float Radius, float Threat);

    private record struct EvadeCandidate(Vector2 Input, Vector2 Accel, float Scale, float? ImpactTime = null, float Threat = 0f);
}
