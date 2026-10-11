using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Standing;
using Content.Shared._WH40K.Npcs;
using Content.Server.Humanoid.Systems;
using Content.Shared.Humanoid;
using System.Linq;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Content.Shared.Rotation;
using Content.Shared.Clothing;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Nutrition.Components;
using Content.Shared.Smoking;
using Content.Server.Nutrition.EntitySystems;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Npcs;

/// <summary>Uses native lying visuals and buckling; never creates or moves furniture.</summary>
public sealed partial class Wh40kNpcSceneSystem : EntitySystem
{
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedRotationVisualsSystem _rotationVisuals = default!;
    [Dependency] private SharedCuffableSystem _cuffs = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SmokingSystem _smoking = default!;
    private const float SeatSearchRange = 1.5f;
    private const float NeighbourSeatRange = 1.25f;
    private float _timer;

    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kNpcSceneComponent, MapInitEvent>(OnInit,
            after: [typeof(RandomHumanoidAppearanceSystem), typeof(LoadoutSystem)]);
        SubscribeLocalEvent<Wh40kNpcSceneComponent, StandAttemptEvent>(OnStand);
    }

    private void OnInit(EntityUid uid, Wh40kNpcSceneComponent comp, MapInitEvent args)
    {
        var xform = Transform(uid);
        comp.Home = xform.Coordinates;
        comp.Facing = xform.LocalRotation;
        // MapInit subscriber order must not let buckling capture a chair-relative protection home.
        if (TryComp<Wh40kNpcProtectionComponent>(uid, out var protection)) protection.Home ??= comp.Home;
        foreach (var marking in comp.Markings) _humanoid.AddMarking(uid, marking.Id, Color.White, forced: true);
        if (comp.Smoking && !HasComp<ActorComponent>(uid) &&
            _inventory.TryGetSlotEntity(uid, "mask", out var cigarette) && HasComp<SmokableComponent>(cigarette))
            _smoking.SetSmokableState(cigarette.Value, SmokableState.Lit);
        if (comp.Cuffs is { } cuffsProto && !HasComp<ActorComponent>(uid) &&
            TryComp<CuffableComponent>(uid, out var cuffable) && cuffable.CuffedHandCount == 0)
        {
            var cuffs = Spawn(cuffsProto, xform.Coordinates);
            if (!_cuffs.TryAddNewCuffs(uid, uid, cuffs, cuffable))
                QueueDel(cuffs);
        }
        Stage(uid, comp);
    }

    private void OnStand(EntityUid uid, Wh40kNpcSceneComponent comp, StandAttemptEvent args)
    {
        if (comp.Pose == Wh40kNpcScenePose.Lying) args.Cancel();
    }

    public override void Update(float frameTime)
    {
        _timer += frameTime;
        if (_timer < 1f) return;
        _timer = 0;
        var query = EntityQueryEnumerator<Wh40kNpcSceneComponent>();
        while (query.MoveNext(out var uid, out var comp))
            if (!Paused(uid)) Stage(uid, comp);
    }

    private void Stage(EntityUid uid, Wh40kNpcSceneComponent comp)
    {
        if (comp.Pose == Wh40kNpcScenePose.Lying)
        {
            if (TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled)
                return;

            if (comp.Buckle && buckle != null && TryBuckleToStrap(uid, comp, StrapPosition.Down, buckle))
                return;

            if (Transform(uid).LocalRotation != comp.Facing)
                _transform.SetLocalRotation(uid, comp.Facing);

            var worldFacing = comp.Home is { } homeCoords && homeCoords.IsValid(EntityManager)
                ? _transform.GetWorldRotation(homeCoords.EntityId) + comp.Facing
                : _transform.GetWorldRotation(uid);
            var lieAngle = worldFacing - MathHelper.Pi;
            _rotationVisuals.SetHorizontalAngle(uid, lieAngle);
            if (!_standing.IsDown(uid))
                _standing.Down(uid, playSound: false, dropHeldItems: false);
            return;
        }

        if (comp.Pose != Wh40kNpcScenePose.Seated || comp.Home is not { } home ||
            !home.IsValid(EntityManager) || !TryComp<BuckleComponent>(uid, out var seatedBuckle) || seatedBuckle.Buckled)
            return;

        TryBuckleToStrap(uid, comp, StrapPosition.Stand, seatedBuckle);
    }

    private bool TryBuckleToStrap(EntityUid uid, Wh40kNpcSceneComponent comp, StrapPosition targetPosition, BuckleComponent buckle)
    {
        if (comp.Home is not { } home || !home.IsValid(EntityManager))
            return false;

        if (!TryGetTile(home, out var homeTile))
            return false;

        var ownSeats = new List<(EntityUid Seat, float Distance)>();
        var neighbours = new List<(EntityUid Seat, float Distance)>();
        var hasOwnSeat = false;
        var point = _transform.ToMapCoordinates(home);

        foreach (var (candidate, strap) in _lookup.GetEntitiesInRange<StrapComponent>(point, SeatSearchRange))
        {
            if (strap.Position != targetPosition ||
                !TryGetTile(Transform(candidate).Coordinates, out var seatTile) || seatTile.Frame != homeTile.Frame)
                continue;

            var position = _transform.GetMapCoordinates(candidate);
            var distance = (position.Position - point.Position).LengthSquared();
            if (position.MapId != point.MapId || distance > SeatSearchRange * SeatSearchRange)
                continue;

            var ownTile = seatTile == homeTile;
            hasOwnSeat |= ownTile;
            if (!strap.Enabled || strap.BuckledEntities.Count > 0)
                continue;

            if (ownTile)
                ownSeats.Add((candidate, distance));
            else if (distance <= NeighbourSeatRange * NeighbourSeatRange &&
                     Math.Abs(seatTile.Tile.X - homeTile.Tile.X) + Math.Abs(seatTile.Tile.Y - homeTile.Tile.Y) == 1)
                neighbours.Add((candidate, distance));
        }

        // An occupied/disabled home-tile seat means wait, rather than taking the next NPC's chair/bed.
        var candidates = hasOwnSeat ? ownSeats : neighbours;
        foreach (var (seat, _) in candidates.OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Seat.Id))
        {
            if (!hasOwnSeat && IsReservedNeighbour(uid, seat, targetPosition))
                continue;

            // Native buckling still owns range, obstruction, capacity and container/whitelist checks.
            if (!_buckle.TryBuckle(uid, null, seat, buckle, popup: false))
                continue;

            if (targetPosition == StrapPosition.Stand)
            {
                var facing = _transform.GetWorldRotation(home.EntityId) + comp.Facing;
                _rotate.TryFaceAngle(uid, facing);
            }

            return true;
        }

        return false;
    }

    private bool TryGetTile(EntityCoordinates coordinates, out (EntityUid Frame, Vector2i Tile) tile)
    {
        tile = default;
        if (!coordinates.IsValid(EntityManager)) return false;
        if (_transform.GetGrid(coordinates) is {} grid && TryComp<MapGridComponent>(grid, out var gridComp))
        {
            tile = (grid, _map.CoordinatesToTile(grid, gridComp, coordinates));
            return true;
        }
        if (_transform.GetMap(coordinates) is not {} map) return false;
        tile = (map, _transform.ToMapCoordinates(coordinates).Position.Floored());
        return true;
    }

    private bool IsReservedNeighbour(EntityUid uid, EntityUid seat, StrapPosition position)
    {
        if (!TryGetTile(Transform(seat).Coordinates, out var seatTile)) return true;
        var point = _transform.GetMapCoordinates(seat);
        foreach (var (other, scene) in _lookup.GetEntitiesInRange<Wh40kNpcSceneComponent>(point, SeatSearchRange))
            if (other != uid && ShouldBuckleTo(scene, position) && scene.Home is {} home &&
                TryGetTile(home, out var tile) && tile == seatTile) return true;

        // Saved sources reserve their own tile even before their body/furniture finishes map initialization.
        foreach (var (source, spawn) in _lookup.GetEntitiesInRange<Wh40kAmbientNpcSpawnComponent>(point, SeatSearchRange))
            if (IsReservedSource(source, spawn.Prototype.Id, seatTile, position)) return true;
        foreach (var (source, spawn) in _lookup.GetEntitiesInRange<Wh40kNpcSpawnComponent>(point, SeatSearchRange))
            if (IsReservedSource(source, spawn.Prototype, seatTile, position)) return true;
        return false;
    }

    private bool ShouldBuckleTo(Wh40kNpcSceneComponent scene, StrapPosition position)
    {
        return position switch
        {
            StrapPosition.Stand => scene.Pose == Wh40kNpcScenePose.Seated,
            StrapPosition.Down => scene.Pose == Wh40kNpcScenePose.Lying && scene.Buckle,
            _ => false,
        };
    }

    private bool IsReservedSource(EntityUid source, string prototype, (EntityUid Frame, Vector2i Tile) seatTile, StrapPosition position)
    {
        return TryGetTile(Transform(source).Coordinates, out var tile) && tile == seatTile &&
               _prototypes.TryIndex<EntityPrototype>(prototype, out var proto) &&
               proto.TryGetComponent<Wh40kNpcSceneComponent>(out var scene, EntityManager.ComponentFactory) &&
               ShouldBuckleTo(scene, position);
    }
}
