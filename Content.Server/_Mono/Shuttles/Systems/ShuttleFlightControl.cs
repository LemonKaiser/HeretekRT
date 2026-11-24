using System.Numerics;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.Server._Mono.Shuttles;

/// <summary>Acceleration available from the currently working shuttle thrusters.</summary>
internal readonly struct ShuttleFlightControl(ShuttleComponent shuttle, PhysicsComponent body, Angle rotation, float multiplier)
{
    private readonly float _scale = shuttle.BaseThrusterMultiplier * multiplier;

    public float AngularAcceleration => MathF.Max(0f, shuttle.AngularThrust * body.InvI * _scale);
    public float MaximumAcceleration => MathF.Max(MathF.Max(shuttle.LinearThrust[0], shuttle.LinearThrust[1]),
        MathF.Max(shuttle.LinearThrust[2], shuttle.LinearThrust[3])) * _scale * body.InvMass;

    public bool HasReversibleThrust => shuttle.LinearThrust[0] > 0f && shuttle.LinearThrust[2] > 0f ||
                                      shuttle.LinearThrust[1] > 0f && shuttle.LinearThrust[3] > 0f;

    private Vector2 GetLocalCapacity(Vector2 direction) => new Vector2(
        shuttle.LinearThrust[direction.X > 0f ? 1 : 3],
        shuttle.LinearThrust[direction.Y > 0f ? 2 : 0]) * (_scale * body.InvMass);

    public Vector2 LimitAcceleration(Vector2 requested, Angle? heading = null)
    {
        var attitude = heading ?? rotation;
        var local = (-attitude).RotateVec(requested);
        var capacity = GetLocalCapacity(local);
        return attitude.RotateVec(Vector2.Clamp(local, -capacity, capacity));
    }

    /// <summary>
    /// Restricts linear acceleration to thruster axes that have working propulsion in both
    /// directions. Prevents an unbalanced surviving thruster from injecting lateral drift that
    /// its broken opposite partner cannot cancel during damaged precision maneuvers.
    /// </summary>
    public Vector2 LimitToReversibleAxes(Vector2 requested)
    {
        var local = (-rotation).RotateVec(requested);
        if (shuttle.LinearThrust[1] <= 0f || shuttle.LinearThrust[3] <= 0f)
            local.X = 0f;
        if (shuttle.LinearThrust[0] <= 0f || shuttle.LinearThrust[2] <= 0f)
            local.Y = 0f;
        var capacity = GetLocalCapacity(local);
        return rotation.RotateVec(Vector2.Clamp(local, -capacity, capacity));
    }

    public float GetAxisAcceleration(Vector2 direction, Angle? heading = null)
    {
        if (direction.LengthSquared() <= float.Epsilon)
            return 0f;

        var unit = Vector2.Normalize(direction);
        return MathF.Max(0f, Vector2.Dot(LimitAcceleration(unit * MaximumAcceleration, heading), unit));
    }

    public ShuttleInput GetInput(Vector2 acceleration, float angularAcceleration)
    {
        var local = (-rotation).RotateVec(acceleration);
        var capacity = GetLocalCapacity(local);
        var strafe = new Vector2(GetFraction(local.X, capacity.X), GetFraction(local.Y, capacity.Y));
        return new ShuttleInput(strafe, -GetFraction(angularAcceleration, AngularAcceleration), 0f, ProportionalThrust: true);
    }

    public Angle GetThrustHeading(Vector2 worldDirection)
    {
        // Prefer the fore thrusters while they can meaningfully contribute. With a damaged
        // or strongly asymmetric shuttle, turn the strongest working axis into the requested direction.
        var fore = shuttle.LinearThrust[2];
        var strongest = MathF.Max(MathF.Max(shuttle.LinearThrust[0], shuttle.LinearThrust[1]),
            MathF.Max(fore, shuttle.LinearThrust[3]));
        var local = Vector2.UnitY;
        if (fore < strongest * 0.45f)
        {
            if (strongest == shuttle.LinearThrust[0])
                local = -Vector2.UnitY;
            else if (strongest == shuttle.LinearThrust[1])
                local = Vector2.UnitX;
            else
                local = -Vector2.UnitX;
        }

        return worldDirection.ToAngle() - local.ToAngle();
    }

    public Angle GetNearestThrustHeading(Vector2 direction, bool requireReverse = false, float minThrustFraction = 0f)
    {
        var strongest = MathF.Max(MathF.Max(shuttle.LinearThrust[0], shuttle.LinearThrust[1]),
            MathF.Max(shuttle.LinearThrust[2], shuttle.LinearThrust[3]));
        var threshold = strongest * minThrustFraction;
        var best = rotation;
        var shortestTurn = double.PositiveInfinity;
        var worldAngle = direction.ToWorldAngle();
        for (var i = 0; i < shuttle.LinearThrust.Length; i++)
        {
            if (shuttle.LinearThrust[i] <= 0f ||
                shuttle.LinearThrust[i] < threshold ||
                requireReverse && shuttle.LinearThrust[(i + 2) % 4] <= 0f)
                continue;

            var heading = worldAngle - ((Direction) (i * 2)).ToAngle();
            var turn = Math.Abs(Angle.ShortestDistance(rotation, heading).Theta);
            if (turn >= shortestTurn)
                continue;

            best = heading;
            shortestTurn = turn;
        }

        if (double.IsPositiveInfinity(shortestTurn) && minThrustFraction > 0f)
            return GetNearestThrustHeading(direction, requireReverse, 0f);

        return best;
    }

    /// <summary>
    /// Computes the highest safe cruise speed that can stop within <paramref name="distance"/> along
    /// <paramref name="brakeDirection"/> using either direct retro-thrust or a flip-and-burn pass.
    /// </summary>
    public float GetMaxSafeStoppingSpeed(
        Vector2 brakeDirection,
        float distance,
        float maxSpeed,
        float angularVelocity,
        float maxTurnRate,
        float baseResponseTime,
        float safetyFactor = 0.86f)
    {
        if (distance <= 0f || maxSpeed <= 0f)
            return 0f;

        var unit = brakeDirection.LengthSquared() > float.Epsilon ? Vector2.Normalize(brakeDirection) : Vector2.Zero;
        var directAccel = unit != Vector2.Zero ? GetAxisAcceleration(unit) : MaximumAcceleration;
        var bestSpeed = StoppingSpeed(distance, directAccel, maxSpeed, baseResponseTime, safetyFactor);
        if (unit == Vector2.Zero || AngularAcceleration <= 0f || bestSpeed >= maxSpeed)
            return bestSpeed;

        var worldAngle = unit.ToWorldAngle();
        for (var i = 0; i < shuttle.LinearThrust.Length; i++)
        {
            if (shuttle.LinearThrust[i] <= 0f)
                continue;

            var candidateAccel = shuttle.LinearThrust[i] * (_scale * body.InvMass);
            if (candidateAccel <= directAccel * 1.05f)
                continue;

            var candidateHeading = worldAngle - ((Direction) (i * 2)).ToAngle();
            var turnDelta = (float) Angle.ShortestDistance(rotation, candidateHeading).Theta;
            var turnTime = EstimateTurnTime(turnDelta, angularVelocity, AngularAcceleration, maxTurnRate);
            var candidateSpeed = StoppingSpeed(distance, candidateAccel, maxSpeed, baseResponseTime + turnTime, safetyFactor);
            if (candidateSpeed > bestSpeed)
                bestSpeed = candidateSpeed;
        }

        return bestSpeed;
    }

    /// <summary>
    /// Chooses the fastest braking axis for <paramref name="brakeDirection"/>: keeps the current
    /// attitude when direct retro-thrust is competitive, or flips the strongest economical thruster
    /// against motion when doing so shortens the stopping distance (broken brakes or asymmetric ships).
    /// </summary>
    public (float Acceleration, float ResponseTime, Angle? TurnHeading) GetBestBraking(
        Vector2 brakeDirection,
        float speed,
        float angularVelocity,
        float maxTurnRate,
        float baseResponseTime,
        float safetyFactor = 0.86f)
    {
        if (brakeDirection.LengthSquared() <= float.Epsilon)
            return (MaximumAcceleration, baseResponseTime, null);

        var unit = Vector2.Normalize(brakeDirection);
        var directAccel = GetAxisAcceleration(unit);
        if (AngularAcceleration <= 0f)
            return (directAccel, baseResponseTime, null);

        var evalSpeed = MathF.Max(speed, 12f);
        var directEffective = directAccel * safetyFactor;
        var bestDistance = directEffective > 0.001f
            ? evalSpeed * baseResponseTime + evalSpeed * evalSpeed / (2f * directEffective)
            : float.PositiveInfinity;

        var bestAccel = directAccel;
        var bestResponse = baseResponseTime;
        Angle? bestHeading = null;

        var worldAngle = unit.ToWorldAngle();
        for (var i = 0; i < shuttle.LinearThrust.Length; i++)
        {
            if (shuttle.LinearThrust[i] <= 0f)
                continue;

            var candidateHeading = worldAngle - ((Direction) (i * 2)).ToAngle();
            var candidateAccel = shuttle.LinearThrust[i] * (_scale * body.InvMass);
            var candidateEffective = candidateAccel * safetyFactor;
            if (candidateEffective <= 0.001f)
                continue;

            // Only consider flipping if direct retro-thrust is missing or substantially weaker.
            if (directAccel >= candidateAccel * 0.65f)
                continue;

            var turnDelta = (float) Angle.ShortestDistance(rotation, candidateHeading).Theta;
            var turnTime = EstimateTurnTime(turnDelta, angularVelocity, AngularAcceleration, maxTurnRate);
            var candidateResponse = baseResponseTime + turnTime;
            var candidateDistance = evalSpeed * candidateResponse + evalSpeed * evalSpeed / (2f * candidateEffective);

            // Require a meaningful stopping-distance advantage (or dead direct brakes) to justify a flip.
            var requiredRatio = directAccel < MaximumAcceleration * 0.2f ? 1f : 0.82f;
            if (candidateDistance >= bestDistance * requiredRatio)
                continue;

            bestDistance = candidateDistance;
            bestAccel = candidateAccel;
            bestResponse = candidateResponse;
            bestHeading = candidateHeading;
        }

        return (bestAccel, bestResponse, bestHeading);
    }

    /// <summary>
    /// Exact trapezoidal / triangular time-optimal rotation estimate, accounting for initial angular velocity.
    /// </summary>
    public static float EstimateTurnTime(float angleError, float angularVelocity, float angularAcceleration, float maxTurnRate)
    {
        if (angularAcceleration <= 0f || maxTurnRate <= 0f)
            return float.PositiveInfinity;

        var absAngle = MathF.Abs(angleError);
        if (absAngle <= 0.01f)
            return 0f;

        // Time to cancel any spin currently opposing the desired turn direction.
        var sign = MathF.Sign(angleError);
        var opposingRate = MathF.Max(0f, -angularVelocity * sign);
        var recoveryTime = opposingRate / angularAcceleration;
        var effectiveAngle = absAngle + 0.5f * opposingRate * recoveryTime;

        var triangularLimit = maxTurnRate * maxTurnRate / angularAcceleration;
        var maneuverTime = effectiveAngle <= triangularLimit
            ? 2f * MathF.Sqrt(effectiveAngle / angularAcceleration)
            : effectiveAngle / maxTurnRate + maxTurnRate / angularAcceleration;

        // Small margin for control settling and discrete physics substeps.
        return recoveryTime + maneuverTime + 0.14f;
    }

    public static float StoppingSpeed(
        float distance,
        float acceleration,
        float maxSpeed,
        float responseTime = 0f,
        float arrivalSpeed = 0f,
        float safetyFactor = 0.86f)
    {
        var clampedArrival = MathF.Min(maxSpeed, MathF.Max(arrivalSpeed, 0f));
        if (distance <= 0f)
            return clampedArrival;

        // Solve distance = speed * responseTime + (speed² - arrivalSpeed²) / (2 * braking).
        // Near distance = 0 this smoothly approaches distance / responseTime, which is critically
        // damped when responseTime ≈ 4 * velocityTimeConstant.
        var braking = MathF.Max(acceleration, 0f) * safetyFactor;
        if (braking <= 0f)
            return clampedArrival;

        var response = braking * responseTime;
        var speed = MathF.Sqrt(response * response + clampedArrival * clampedArrival + 2f * braking * distance) - response;
        return MathF.Min(maxSpeed, MathF.Max(clampedArrival, speed));
    }

    public static float GetAngularCommand(
        float angleError,
        float relativeRate,
        float acceleration,
        float maxRate,
        float velocityResponseTime,
        float stoppingResponseTime = 0.24f)
    {
        var speed = StoppingSpeed(MathF.Abs(angleError), acceleration, maxRate, stoppingResponseTime, safetyFactor: 0.88f);
        return (MathF.CopySign(speed, angleError) - relativeRate) / velocityResponseTime;
    }

    public static float GetMaxCornerRadius(Box2 bounds, Vector2 localCenter)
    {
        var dx = MathF.Max(MathF.Abs(bounds.Left - localCenter.X), MathF.Abs(bounds.Right - localCenter.X));
        var dy = MathF.Max(MathF.Abs(bounds.Bottom - localCenter.Y), MathF.Abs(bounds.Top - localCenter.Y));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float GetFraction(float requested, float available) =>
        available > 0f ? Math.Clamp(requested / available, -1f, 1f) : 0f;
}
