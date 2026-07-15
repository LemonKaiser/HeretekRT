using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.Physics.Controllers;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Construction.Components;
using Robust.Shared.Map;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._Mono.NPC.HTN.Operators;

/// <summary>
/// Moves parent shuttle to specified target key. Hands the actual steering off to ShipSteeringSystem.
/// </summary>
public sealed partial class ShipMoveToOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private IEntityManager _entManager = default!;
    private PowerReceiverSystem _power = default!;
    private ShipSteeringSystem _steering = default!;

    /// <summary>
    /// When to shut the task down.
    /// </summary>
    [DataField]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    /// <summary>
    /// When we're finished moving to the target should we remove its key?
    /// </summary>
    [DataField]
    public bool RemoveKeyOnFinish = true;

    /// <summary>
    /// Target Coordinates to move to. This gets removed after execution.
    /// </summary>
    [DataField]
    public string TargetKey = "ShipTargetCoordinates";

    /// <summary>
    /// World angle to try to be at after arrival. This gets removed after execution.
    /// </summary>
    [DataField]
    public string AngleKey = "ShipTargetAngle";

    /// <summary>
    /// Whether to keep facing target if backing off due to RangeTolerance.
    /// </summary>
    [DataField]
    public bool AlwaysFaceTarget = false;

    /// <summary>
    /// Whether to turn to the current travel course before applying normal travel thrust.
    /// Emergency collision evasion may still strafe to avoid an imminent collision.
    /// </summary>
    [DataField]
    public bool ForwardFlight = false;

    /// <summary>
    /// Multiplier for the normal shuttle thrust while this movement order is active.
    /// </summary>
    [DataField]
    public float AutopilotAccelerationMultiplier = 1f;

    /// <summary>
    /// Heading error in radians below which forward flight begins after a turn.
    /// </summary>
    [DataField]
    public float ForwardFlightEnterAngle = 0.2f;

    /// <summary>
    /// Heading error in radians above which forward flight returns to turn-and-brake mode.
    /// </summary>
    [DataField]
    public float ForwardFlightExitAngle = 0.35f;

    /// <summary>
    /// Collision horizon in seconds below which lateral emergency evasion is permitted.
    /// </summary>
    [DataField]
    public float ForwardFlightEmergencyTime = 1.5f;

    /// <summary>
    /// Whether to avoid obstacles.
    /// </summary>
    [DataField]
    public bool AvoidCollisions = true;

    /// <summary>
    /// Whether to avoid shipgun projectiles.
    /// </summary>
    [DataField]
    public bool AvoidProjectiles = false;

    /// <summary>
    /// How unwilling we are to use brake to adjust our velocity. Higher means less willing.
    /// </summary>
    [DataField]
    public float BrakeThreshold = 0.3f;

    /// <summary>
    /// How many evasion sectors to init on the outer ring.
    /// </summary>
    [DataField]
    public int EvasionSectorCount = 24;

    /// <summary>
    /// How many layers of evasion sectors to have.
    /// </summary>
    [DataField]
    public int EvasionSectorDepth = 2;

    /// <summary>
    /// Collision horizon in seconds used even while the shuttle is stationary.
    /// </summary>
    [DataField]
    public float BaseEvasionTime = 4f;

    /// <summary>
    /// Additional collision radius used when choosing an evasion corridor.
    /// </summary>
    [DataField]
    public float EvasionBuffer = 3f;

    /// <summary>
    /// Maximum speed while following a collision-avoidance vector.
    /// </summary>
    [DataField]
    public float AvoidanceMaxSpeed = float.PositiveInfinity;

    /// <summary>
    /// Minimum distance to scan in the current and planned travel directions for collision evasion.
    /// </summary>
    [DataField]
    public float MinimumObstacleScanDistance = 0f;

    /// <summary>
    /// Whether to consider the movement finished if we collide with target.
    /// </summary>
    [DataField]
    public bool FinishOnCollide = true;

    /// <summary>
    /// Velocity below which we count as successfully braked.
    /// Don't care about velocity if null.
    /// </summary>
    [DataField]
    public float? InRangeMaxSpeed = 0.1f;

    /// <summary>
    /// Maximum linear speed requested from the shuttle mover while this task is active.
    /// Null leaves the shuttle's normal speed unrestricted.
    /// </summary>
    [DataField]
    public float? MaxLinearVelocity = null;

    /// <summary>
    /// Requested speed at the steering target when a slowdown distance is configured.
    /// </summary>
    [DataField]
    public float? MinLinearVelocity = null;

    /// <summary>
    /// Distance from the target over which the requested maximum speed is reduced.
    /// </summary>
    [DataField]
    public float SlowdownDistance = 0f;

    /// <summary>
    /// Whether to try to match velocity with target.
    /// </summary>
    [DataField]
    public bool LeadingEnabled = true;

    /// <summary>
    /// Max rotation rate to be considered stationary, if not null.
    /// </summary>
    [DataField]
    public float? MaxRotateRate = null;

    /// <summary>
    /// Whether to begin aligning to the requested angle before reaching the destination.
    /// </summary>
    [DataField]
    public bool RotateBeforeArrival = false;

    /// <summary>
    /// Distance from the target at which early arrival-angle alignment may begin.
    /// A non-positive value keeps the original behaviour of aligning throughout the movement.
    /// </summary>
    [DataField]
    public float RotationAlignmentDistance = 0f;

    /// <summary>
    /// What movement behavior to use.
    /// </summary>
    [DataField]
    public ShipSteeringMode Mode = ShipSteeringMode.GoToRange;

    /// <summary>
    /// In Orbit mode, how much to angularly offset our destination.
    /// </summary>
    [DataField]
    public float OrbitOffset = 30f;

    /// <summary>
    /// How close we need to get before considering movement finished.
    /// </summary>
    [DataField]
    public float Range = 5f;

    /// <summary>
    /// At most how far inside to have to stay into the desired range. If null, will consider the movement finished while in range.
    /// </summary>
    [DataField]
    public float? RangeTolerance = null;

    /// <summary>
    /// Whether to require us to be anchored.
    /// Here because HTN does not allow us to continuously check a condition by itself.
    /// Ignored if we're not anchorable.
    /// </summary>
    [DataField]
    public bool RequireAnchored = true;

    /// <summary>
    /// Whether to require us to be powered, if we have ApcPowerReceiver.
    /// </summary>
    [DataField]
    public bool RequirePowered = true;

    /// <summary>
    /// Whether to finish if there's another active pilot on the grid.
    /// </summary>
    [DataField]
    public bool RequireSolo = false;

    /// <summary>
    /// Rotation to move at relative to direction to target.
    /// </summary>
    [DataField]
    public float TargetRotation = 0f;

    private const string MovementCancelToken = "ShipMovementCancelToken";
    private const string SteeringDoneKeyPrefix = "ShipSteeringDone:";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _power = sysManager.GetEntitySystem<PowerReceiverSystem>();
        _steering = sysManager.GetEntitySystem<ShipSteeringSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var targetCoordinates, _entManager))
        {
            return (false, null);
        }

        return (true, new Dictionary<string, object>()
        {
            {NPCBlackboard.OwnerCoordinates, targetCoordinates}
        });
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        // HTN operators are prototype singletons, so per-order runtime state must live in the
        // owning NPC blackboard rather than on this operator instance.
        blackboard.Remove<bool>(SteeringDoneKeyPrefix + TargetKey);

        // Need to remove the planning value for execution.
        blackboard.Remove<EntityCoordinates>(NPCBlackboard.OwnerCoordinates);
        if (!blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var targetCoordinates, _entManager))
            return;
        Angle? targetAngle = blackboard.TryGetValue<Angle>(AngleKey, out var keyAngle, _entManager) ? keyAngle : null;

        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var comp = _steering.Steer(uid, targetCoordinates);

        if (comp == null)
            return;

        comp.AlwaysFaceTarget = AlwaysFaceTarget;
        comp.ForwardFlight = ForwardFlight;
        comp.AutopilotAccelerationMultiplier = MathF.Max(AutopilotAccelerationMultiplier, 0f);
        comp.ForwardFlightEnterAngle = ForwardFlightEnterAngle;
        comp.ForwardFlightExitAngle = MathF.Max(ForwardFlightExitAngle, ForwardFlightEnterAngle);
        comp.ForwardFlightEmergencyTime = ForwardFlightEmergencyTime;
        comp.ForwardFlightAligned = false;
        comp.AvoidanceWaypoint = null;
        comp.AvoidCollisions = AvoidCollisions;
        comp.AvoidProjectiles = AvoidProjectiles;
        comp.BrakeThreshold = BrakeThreshold;
        comp.BaseEvasionTime = BaseEvasionTime;
        comp.EvasionBuffer = EvasionBuffer;
        comp.AvoidanceMaxSpeed = AvoidanceMaxSpeed;
        comp.EvasionSectorCount = EvasionSectorCount;
        comp.EvasionSectorDepth = EvasionSectorDepth;
        comp.MinimumObstacleScanDistance = MinimumObstacleScanDistance;
        comp.FinishOnCollide = FinishOnCollide;
        comp.InRangeMaxSpeed = InRangeMaxSpeed;
        comp.SetMaxVelocity = MaxLinearVelocity;
        comp.MinLinearVelocity = MinLinearVelocity;
        comp.SlowdownDistance = SlowdownDistance;
        comp.InRangeRotation = targetAngle;
        comp.LeadingEnabled = LeadingEnabled;
        comp.MaxRotateRate = MaxRotateRate;
        comp.Mode = Mode;
        comp.NoFinish = ShutdownState == HTNPlanState.PlanFinished;
        comp.OrbitOffset = Angle.FromDegrees(OrbitOffset);
        comp.Range = Range;
        comp.RangeTolerance = RangeTolerance;
        comp.RotateBeforeArrival = RotateBeforeArrival;
        comp.RotationAlignmentDistance = RotationAlignmentDistance;
        comp.TargetRotation = TargetRotation;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<ShipSteererComponent>(owner, out var steerer)
            || !blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var target, _entManager)
            || !_entManager.TryGetComponent<TransformComponent>(owner, out var xform)
            // also fail if we're anchorable but are unanchored and require to be anchored
            || RequireAnchored
                && _entManager.TryGetComponent<AnchorableComponent>(owner, out var anchorable) && !xform.Anchored
            || RequirePowered
                && _entManager.TryGetComponent<ApcPowerReceiverComponent>(owner, out var receiver) && !_power.IsPowered(owner, receiver)
        )
            return HTNOperatorStatus.Failed;

        // ensure we're still steering if we e.g. move grids
        var comp = _steering.Steer(owner, target);
        if (comp == null)
            return HTNOperatorStatus.Failed;

        Angle? targetAngle = blackboard.TryGetValue<Angle>(AngleKey, out var keyAngle, _entManager) ? keyAngle : null;
        comp.InRangeRotation = targetAngle;

        // Just keep moving in the background and let the other tasks handle it.
        if (ShutdownState == HTNPlanState.PlanFinished && steerer.Status == ShipSteeringStatus.Moving)
        {
            return HTNOperatorStatus.Finished;
        }

        if (RequireSolo && _entManager.TryGetComponent<PilotedShuttleComponent>(xform.GridUid, out var piloted) && piloted.ActiveSources > 1)
            return HTNOperatorStatus.Finished;

        return steerer.Status switch
        {
            ShipSteeringStatus.InRange => HTNOperatorStatus.Finished,
            ShipSteeringStatus.Moving => HTNOperatorStatus.Continuing,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        // Cleanup the blackboard and remove steering.
        if (blackboard.TryGetValue<CancellationTokenSource>(MovementCancelToken, out var cancelToken, _entManager))
        {
            cancelToken.Cancel();
            blackboard.Remove<CancellationTokenSource>(MovementCancelToken);
        }

        if (RemoveKeyOnFinish)
        {
            blackboard.Remove<EntityCoordinates>(TargetKey);
            blackboard.Remove<Angle>(AngleKey);
        }

        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var success = _entManager.TryGetComponent<ShipSteererComponent>(uid, out var steerer) &&
                      steerer.Status == ShipSteeringStatus.InRange;
        _steering.Stop(uid);
        var steeringDoneKey = SteeringDoneKeyPrefix + TargetKey;
        if (!blackboard.TryGetValue<bool>(steeringDoneKey, out var raisedEvent, _entManager) || !raisedEvent)
        {
            blackboard.SetValue(steeringDoneKey, true);
            _entManager.EventBus.RaiseLocalEvent(uid, new SteeringDoneEvent(success, TargetKey), false);
        }
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);

        ConditionalShutdown(blackboard);
    }
}

/// <summary>
/// Reports which blackboard movement order ended. A console can replace a normal autopilot order
/// with an auto-docking order before the old HTN plan has finished shutting down, so success alone
/// is not enough to associate this event with the active order.
/// </summary>
public record struct SteeringDoneEvent(bool Success, string TargetKey);
