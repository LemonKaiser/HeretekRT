using System.Numerics;
using Content.Server._Mono.Shuttles;
using Content.Server.Physics.Controllers;
using Content.Shared._NF.Shuttles.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipSteeringSystem
{
    [Dependency] private SharedMapSystem _maps = default!;

    // Critically damped cascade: StoppingResponseTime ≈ 4 * VelocityResponseTime (ζ ≈ 1.0).
    private const float VelocityResponseTime = 0.14f;
    private const float StoppingResponseTime = 0.52f;
    private const float AngularVelocityResponseTime = 0.08f;
    private const float AngularStoppingResponseTime = 0.28f;
    private const float CruiseObstacleSearchRange = 2400f;
    private const float CruiseTurnRate = 3.5f;
    private const float SpinRecoveryRateThreshold = 4.5f;
    private const float AntiTunnelSafeSpeed = 94f;
    private const float DockingAlignMinDistance = 42f;
    private const float DamagedStagingRange = 18f;
    private const float DamagedReversibleStagingSpeed = 8f;
    private const float DamagedSingleAxisStagingSpeed = 3.5f;
    private const float TurnClearanceAngleStep = MathF.PI / 30f; // 6 degrees

    private static readonly float[] AvoidanceAngleOffsets = { 5f, 10f, 16f, 24f, 34f, 46f, 60f, 78f, 100f, 128f, 160f, 180f };
    private static readonly float[] AvoidanceThrottles = { 1f, 0.7f, 0.4f };
    private static readonly float[] LocalDeflectAngles = { 20f, 40f, 65f, 90f, 120f, 150f, 180f };

    private List<Entity<MapGridComponent>> _nearbyCheckGrids = new();
    private List<Entity<MapGridComponent>> _turnCheckGrids = new();

    private void ProcessAutopilot(
        ShipSteererComponent order,
        ref SteeringContext context,
        in SteeringConfig config,
        bool inRange,
        float targetRate,
        ref GetShuttleInputsEvent input)
    {
        _shuttle.SetInertiaDampening(context.ShipUid, context.ShipBody, context.Shuttle, context.ShipXform, InertiaDampeningMode.Off);

        // Always operate in the shuttle's center-of-mass frame. Grid origin (0, 0) swings wildly
        // while turning on large or asymmetric ships, which would otherwise make linear thrusters
        // fight the ship's own rotation.
        var localCenter = context.ShipBody.LocalCenter;
        var shipCenter = context.ShipPos.Position + context.ShipNorthAngle.RotateVec(localCenter);
        var velocity = context.ShipBody.LinearVelocity;

        var finalHeading = order.InRangeRotation is { } orderHeading ? orderHeading - Math.PI : (Angle?) null;
        finalHeading = context.TargetApproachAngle ?? finalHeading;

        var targetCenter = context.DestMapPos.Position;
        var targetCenterVelocity = context.TargetVel;
        if (order.RotateBeforeArrival && finalHeading is { } arrivalPoseHeading)
        {
            var arrivalOffset = arrivalPoseHeading.RotateVec(localCenter);
            targetCenter += arrivalOffset;
            targetCenterVelocity += targetRate * new Vector2(-arrivalOffset.Y, arrivalOffset.X);
        }

        var displacement = targetCenter - shipCenter;
        var distance = displacement.Length();
        var relativeVelocity = velocity - targetCenterVelocity;
        var relativeSpeed = relativeVelocity.Length();
        var course = NormalizedOrZero(displacement);

        var fullLinearThrust = !order.RotateBeforeArrival || Array.TrueForAll(context.Shuttle.LinearThrust, static thrust => thrust > 0f);
        var damagedApproach = order.RotateBeforeArrival && !fullLinearThrust && distance <= DamagedStagingRange;

        var accelerationMultiplier = order.AutopilotAccelerationMultiplier;
        var control = new ShuttleFlightControl(context.Shuttle, context.ShipBody, context.ShipNorthAngle, accelerationMultiplier);

        var finalError = finalHeading is { } heading ? (float) ShortestAngleDistance(context.ShipNorthAngle, heading).Theta : 0f;
        var relativeRate = context.ShipBody.AngularVelocity - targetRate;
        var angularReady = MathF.Abs(finalError) <= order.RotationTolerance &&
                           MathF.Abs(relativeRate) <= (order.MaxRotateRate ?? 0.03f);

        var originDistance = Vector2.Distance(context.ShipPos.Position, context.DestMapPos.Position);
        var autoDockArrivalRange = fullLinearThrust ? order.Range + (order.RangeTolerance ?? 0f) : 0.55f;
        var arrivalInRange = order.RotateBeforeArrival
            ? distance <= autoDockArrivalRange
            : inRange && originDistance <= order.Range + (order.RangeTolerance ?? 0f);

        if (arrivalInRange && relativeVelocity.LengthSquared() <= config.MaxArrivedVel * config.MaxArrivedVel &&
            (angularReady || !order.RotateBeforeArrival && control.AngularAcceleration <= 0f))
        {
            order.Status = ShipSteeringStatus.InRange;
            return;
        }

        if (distance + 0.75f < order.BestDestinationDistance)
        {
            order.BestDestinationDistance = distance;
            order.SecondsWithoutProgress = 0f;
        }

        order.SecondsWithoutProgress += context.FrameTime;
        if (order.SecondsWithoutProgress >= 60f ||
            control.MaximumAcceleration <= 0f && !arrivalInRange ||
            control.AngularAcceleration <= 0f && !arrivalInRange && course != Vector2.Zero &&
            control.GetAxisAcceleration(course) <= 0f && relativeSpeed < 0.5f)
        {
            order.Status = ShipSteeringStatus.Failed;
            return;
        }

        var originMargin = order.RotateBeforeArrival
            ? 0.15f
            : MathF.Max(order.Range * 0.65f, localCenter.Length() + 2f);
        var remaining = order.RotateBeforeArrival
            ? MathF.Max(0f, distance - originMargin)
            : arrivalInRange ? 0f : MathF.Max(0f, distance - originMargin);
        var cruiseSpeed = order.SetMaxVelocity ?? context.Shuttle.BaseMaxLinearVelocity;
        var velocityDirection = NormalizedOrZero(relativeVelocity);

        var currentBraking = GetCruiseBraking(control, -velocityDirection, relativeSpeed, context.ShipBody.AngularVelocity);
        var speed = GetAutopilotSpeed(order, ref context, config, control, course, velocityDirection,
            distance, remaining, relativeSpeed, cruiseSpeed, currentBraking, out var horizon);

        if (damagedApproach)
        {
            var damagedCap = control.HasReversibleThrust ? DamagedReversibleStagingSpeed : DamagedSingleAxisStagingSpeed;
            speed = MathF.Min(speed, damagedCap);
        }

        var preferredVelocity = targetCenterVelocity + course * speed;
        var desiredVelocity = SelectAutopilotVelocity(ref context, control, shipCenter, velocity, preferredVelocity,
            targetCenterVelocity, order.PreviousAvoidanceCourse, horizon,
            damagedApproach ? (control.HasReversibleThrust ? DamagedReversibleStagingSpeed : DamagedSingleAxisStagingSpeed) : float.PositiveInfinity,
            out var avoidingObstacle);

        // Guard against the server anti-tunneling check (>= 100 m/s swept-AABB stop) when skirting near grids.
        if (avoidingObstacle && desiredVelocity.LengthSquared() > AntiTunnelSafeSpeed * AntiTunnelSafeSpeed)
            desiredVelocity = Vector2.Normalize(desiredVelocity) * AntiTunnelSafeSpeed;

        order.PreviousAvoidanceCourse = NormalizedOrZero(desiredVelocity - targetCenterVelocity);
        var acceleration = (desiredVelocity - velocity) / VelocityResponseTime;

        // Flip-and-burn management: lock the selected braking thruster against travel until the high-speed
        // braking pass completes, so the ship commits cleanly instead of flipping back and forth.
        var desiredAlongVel = Vector2.Dot(desiredVelocity - targetCenterVelocity, velocityDirection);
        var exceedingBrakingSpeed = relativeSpeed > 0.5f && desiredAlongVel + 0.4f < relativeSpeed;
        var directBrakeNow = velocityDirection != Vector2.Zero ? control.GetAxisAcceleration(-velocityDirection) : control.MaximumAcceleration;

        var alignDistance = MathF.Max(DockingAlignMinDistance, relativeSpeed * 1.6f);
        var arrivalBrakeAccel = finalHeading is { } checkHeading && course != Vector2.Zero
            ? control.GetAxisAcceleration(-course, checkHeading)
            : control.MaximumAcceleration;
        var canBrakeAtArrival = finalHeading != null &&
                                (arrivalBrakeAccel >= control.MaximumAcceleration * 0.25f ||
                                 arrivalBrakeAccel >= 0.5f &&
                                 ShuttleFlightControl.StoppingSpeed(remaining, arrivalBrakeAccel, cruiseSpeed, StoppingResponseTime, safetyFactor: 0.86f) >= relativeSpeed * 1.05f);

        if (relativeSpeed <= 2.5f ||
            order.CruiseBrakingThrusterAngle != null && !exceedingBrakingSpeed && relativeSpeed <= 8f ||
            order.CruiseBrakingThrusterAngle != null && order.RotateBeforeArrival && fullLinearThrust &&
            !avoidingObstacle && canBrakeAtArrival && distance <= alignDistance && relativeSpeed <= 6f)
        {
            order.CruiseBrakingThrusterAngle = null;
        }
        else if (order.CruiseBrakingThrusterAngle == null && exceedingBrakingSpeed &&
                 currentBraking.TurnHeading is { } brakingHeading &&
                 (relativeSpeed > 14f || directBrakeNow < control.MaximumAcceleration * 0.15f))
        {
            order.CruiseBrakingThrusterAngle = (-velocityDirection).ToWorldAngle() - brakingHeading;
        }

        if (order.CruiseBrakingThrusterAngle != null && exceedingBrakingSpeed)
        {
            var requestedBraking = Vector2.Dot(acceleration, -velocityDirection);
            var targetBraking = MathF.Min(currentBraking.Acceleration * 0.92f, relativeSpeed / MathF.Max(context.FrameTime * 2f, VelocityResponseTime));
            acceleration -= velocityDirection * MathF.Max(0f, targetBraking - requestedBraking);
        }

        // If the ship is tumbling fast (e.g. post-impact spin at 18 rad/s), let gyros arrest the spin first
        // instead of firing linear engines in random directions as the hull whips around.
        var tumbling = control.AngularAcceleration > 0f &&
                       MathF.Abs(context.ShipBody.AngularVelocity) > SpinRecoveryRateThreshold;
        if (tumbling && !avoidingObstacle)
        {
            acceleration = relativeSpeed > 2f
                ? -velocityDirection * MathF.Min(directBrakeNow, relativeSpeed / VelocityResponseTime)
                : Vector2.Zero;
        }

        var thrustDirection = NormalizedOrZero(acceleration);
        var cruiseDirection = NormalizedOrZero(desiredVelocity - targetCenterVelocity);

        // Begin aligning to docking heading early enough to finish turning by arrival, as long as we
        // have usable braking thrust in the arrival heading and aren't in a flip-and-burn pass.
        var align = finalHeading != null && (damagedApproach
            ? distance <= 0.55f && relativeSpeed <= 0.4f
            : arrivalInRange ||
              order.RotateBeforeArrival && fullLinearThrust && !avoidingObstacle && canBrakeAtArrival && distance <= alignDistance);

        var acceleratingAlongCourse = cruiseDirection != Vector2.Zero &&
                                      Vector2.Dot(thrustDirection, cruiseDirection) > 0.35f;
        var brakingDirectly = exceedingBrakingSpeed && currentBraking.TurnHeading == null && directBrakeNow > 0.25f;
        var requestedAccelMag = MathF.Min(acceleration.Length(), control.MaximumAcceleration);
        var needsThrustHeading = thrustDirection != Vector2.Zero &&
                                 !acceleratingAlongCourse &&
                                 !brakingDirectly &&
                                 Vector2.Dot(control.LimitAcceleration(acceleration), thrustDirection) < requestedAccelMag * 0.25f;

        var angularCommand = 0f;
        if (control.AngularAcceleration > 0f)
        {
            var requested = context.ShipNorthAngle;
            var holdingArrivalHeading = order.CruiseBrakingThrusterAngle == null && align &&
                                        (!damagedApproach || relativeSpeed <= 0.65f) &&
                                        (!needsThrustHeading || arrivalInRange && relativeSpeed <= config.MaxArrivedVel);

            if (order.CruiseBrakingThrusterAngle is { } brakeAngle)
            {
                requested = (-velocityDirection).ToWorldAngle() - brakeAngle;
                // Do not accelerate forward while rotating into a flip-and-burn pass.
                var turnRemaining = Math.Abs(ShortestAngleDistance(context.ShipNorthAngle, requested).Theta);
                if (turnRemaining > 0.65)
                {
                    var forwardComponent = Vector2.Dot(acceleration, velocityDirection);
                    if (forwardComponent > 0f)
                        acceleration -= velocityDirection * forwardComponent;
                }
            }
            else if (holdingArrivalHeading && finalHeading is { } arrivalHeading)
            {
                requested = arrivalHeading;
                if (damagedApproach && control.HasReversibleThrust)
                    acceleration = control.LimitToReversibleAxes(-relativeVelocity / MathF.Max(VelocityResponseTime, 0.16f));
            }
            else if (damagedApproach)
            {
                var approachCourse = cruiseDirection != Vector2.Zero ? cruiseDirection : course;
                // Only enter brake-first mode when velocity is meaningfully misaligned with the course
                // (catches slow sideways orbiting without chattering when flying straight toward staging).
                var drifting = relativeSpeed > 0.04f && Vector2.Dot(velocityDirection, approachCourse) < 0.85f;
                var correctionDirection = drifting ? -velocityDirection : approachCourse;
                if (correctionDirection != Vector2.Zero)
                {
                    requested = control.GetNearestThrustHeading(correctionDirection, requireReverse: control.HasReversibleThrust);
                    var headingError = Math.Abs(ShortestAngleDistance(context.ShipNorthAngle, requested).Theta);
                    if (drifting || headingError > 0.18 || MathF.Abs(relativeRate) > 0.35f)
                        acceleration = -relativeVelocity / MathF.Max(VelocityResponseTime, 0.14f);

                    // Only fire axes that can both accelerate and brake, so an unbalanced thruster
                    // doesn't push the damaged hull into a sideways orbit around staging.
                    if (control.HasReversibleThrust)
                        acceleration = control.LimitToReversibleAxes(acceleration);
                }
            }
            else if (thrustDirection != Vector2.Zero && (cruiseDirection == Vector2.Zero || needsThrustHeading))
            {
                requested = control.GetNearestThrustHeading(thrustDirection, minThrustFraction: 0.45f);
            }
            else if (cruiseDirection != Vector2.Zero)
            {
                requested = control.GetThrustHeading(cruiseDirection);
            }

            var error = (float) ShortestAngleDistance(context.ShipNorthAngle, requested).Theta;
            if (config.AvoidCollisions)
            {
                ApplyLocalProximityAndTurnSafety(
                    ref context,
                    control,
                    shipCenter,
                    localCenter,
                    velocity,
                    desiredVelocity,
                    course,
                    relativeSpeed,
                    ref acceleration,
                    ref error);
            }

            angularCommand = ShuttleFlightControl.GetAngularCommand(
                error,
                holdingArrivalHeading ? relativeRate : context.ShipBody.AngularVelocity,
                control.AngularAcceleration,
                CruiseTurnRate,
                AngularVelocityResponseTime,
                AngularStoppingResponseTime);
        }
        else if (config.AvoidCollisions)
        {
            var dummyError = 0f;
            ApplyLocalProximityAndTurnSafety(
                ref context,
                control,
                shipCenter,
                localCenter,
                velocity,
                desiredVelocity,
                course,
                relativeSpeed,
                ref acceleration,
                ref dummyError);
        }

        input.AccelMul = accelerationMultiplier;
        input.AngularMul = accelerationMultiplier;
        input.Input = control.GetInput(acceleration, angularCommand);
    }

    private float GetAutopilotSpeed(
        ShipSteererComponent order,
        ref SteeringContext context,
        in SteeringConfig config,
        ShuttleFlightControl control,
        Vector2 course,
        Vector2 velocityDirection,
        float distance,
        float remaining,
        float relativeSpeed,
        float cruiseSpeed,
        (float Acceleration, float ResponseTime, Angle? TurnHeading) currentBraking,
        out float horizon)
    {
        var angularVelocity = context.ShipBody.AngularVelocity;
        var plannedSpeedCap = control.GetMaxSafeStoppingSpeed(
            -course,
            remaining,
            cruiseSpeed,
            angularVelocity,
            CruiseTurnRate,
            StoppingResponseTime,
            safetyFactor: 0.86f);

        // If we're already moving fast along a different direction, also respect our actual braking capability.
        if (relativeSpeed > 2f && velocityDirection != Vector2.Zero && Vector2.Dot(velocityDirection, course) > 0.25f)
        {
            var actualSpeedCap = control.GetMaxSafeStoppingSpeed(
                -velocityDirection,
                remaining,
                cruiseSpeed,
                angularVelocity,
                CruiseTurnRate,
                StoppingResponseTime,
                safetyFactor: 0.86f);
            var desiredSpeed = MathF.Min(actualSpeedCap, plannedSpeedCap);

            var effDecel = MathF.Max(0.05f, currentBraking.Acceleration * 0.86f);
            var brakePath = relativeSpeed * currentBraking.ResponseTime + relativeSpeed * relativeSpeed / (2f * effDecel);
            var dynamicScan = MathF.Max(CruiseObstacleSearchRange, brakePath * 1.35f + 250f);
            if (config.AvoidCollisions)
            {
                var horizonSpeedCap = control.GetMaxSafeStoppingSpeed(
                    -velocityDirection,
                    dynamicScan * 0.85f,
                    cruiseSpeed,
                    angularVelocity,
                    CruiseTurnRate,
                    StoppingResponseTime,
                    safetyFactor: 0.84f);
                desiredSpeed = MathF.Min(desiredSpeed, horizonSpeedCap);
            }

            var searchConfig = config with { MaxObstructorDistance = dynamicScan };
            ScanForObstacles(ref context, searchConfig,
                new BrakeContext(currentBraking.Acceleration, brakePath, brakePath), course);
            var stoppingTime = currentBraking.ResponseTime + relativeSpeed / effDecel;
            horizon = Math.Clamp(2.5f + stoppingTime, 4.5f, 32f);
            return desiredSpeed;
        }

        var baseDecel = MathF.Max(0.05f, currentBraking.Acceleration * 0.86f);
        var currentBrakePath = relativeSpeed > 0f
            ? relativeSpeed * currentBraking.ResponseTime + relativeSpeed * relativeSpeed / (2f * baseDecel)
            : 0f;
        var scanRange = MathF.Max(CruiseObstacleSearchRange, currentBrakePath * 1.35f + 250f);
        var scanCfg = config with { MaxObstructorDistance = scanRange };
        ScanForObstacles(ref context, scanCfg,
            new BrakeContext(relativeSpeed > 0f ? MathF.Max(0.1f, currentBraking.Acceleration) : 1f, currentBrakePath, currentBrakePath), course);
        horizon = Math.Clamp(2.5f + currentBraking.ResponseTime + relativeSpeed / baseDecel, 4.5f, 32f);
        return plannedSpeedCap;
    }

    private static (float Acceleration, float ResponseTime, Angle? TurnHeading) GetCruiseBraking(
        ShuttleFlightControl control,
        Vector2 direction,
        float speed,
        float angularVelocity)
    {
        return control.GetBestBraking(
            direction,
            speed,
            angularVelocity,
            CruiseTurnRate,
            StoppingResponseTime,
            safetyFactor: 0.86f);
    }

    private bool IsTargetCorridorClear(ref SteeringContext context, EntityUid targetGrid)
    {
        var localCenter = context.ShipBody.LocalCenter;
        var shipCenter = context.ShipPos.Position + context.ShipNorthAngle.RotateVec(localCenter);
        var finalRotation = context.TargetApproachAngle ?? context.ShipNorthAngle;
        var targetCenter = context.DestMapPos.Position +
                           (context.TargetApproachAngle is { } approachHeading
                               ? approachHeading.RotateVec(localCenter)
                               : Vector2.Zero);

        var travel = targetCenter - shipCenter;
        var length = travel.Length();
        if (length <= 0.05f)
            return true;

        if (!TryComp<FixturesComponent>(context.ShipUid, out var shipFixtures) || shipFixtures.Fixtures.Count == 0 ||
            !_physQuery.TryComp(targetGrid, out var targetBody))
        {
            return true;
        }

        var bounds = context.ShipGrid.LocalAABB;
        var maxCornerRadius = ShuttleFlightControl.GetMaxCornerRadius(bounds, localCenter);
        var targetWorldAabb = _physics.GetWorldAABB(targetGrid, body: targetBody).Enlarged(maxCornerRadius + 2f);
        if (!TryClipSegmentToBox(shipCenter, targetCenter, targetWorldAabb, out var tEnter, out var tExit))
            return true;

        var mapId = context.ShipPos.MapId;
        var clippedLength = (tExit - tEnter) * length;
        var minExtent = MathF.Min(bounds.Width, bounds.Height);
        var stepSize = Math.Clamp(minExtent * 0.5f, 1.5f, 4f);
        var steps = Math.Clamp((int) MathF.Ceiling(clippedLength / stepSize), 1, 48);
        var headingDiff = Math.Abs(ShortestAngleDistance(context.ShipNorthAngle, finalRotation).Theta);

        for (var i = 0; i <= steps; i++)
        {
            var t = tEnter + (tExit - tEnter) * (i / (float) steps);
            var sampleCenter = Vector2.Lerp(shipCenter, targetCenter, t);
            if (!IsHullPoseClear(mapId, context.ShipUid, shipFixtures, localCenter, sampleCenter, finalRotation, out _, onlyGrid: targetGrid))
                return false;

            // Near the ship's current position, also verify our actual current heading clears the target
            // so an elongated ship doesn't drop obstacle avoidance while still angled into an outcrop.
            if (headingDiff > 0.25 && t * length <= MathF.Max(20f, maxCornerRadius * 1.5f) &&
                !IsHullPoseClear(mapId, context.ShipUid, shipFixtures, localCenter, sampleCenter, context.ShipNorthAngle, out _, onlyGrid: targetGrid))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Exact fixture-level proximity and swept-rotation guard. Only triggers when another grid is
    /// physically within the ship's rotational/braking envelope, preventing elongated or asymmetric
    /// ships from swinging their bow/stern into concave station outcrops during turns.
    /// </summary>
    private void ApplyLocalProximityAndTurnSafety(
        ref SteeringContext context,
        ShuttleFlightControl control,
        Vector2 shipCenter,
        Vector2 localCenter,
        Vector2 velocity,
        Vector2 desiredVelocity,
        Vector2 course,
        float relativeSpeed,
        ref Vector2 acceleration,
        ref float turnError)
    {
        if (!TryComp<FixturesComponent>(context.ShipUid, out var shipFixtures) || shipFixtures.Fixtures.Count == 0)
            return;

        var mapId = context.ShipPos.MapId;
        var maxCornerRadius = ShuttleFlightControl.GetMaxCornerRadius(context.ShipGrid.LocalAABB, localCenter);
        var velDirNow = NormalizedOrZero(velocity);
        var currentBrakeAccel = velDirNow != Vector2.Zero
            ? MathF.Max(0.35f, control.GetAxisAcceleration(-velDirNow) * 0.85f)
            : MathF.Max(0.35f, control.MaximumAcceleration * 0.85f);
        var currentStopDist = velocity.Length() * 0.25f + velocity.LengthSquared() / (2f * currentBrakeAccel);
        var broadphaseRadius = maxCornerRadius + MathF.Min(MathF.Max(currentStopDist + 4f, relativeSpeed * 0.65f + 3.5f), 48f);
        var queryBox = new Box2(
            shipCenter - new Vector2(broadphaseRadius, broadphaseRadius),
            shipCenter + new Vector2(broadphaseRadius, broadphaseRadius));

        _nearbyCheckGrids.Clear();
        _mapMan.FindGridsIntersecting(mapId, queryBox, ref _nearbyCheckGrids, approx: true, includeMap: false);
        var hasOtherGrid = false;
        foreach (var grid in _nearbyCheckGrids)
        {
            if (grid.Owner != context.ShipUid && _physQuery.HasComponent(grid.Owner))
            {
                hasOtherGrid = true;
                break;
            }
        }

        if (!hasOtherGrid)
            return;

        var currentRot = context.ShipNorthAngle;
        var currentlyClear = IsHullPoseClear(mapId, context.ShipUid, shipFixtures, localCenter, shipCenter, currentRot, out var overlappingGrid);

        // 1. Exact local curved-rollout guard: catches nearby concave bay walls or target-station
        //    protrusions using actual thruster-limited acceleration from current velocity.
        if (currentlyClear && (desiredVelocity.LengthSquared() > 0.04f || velocity.LengthSquared() > 0.04f))
        {
            if (!IsCurvedRolloutClear(mapId, context.ShipUid, shipFixtures, control, localCenter, shipCenter, currentRot, velocity, desiredVelocity, out var hitGrid))
            {
                var obstacleVel = _physQuery.TryComp(hitGrid, out var hitBody) ? hitBody.LinearVelocity : Vector2.Zero;
                var hitAabb = _physics.GetWorldAABB(hitGrid);
                var nearestPoint = Vector2.Clamp(shipCenter, hitAabb.BottomLeft, hitAabb.TopRight);
                var awayFromHit = NormalizedOrZero(shipCenter - nearestPoint);
                if (awayFromHit == Vector2.Zero)
                    awayFromHit = NormalizedOrZero(shipCenter - hitAabb.Center);

                var relDesired = desiredVelocity - obstacleVel;
                var baseSpeed = MathF.Min(AntiTunnelSafeSpeed, MathF.Max(relDesired.Length(), (velocity - obstacleVel).Length()));
                var baseDir = NormalizedOrZero(relDesired != Vector2.Zero ? relDesired : velocity - obstacleVel);
                if (baseDir == Vector2.Zero)
                    baseDir = awayFromHit;

                var foundDeflect = false;
                var bestDeflect = obstacleVel + awayFromHit * MathF.Min(4f, MathF.Max(2f, baseSpeed));
                foreach (var deg in LocalDeflectAngles)
                {
                    var sides = deg >= 180f ? 1 : 2;
                    for (var s = 0; s < sides && !foundDeflect; s++)
                    {
                        var dir = Angle.FromDegrees(deg * (s == 0 ? 1f : -1f)).RotateVec(baseDir);
                        if (awayFromHit != Vector2.Zero && Vector2.Dot(dir, awayFromHit) < -0.05f)
                            continue;

                        foreach (var throttle in AvoidanceThrottles)
                        {
                            var candVel = obstacleVel + dir * (baseSpeed * throttle);
                            if (!IsCurvedRolloutClear(mapId, context.ShipUid, shipFixtures, control, localCenter, shipCenter, currentRot, velocity, candVel, out _))
                                continue;

                            bestDeflect = candVel;
                            foundDeflect = true;
                            break;
                        }
                    }

                    if (foundDeflect)
                        break;
                }

                desiredVelocity = bestDeflect;
                acceleration = (bestDeflect - velocity) / VelocityResponseTime;
            }
        }

        // 2. Exact swept-rotation guard around COM: never swing bow/stern through a nearby station wall.
        if (control.AngularAcceleration <= 0f)
            return;

        var omega = context.ShipBody.AngularVelocity;
        if (MathF.Abs(turnError) <= 0.03f && MathF.Abs(omega) <= 0.08f)
            return;

        // Compute a small 0.9 m clearance bias toward the nearest nearby grid so we never initiate a turn
        // when the swinging bow/stern would pass millimetres from a wall.
        var wallBias = Vector2.Zero;
        var nearestGridDistSq = float.PositiveInfinity;
        foreach (var grid in _nearbyCheckGrids)
        {
            if (grid.Owner == context.ShipUid || !_physQuery.HasComponent(grid.Owner))
                continue;

            var gridAabb = _physics.GetWorldAABB(grid.Owner);
            var closestPoint = Vector2.Clamp(shipCenter, gridAabb.BottomLeft, gridAabb.TopRight);
            var toGrid = closestPoint - shipCenter;
            var distSq = toGrid.LengthSquared();
            if (distSq < nearestGridDistSq && distSq > 0.0001f)
            {
                nearestGridDistSq = distSq;
                wallBias = Vector2.Normalize(toGrid) * 0.9f;
            }
        }

        // Include angular stopping overshoot so a fast spin doesn't carry the hull into contact.
        var stopOvershoot = omega * MathF.Abs(omega) / (2f * MathF.Max(0.25f, control.AngularAcceleration));
        var checkDelta = MathF.Abs(turnError) >= MathF.Abs(stopOvershoot) && MathF.Sign(turnError) == MathF.Sign(stopOvershoot)
            ? turnError
            : MathF.Abs(turnError) > 0.03f ? turnError : stopOvershoot;

        var turnVel = Vector2.Lerp(velocity, desiredVelocity, 0.5f);
        var checkTurnTime = Math.Clamp(
            ShuttleFlightControl.EstimateTurnTime(checkDelta, omega, control.AngularAcceleration, CruiseTurnRate),
            0.25f,
            1.8f);

        var blockingGrid = overlappingGrid;
        if (currentlyClear &&
            IsRotationArcClear(mapId, context.ShipUid, shipFixtures, localCenter, shipCenter, currentRot, checkDelta, turnVel, checkTurnTime, wallBias, out blockingGrid))
        {
            return;
        }

        // If the shortest turn collides, check whether turning the opposite way swings the hull clear.
        if (currentlyClear && MathF.Abs(turnError) > 0.14f)
        {
            var altDelta = turnError > 0f ? turnError - MathF.Tau : turnError + MathF.Tau;
            var altTurnTime = Math.Clamp(
                ShuttleFlightControl.EstimateTurnTime(altDelta, omega, control.AngularAcceleration, CruiseTurnRate),
                0.25f,
                2.2f);
            if (IsRotationArcClear(mapId, context.ShipUid, shipFixtures, localCenter, shipCenter, currentRot, altDelta, turnVel, altTurnTime, wallBias, out _))
            {
                turnError = altDelta;
                return;
            }
        }

        // Neither rotation direction fits here: arrest spin immediately and translate clear of the wall first.
        turnError = 0f;
        if (blockingGrid == EntityUid.Invalid)
            return;

        var blockVel = _physQuery.TryComp(blockingGrid, out var blockBody) ? blockBody.LinearVelocity : Vector2.Zero;
        var blockAabb = _physics.GetWorldAABB(blockingGrid);
        var nearestWallPoint = Vector2.Clamp(shipCenter, blockAabb.BottomLeft, blockAabb.TopRight);
        var away = NormalizedOrZero(shipCenter - nearestWallPoint);
        if (away == Vector2.Zero)
            away = NormalizedOrZero(shipCenter - blockAabb.Center);
        if (away == Vector2.Zero)
            away = course != Vector2.Zero ? -course : Vector2.UnitY;

        var escapeDir = FindClearEscapeDirection(
            mapId,
            context.ShipUid,
            shipFixtures,
            localCenter,
            shipCenter,
            currentRot,
            away,
            course,
            MathF.Max(3.5f, maxCornerRadius * 0.45f));
        var escapeSpeed = Math.Clamp(control.GetAxisAcceleration(escapeDir) * 0.85f, 3f, 6.5f);
        acceleration = (blockVel + escapeDir * escapeSpeed - velocity) / VelocityResponseTime;
    }

    private bool IsCurvedRolloutClear(
        MapId mapId,
        EntityUid shipUid,
        FixturesComponent shipFixtures,
        ShuttleFlightControl control,
        Vector2 localCenter,
        Vector2 shipCenter,
        Angle rotation,
        Vector2 currentVelocity,
        Vector2 desiredVelocity,
        out EntityUid hitGrid)
    {
        hitGrid = EntityUid.Invalid;
        const float dt = 0.25f;
        const int steps = 5; // 1.25 s curved physics rollout
        var pos = shipCenter;
        var vel = currentVelocity;
        for (var i = 0; i < steps; i++)
        {
            var accel = control.LimitAcceleration((desiredVelocity - vel) / MathF.Max(dt, VelocityResponseTime * 2f));
            pos += (vel + accel * (dt * 0.5f)) * dt;
            vel += accel * dt;
            var leadDir = NormalizedOrZero(vel);
            if (!IsHullPoseClear(mapId, shipUid, shipFixtures, localCenter, pos + leadDir * 0.5f, rotation, out hitGrid))
                return false;
        }

        return true;
    }

    private bool IsRotationArcClear(
        MapId mapId,
        EntityUid shipUid,
        FixturesComponent shipFixtures,
        Vector2 localCenter,
        Vector2 shipCenter,
        Angle startRotation,
        float deltaRadians,
        Vector2 shipVelocity,
        float turnDuration,
        Vector2 wallBias,
        out EntityUid hitGrid)
    {
        hitGrid = EntityUid.Invalid;
        var absDelta = MathF.Abs(deltaRadians);
        if (absDelta <= 0.01f)
            return true;

        var steps = Math.Clamp((int) MathF.Ceiling(absDelta / TurnClearanceAngleStep), 1, 60);
        var lookaheadDrift = shipVelocity * turnDuration;
        var hasDrift = lookaheadDrift.LengthSquared() > 0.09f;
        var hasBias = wallBias.LengthSquared() > 0.01f;

        for (var i = 1; i <= steps; i++)
        {
            var frac = i / (float) steps;
            var testRot = startRotation + deltaRadians * frac;
            if (!IsHullPoseClear(mapId, shipUid, shipFixtures, localCenter, shipCenter, testRot, out hitGrid))
                return false;

            if (hasBias && !IsHullPoseClear(mapId, shipUid, shipFixtures, localCenter, shipCenter + wallBias, testRot, out hitGrid))
                return false;

            if (hasDrift && !IsHullPoseClear(mapId, shipUid, shipFixtures, localCenter, shipCenter + lookaheadDrift * frac, testRot, out hitGrid))
                return false;
        }

        return true;
    }

    private Vector2 FindClearEscapeDirection(
        MapId mapId,
        EntityUid shipUid,
        FixturesComponent shipFixtures,
        Vector2 localCenter,
        Vector2 shipCenter,
        Angle rotation,
        Vector2 away,
        Vector2 course,
        float stepDistance)
    {
        var bestDir = away;
        var bestScore = float.NegativeInfinity;
        for (var i = 0; i < 8; i++)
        {
            var dir = Angle.FromDegrees(i * 45f).RotateVec(away);
            var midClear = IsHullPoseClear(mapId, shipUid, shipFixtures, localCenter, shipCenter + dir * (stepDistance * 0.5f), rotation, out _);
            var endClear = IsHullPoseClear(mapId, shipUid, shipFixtures, localCenter, shipCenter + dir * stepDistance, rotation, out _);
            if (!midClear || !endClear)
                continue;

            var score = Vector2.Dot(dir, away) * 1.4f + Vector2.Dot(dir, course) * 0.5f;
            if (score > bestScore)
            {
                bestScore = score;
                bestDir = dir;
            }
        }

        return bestDir;
    }

    private bool IsHullPoseClear(
        MapId mapId,
        EntityUid shipUid,
        FixturesComponent shipFixtures,
        Vector2 localCenter,
        Vector2 centerPosition,
        Angle rotation,
        out EntityUid hitGrid,
        EntityUid? onlyGrid = null)
    {
        hitGrid = EntityUid.Invalid;
        var origin = centerPosition - rotation.RotateVec(localCenter);
        var pose = new PhysicsTransform(origin, rotation);

        foreach (var fixture in shipFixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            _turnCheckGrids.Clear();
            _maps.FindGridsIntersecting(
                mapId,
                fixture.Shape,
                pose,
                ref _turnCheckGrids,
                approx: false,
                includeMap: false);

            foreach (var grid in _turnCheckGrids)
            {
                if (grid.Owner == shipUid)
                    continue;
                if (onlyGrid != null && grid.Owner != onlyGrid.Value)
                    continue;
                if (!_physQuery.HasComponent(grid.Owner))
                    continue;

                hitGrid = grid.Owner;
                return false;
            }
        }

        return true;
    }

    private static bool TryClipSegmentToBox(Vector2 start, Vector2 end, Box2 box, out float tEnter, out float tExit)
    {
        tEnter = 0f;
        tExit = 1f;
        var delta = end - start;

        if (!ClipAxis(delta.X, start.X, box.Left, box.Right, ref tEnter, ref tExit))
            return false;
        if (!ClipAxis(delta.Y, start.Y, box.Bottom, box.Top, ref tEnter, ref tExit))
            return false;

        return tExit >= tEnter;
    }

    private static bool ClipAxis(float d, float origin, float min, float max, ref float tEnter, ref float tExit)
    {
        if (MathF.Abs(d) <= 1e-6f)
            return origin >= min && origin <= max;

        var inv = 1f / d;
        var t1 = (min - origin) * inv;
        var t2 = (max - origin) * inv;
        if (t1 > t2)
            (t1, t2) = (t2, t1);

        tEnter = MathF.Max(tEnter, t1);
        tExit = MathF.Min(tExit, t2);
        return tEnter <= tExit;
    }

    private Vector2 SelectAutopilotVelocity(
        ref SteeringContext context,
        ShuttleFlightControl control,
        Vector2 shipCenter,
        Vector2 velocity,
        Vector2 preferred,
        Vector2 targetVelocity,
        Vector2 previousCourse,
        float horizon,
        float avoidanceSpeedLimit,
        out bool avoidingObstacle)
    {
        avoidingObstacle = false;
        if (_avoidEnts.Count == 0)
            return preferred;

        // Center the clearance circle on COM using the true maximum corner sweep radius,
        // so rotating an elongated or off-center ship never pokes outside the avoided radius.
        var bounds = context.ShipGrid.LocalAABB;
        var radius = ShuttleFlightControl.GetMaxCornerRadius(bounds, context.ShipBody.LocalCenter) + 2.8f;
        TryComp<FixturesComponent>(context.ShipUid, out var shipFixtures);

        if (float.IsPositiveInfinity(GetAutopilotImpactTime(ref context, shipFixtures, shipCenter, control, velocity, preferred, radius, horizon)))
            return preferred;

        avoidingObstacle = true;
        var referenceVelocity = float.IsFinite(avoidanceSpeedLimit) ? targetVelocity : Vector2.Zero;
        var relativePreferred = preferred - referenceVelocity;
        var relativeVelocity = velocity - referenceVelocity;
        var speed = MathF.Min(avoidanceSpeedLimit, MathF.Max(relativePreferred.Length(), relativeVelocity.Length() * 0.92f));
        if (speed > AntiTunnelSafeSpeed)
            speed = AntiTunnelSafeSpeed;

        var course = NormalizedOrZero(relativePreferred == Vector2.Zero ? relativeVelocity : relativePreferred);
        var best = referenceVelocity;
        var latestImpact = GetAutopilotImpactTime(ref context, shipFixtures, shipCenter, control, velocity, best, radius, horizon);
        var bestCost = speed * speed * 1.25f;

        // Try small glancing deviations first at full speed so the shuttle curves smoothly around
        // obstacles while keeping forward momentum, with slight hysteresis to prevent side-flipping.
        foreach (var degrees in AvoidanceAngleOffsets)
        {
            var sides = degrees >= 180f ? 1 : 2;
            for (var s = 0; s < sides; s++)
            {
                var side = s == 0 ? 1f : -1f;
                var direction = Angle.FromDegrees(degrees * side).RotateVec(course);
                foreach (var throttle in AvoidanceThrottles)
                {
                    var candidate = referenceVelocity + direction * (speed * throttle);
                    var cost = Vector2.DistanceSquared(candidate, preferred) +
                        (1f - Vector2.Dot(previousCourse, direction)) * speed * speed * 0.10f;
                    if (float.IsPositiveInfinity(latestImpact) && cost >= bestCost)
                        continue;

                    var impact = GetAutopilotImpactTime(ref context, shipFixtures, shipCenter, control, velocity, candidate, radius, horizon);
                    if (impact < latestImpact || impact == latestImpact && cost >= bestCost)
                        continue;

                    best = candidate;
                    latestImpact = impact;
                    bestCost = cost;
                }
            }
        }

        return best;
    }

    private float GetAutopilotImpactTime(
        ref SteeringContext context,
        FixturesComponent? shipFixtures,
        Vector2 position,
        ShuttleFlightControl control,
        Vector2 velocity,
        Vector2 desiredVelocity,
        float radius,
        float horizon)
    {
        var steps = Math.Clamp((int) MathF.Ceiling(horizon / 0.65f), 12, 48);
        var step = horizon / steps;
        Span<Vector2> trajectory = stackalloc Vector2[steps + 1];
        trajectory[0] = position;
        var predictedVelocity = velocity;
        var rolloutResponse = MathF.Max(step, VelocityResponseTime * 2f);
        for (var i = 0; i < steps; i++)
        {
            var acceleration = control.LimitAcceleration((desiredVelocity - predictedVelocity) / rolloutResponse);
            trajectory[i + 1] = trajectory[i] + (predictedVelocity + acceleration * (step * 0.5f)) * step;
            predictedVelocity += acceleration * step;
        }

        var mapId = context.ShipPos.MapId;
        var localCenter = context.ShipBody.LocalCenter;
        var currentRot = context.ShipNorthAngle;
        var canExactCheck = shipFixtures != null && shipFixtures.Fixtures.Count > 0;

        var impact = float.PositiveInfinity;
        foreach (var obstacle in _avoidEnts)
        {
            var clearance = radius + obstacle.Radius;
            var clearanceSquared = clearance * clearance;
            var relativePosition = position - obstacle.Pos;
            var obstacleVelocity = obstacle.Ent.Comp2.LinearVelocity;
            Box2? obsAabb = null;
            for (var i = 0; i < steps && i * step < impact; i++)
            {
                var displacement = trajectory[i + 1] - trajectory[i] - obstacleVelocity * step;
                var lengthSquared = displacement.LengthSquared();
                var along = Vector2.Dot(relativePosition, displacement);
                var progress = lengthSquared > 0f ? Math.Clamp(-along / lengthSquared, 0f, 1f) : 0f;
                var closest = relativePosition + displacement * progress;
                if (closest.LengthSquared() <= clearanceSquared)
                {
                    // The ship can already be inside a conservative bounding circle of a long wall or
                    // concave station. When moving away from its center (along > 0), verify against
                    // exact grid fixtures so a curved trajectory cannot bend into a wall wing.
                    if (along <= 0f)
                    {
                        impact = MathF.Min(impact, (i + progress) * step);
                        break;
                    }

                    if (canExactCheck && (i + 1) * step <= 4.5f)
                    {
                        obsAabb ??= _physics.GetWorldAABB(obstacle.Ent.Owner, body: obstacle.Ent.Comp2, xform: obstacle.Ent.Comp1).Enlarged(radius);
                        var probePos = trajectory[i + 1] - obstacleVelocity * ((i + 1) * step);
                        var dispDir = NormalizedOrZero(displacement);
                        if (obsAabb.Value.Contains(probePos) &&
                            !IsHullPoseClear(mapId, context.ShipUid, shipFixtures!, localCenter, probePos + dispDir * 0.9f, currentRot, out _, onlyGrid: obstacle.Ent.Owner))
                        {
                            impact = MathF.Min(impact, (i + 1) * step);
                            break;
                        }
                    }
                }

                relativePosition += displacement;
            }
        }

        return impact;
    }
}
