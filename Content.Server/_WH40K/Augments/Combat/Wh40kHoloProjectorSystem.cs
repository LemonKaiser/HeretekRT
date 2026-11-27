using System.Linq;
using Content.Server.Atmos.Components;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Augments.Combat;

public sealed class Wh40kHoloProjectorSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    private const CollisionGroup Blocking = CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.HighImpassable;
    private static readonly Vector2i[] Neighbours = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    private EntityQuery<AirtightComponent> _airtightQuery;
    private EntityQuery<DoorComponent> _doorQuery;

    public override void Initialize()
    {
        base.Initialize();

        _airtightQuery = GetEntityQuery<AirtightComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();

        SubscribeLocalEvent<Wh40kHoloProjectorComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kHoloProjectorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kHoloProjectorComponent, Wh40kHoloProjectEvent>(OnProject);
        SubscribeLocalEvent<Wh40kHoloDecoyComponent, DamageChangedEvent>(OnDecoyDamaged);
    }

    private void OnStartup(Entity<Wh40kHoloProjectorComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<Wh40kHoloProjectorComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnProject(Entity<Wh40kHoloProjectorComponent> ent, ref Wh40kHoloProjectEvent args)
    {
        if (args.Handled)
            return;

        if (!TryCast(ent))
        {
            var key = _buckle.IsBuckled(ent) ? "wh40k-holo-projector-buckled" : "wh40k-holo-projector-cramped";
            _popup.PopupEntity(Loc.GetString(key), ent, ent);
            return;
        }

        args.Handled = true;
    }

    public bool TryCast(Entity<Wh40kHoloProjectorComponent> ent)
    {
        var (uid, comp) = ent;
        var xform = Transform(uid);
        if (_container.IsEntityInContainer(uid) || _buckle.IsBuckled(uid) ||
            xform.GridUid is not { } grid ||
            !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            return false;
        }

        var origin = _transform.WithEntityId(xform.Coordinates, grid);
        var start = _map.TileIndicesFor(grid, gridComp, origin);
        if (!Open(grid, gridComp, start))
            return false;

        var room = RoomTiles(grid, gridComp, start, comp.Range);
        var spots = room.Where(t => t.Steps >= 2).Select(t => t.Tile).ToList();
        if (spots.Count == 0)
            spots = room.Where(t => t.Steps >= 1).Select(t => t.Tile).ToList();
        if (spots.Count == 0)
            return false;

        if (TryComp<PullableComponent>(uid, out var pullable) && _pulling.IsPulled(uid, pullable))
            _pulling.TryStopPull(uid, pullable);
        if (TryComp<PullerComponent>(uid, out var puller) &&
            TryComp(puller.Pulling, out PullableComponent? pulled))
        {
            _pulling.TryStopPull(puller.Pulling.Value, pulled);
        }

        var rotation = _transform.GetWorldRotation(uid);
        var landing = _random.PickAndTake(spots);
        _transform.SetCoordinates(uid, _map.GridTileToLocal(grid, gridComp, landing));
        _transform.SetWorldRotation(uid, rotation);
        SpawnAtPosition(comp.Flicker, Transform(uid).Coordinates);
        _audio.PlayPvs(comp.CastSound, origin);

        for (var i = 0; i < comp.Copies; i++)
        {
            var at = i == 0
                ? origin
                : _map.GridTileToLocal(grid, gridComp, spots.Count > 0 ? _random.PickAndTake(spots) : landing);
            SpawnDecoy(ent, at, rotation);
        }

        return true;
    }

    private void SpawnDecoy(Entity<Wh40kHoloProjectorComponent> ent, EntityCoordinates at, Angle rotation)
    {
        var decoy = SpawnAtPosition(ent.Comp.Decoy, at);
        _transform.SetWorldRotation(decoy, rotation);
        SpawnAtPosition(ent.Comp.Flicker, at);

        var comp = EnsureComp<Wh40kHoloDecoyComponent>(decoy);
        comp.Source = ent;
        Dirty(decoy, comp);
        _meta.SetEntityName(decoy, Identity.Name(ent, EntityManager));
        _meta.SetEntityDescription(decoy, Description(ent));

        if (TryComp<MovementSpeedModifierComponent>(ent, out var move))
            _speed.ChangeBaseSpeed(decoy, move.CurrentWalkSpeed, move.CurrentSprintSpeed, move.Acceleration);
    }

    private List<(Vector2i Tile, int Steps)> RoomTiles(EntityUid grid, MapGridComponent gridComp, Vector2i start, int range)
    {
        var found = new List<(Vector2i Tile, int Steps)>();
        var seen = new HashSet<Vector2i> { start };
        var queue = new Queue<(Vector2i Tile, int Steps)>();
        queue.Enqueue((start, 0));

        while (queue.TryDequeue(out var cur))
        {
            found.Add(cur);
            if (cur.Steps >= range)
                continue;

            foreach (var offset in Neighbours)
            {
                var next = cur.Tile + offset;
                if (seen.Add(next) && Open(grid, gridComp, next))
                    queue.Enqueue((next, cur.Steps + 1));
            }
        }

        return found;
    }

    private bool Open(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        if (!_map.TryGetTileRef(grid, gridComp, tile, out var tileRef) || _turf.IsSpace(tileRef))
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (anchored.MoveNext(out var anchoredUid))
        {
            if (_doorQuery.HasComp(anchoredUid) || _airtightQuery.HasComp(anchoredUid))
                return false;
        }

        return !_turf.IsTileBlocked(grid, tile, Blocking, gridComp);
    }

    private void OnDecoyDamaged(Entity<Wh40kHoloDecoyComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || TerminatingOrDeleted(ent))
            return;

        SpawnAtPosition(ent.Comp.Puff, Transform(ent).Coordinates);
        QueueDel(ent);
    }
}
