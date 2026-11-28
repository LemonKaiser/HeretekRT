using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server.Power.EntitySystems;
using Content.Shared._Mono.FireControl;
using Content.Shared._WH40K.ShipCombat;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ShipCombat;

/// <summary>
/// Rotates fire-control turrets smoothly toward their aimed target at their configured traverse speed.
/// </summary>
public sealed partial class Wh40kTurretTraverseSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private FireControlSystem _fireControl = default!;

    private static readonly TimeSpan AimHold = TimeSpan.FromSeconds(3);
    private static readonly double Deadband = MathHelper.DegreesToRadians(0.5);

    private const float BearingBinDegrees = 3f;
    private const float RangeBinMeters = 4f;
    private static readonly TimeSpan BearingCacheTime = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<(EntityUid, int), BearingResult> _bearingCache = new();
    private EntityQuery<FireControlRotateComponent> _rotateQuery;
    private EntityQuery<FireControllableComponent> _controlQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    private readonly record struct BearingResult(bool Clear, float Range, MapId Map, TimeSpan Expires);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FireControlConsoleComponent, Wh40kTurretAimMessage>(OnAim);

        _rotateQuery = GetEntityQuery<FireControlRotateComponent>();
        _controlQuery = GetEntityQuery<FireControllableComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
    }

    public bool TryLay(EntityUid weapon, EntityCoordinates target, Vector2 direction)
    {
        if (!TryComp<Wh40kTurretTraverseComponent>(weapon, out var traverse))
            return true;

        Follow(traverse, target);
        var offset = Angle.ShortestDistance(_xform.GetWorldRotation(weapon), Angle.FromWorldVec(direction));
        return Math.Abs(offset.Degrees) <= traverse.Tolerance;
    }

    private void Follow(Wh40kTurretTraverseComponent traverse, EntityCoordinates target)
    {
        traverse.AimTarget = target;
        traverse.AimUntil = _timing.CurTime + AimHold;
    }

    private void OnAim(EntityUid uid, FireControlConsoleComponent component, Wh40kTurretAimMessage args)
    {
        if (component.ConnectedServer is not { } serverUid ||
            !TryComp<FireControlServerComponent>(serverUid, out var server) ||
            !server.Consoles.Contains(uid))
            return;

        Aim(server, args.Selected, args.Coordinates);
    }

    public void Aim(FireControlServerComponent server, List<NetEntity> selected, NetCoordinates coordinates)
    {
        if (!float.IsFinite(coordinates.X) || !float.IsFinite(coordinates.Y))
            return;

        var target = GetCoordinates(coordinates);
        foreach (var netWeapon in selected)
        {
            var weapon = GetEntity(netWeapon);
            if (server.Controlled.Contains(weapon) && _rotateQuery.HasComp(weapon) &&
                TryComp<Wh40kTurretTraverseComponent>(weapon, out var traverse))
            {
                Follow(traverse, target);
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<Wh40kTurretTraverseComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var traverse, out var xform))
        {
            if (traverse.AimTarget is not { } target)
                continue;

            if (traverse.AimUntil < now || !Exists(target.EntityId))
            {
                traverse.AimTarget = null;
                continue;
            }

            if (!_power.IsPowered(uid))
                continue;

            var aim = _xform.ToMapCoordinates(target);
            if (aim.MapId != xform.MapID)
                continue;

            var (position, rotation) = _xform.GetWorldPositionRotation(xform);
            var direction = aim.Position - position;
            if (direction.LengthSquared() < 0.01f)
                continue;

            var goal = Angle.FromWorldVec(direction);
            var offset = Angle.ShortestDistance(rotation, goal).Theta;
            if (Math.Abs(offset) < Deadband)
                continue;

            var maxStep = MathHelper.DegreesToRadians(traverse.TurnRate) * frameTime;
            var next = rotation + Math.Clamp(offset, -maxStep, maxStep);
            if (Math.Abs(offset) > MathHelper.DegreesToRadians(traverse.Tolerance) &&
                StopsAtBlockedArc(uid, xform, position, direction.Length(), goal, rotation, next))
                continue;

            _rotate.TryFaceAngle(uid, next, xform);
        }
    }

    private bool StopsAtBlockedArc(
        EntityUid uid,
        TransformComponent xform,
        Vector2 position,
        float distance,
        Angle goal,
        Angle rotation,
        Angle next)
    {
        if (_controlQuery.TryComp(uid, out var control) && control.IgnoreLos)
            return false;

        var range = MathF.Min(distance, GridReach(xform));
        var map = xform.MapID;
        return !ClearBearing(uid, position, goal, range, map) &&
               ClearBearing(uid, position, rotation, range, map) &&
               !ClearBearing(uid, position, next, range, map);
    }

    private float GridReach(TransformComponent xform)
    {
        if (xform.ParentUid != xform.GridUid || !_gridQuery.TryComp(xform.GridUid, out var grid))
            return float.MaxValue;

        var bounds = grid.LocalAABB;
        var local = xform.LocalPosition;
        var x = MathF.Max(MathF.Abs(bounds.Left - local.X), MathF.Abs(bounds.Right - local.X));
        var y = MathF.Max(MathF.Abs(bounds.Bottom - local.Y), MathF.Abs(bounds.Top - local.Y));
        return MathF.Sqrt(x * x + y * y) + 1f;
    }

    private bool ClearBearing(EntityUid uid, Vector2 position, Angle bearing, float range, MapId map)
    {
        var now = _timing.CurTime;
        var key = (uid, (int) MathF.Round((float) bearing.Reduced().Degrees / BearingBinDegrees));
        if (_bearingCache.TryGetValue(key, out var cached) &&
            cached.Expires > now &&
            MathF.Abs(cached.Range - range) <= RangeBinMeters &&
            cached.Map == map)
            return cached.Clear;

        if (_bearingCache.Count > 2048)
            _bearingCache.Clear();

        var clear = _fireControl.Wh40kClearShot(uid, position, position + bearing.ToWorldVec() * range, map);
        _bearingCache[key] = new BearingResult(clear, range, map, now + BearingCacheTime);
        return clear;
    }
}
