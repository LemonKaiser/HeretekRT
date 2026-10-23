using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Server.Parallax;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared._WH40K.SectorMap;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>Renews procedural resources while retaining the map and all authored or player infrastructure.</summary>
public sealed partial class KoronusSurfaceRefreshSystem : EntitySystem
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(3);
    private static readonly TimeSpan WarningLead = TimeSpan.FromMinutes(5);
    [Dependency] private BiomeSystem _biomes = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private KoronusQuicksandSystem _quicksand = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private WH40KPlayerCultureManager _culture = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private BiomeSystem.SurfaceRefresh? _refresh;
    private TimeSpan _nextScan;
    private readonly HashSet<EntityUid> _evacuees = new();
    private readonly HashSet<EntityUid> _movedRoots = new();
    private readonly List<EntityCoordinates> _returnTiles = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(BiomeSystem));
        SubscribeLocalEvent<KoronusPlanetSurfaceMapComponent, ComponentStartup>(OnSurfaceStartup);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnSurfaceStartup(Entity<KoronusPlanetSurfaceMapComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.NextTerrainRefresh = _timing.CurTime + RefreshInterval;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        if (_refresh != null)
            _biomes.CancelSurfaceRefresh(_refresh);
        _refresh = null;
        _nextScan = default;
        _evacuees.Clear();
        _movedRoots.Clear();
        _returnTiles.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_refresh is { } refresh)
        {
            if (TerminatingOrDeleted(refresh.Terrain))
                _refresh = null;
            else if (!TryFindReturnTiles(refresh.Surface))
            {
                // A destroyed base must never cause us to clear terrain under stranded players.
                CancelRefresh(refresh);
            }
            else
            {
                if (!Evacuate(refresh))
                {
                    CancelRefresh(refresh);
                    return;
                }
                ProtectInfrastructure(refresh);
                _biomes.ProtectSurfaceOccupants(refresh);
                if (_biomes.ProcessSurfaceRefresh(refresh))
                {
                    refresh.Surface.NextTerrainRefresh = _timing.CurTime + RefreshInterval;
                    refresh.Surface.TerrainRefreshWarningSent = false;
                    Announce(refresh.Terrain, "koronus-surface-refresh-complete");
                    _refresh = null;
                }
            }
        }

        if (_refresh != null || _timing.CurTime < _nextScan)
            return;
        _nextScan = _timing.CurTime + TimeSpan.FromSeconds(1);

        // Empty planets are paused; their three-hour resource clock must keep advancing.
        var query = AllEntityQuery<KoronusPlanetSurfaceMapComponent, BiomeComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var surface, out var biome, out _))
        {
            if (!biome.Enabled)
                continue;
            if (!surface.TerrainRefreshWarningSent && _timing.CurTime >= surface.NextTerrainRefresh - WarningLead)
            {
                Announce(uid, "koronus-surface-refresh-warning");
                surface.TerrainRefreshWarningSent = true;
            }
            if (_timing.CurTime < surface.NextTerrainRefresh)
                continue;
            if (!TryStartRefresh(uid))
            {
                surface.NextTerrainRefresh = _timing.CurTime + TimeSpan.FromMinutes(1);
                continue;
            }
            break;
        }
    }

    public bool IsRefreshing(EntityUid map) => _refresh?.Terrain.Owner == map;

    public bool TryStartRefresh(EntityUid map)
    {
        if (_refresh != null ||
            !TryComp<KoronusPlanetSurfaceMapComponent>(map, out var surface) ||
            surface.TerrainGrid != map ||
            !TryComp<BiomeComponent>(map, out var biome) || !biome.Enabled ||
            !TryComp<MapGridComponent>(map, out var grid) || !TryFindReturnTiles(surface))
            return false;

        _refresh = _biomes.BeginSurfaceRefresh((map, biome, grid), surface);
        if (!Evacuate(_refresh))
        {
            CancelRefresh(_refresh);
            return false;
        }
        Announce(map, "koronus-surface-refresh-start");
        ProtectInfrastructure(_refresh);
        _biomes.ProtectSurfaceOccupants(_refresh);
        return true;
    }

    private bool TryFindReturnTiles(KoronusPlanetSurfaceMapComponent surface)
    {
        _returnTiles.Clear();
        foreach (var uid in surface.AuthoredBaseGrids)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<MapGridComponent>(uid, out var grid))
                continue;
            var tiles = _maps.GetAllTilesEnumerator(uid, grid);
            while (tiles.MoveNext(out var tile))
            {
                if (tile is not { } candidate || candidate.Tile.IsEmpty || _turf.IsSpace(candidate) ||
                    _turf.IsTileBlocked(candidate, CollisionGroup.MobMask) ||
                    _quicksand.IsQuicksandTile((uid, grid), candidate.GridIndices))
                    continue;
                _returnTiles.Add(_maps.ToCenterCoordinates(candidate, grid));
                if (_returnTiles.Count >= 64)
                    return true;
            }
        }
        return _returnTiles.Count > 0;
    }

    private void CancelRefresh(BiomeSystem.SurfaceRefresh refresh)
    {
        _biomes.CancelSurfaceRefresh(refresh);
        refresh.Surface.NextTerrainRefresh = _timing.CurTime + TimeSpan.FromMinutes(1);
        _refresh = null;
    }

    private bool Evacuate(BiomeSystem.SurfaceRefresh refresh)
    {
        _evacuees.Clear();
        _movedRoots.Clear();
        var minds = AllEntityQuery<MindComponent>();
        while (minds.MoveNext(out var mind))
        {
            if (mind.OwnedEntity is { } owned)
                _evacuees.Add(owned);
            if (mind.VisitingEntity is { } visiting)
                _evacuees.Add(visiting);
            if (mind.OriginalOwnedEntity is { } original && TryGetEntity(original, out var body) && body is { } originalBody)
                _evacuees.Add(originalBody);
        }
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } player)
                _evacuees.Add(player);
        }

        foreach (var uid in _evacuees)
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out TransformComponent? transform) ||
                transform.MapUid != refresh.Terrain.Owner ||
                transform.GridUid is { } grid && grid != refresh.Terrain.Owner)
                continue;
            if (TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled)
                _buckle.Unbuckle((uid, buckle), null);

            // Move an enclosing locker or carrier as a whole, retaining its contents and the player's inventory.
            var root = uid;
            var rootTransform = Transform(root);
            while (rootTransform.ParentUid != EntityUid.Invalid &&
                   !HasComp<MapGridComponent>(rootTransform.ParentUid) && !HasComp<MapComponent>(rootTransform.ParentUid))
            {
                root = rootTransform.ParentUid;
                rootTransform = Transform(root);
            }
            if (!_movedRoots.Add(root))
                continue;
            if (!StopPulling(uid) || !StopPulling(root))
                return false;
            if (rootTransform.Anchored)
                _transform.Unanchor(root, rootTransform);
            _transform.SetCoordinates(root, rootTransform, _returnTiles[(_movedRoots.Count - 1) % _returnTiles.Count]);
            if (TryComp<PhysicsComponent>(root, out var physics))
            {
                _physics.SetLinearVelocity(root, Vector2.Zero, body: physics);
                _physics.SetAngularVelocity(root, 0f, body: physics);
            }
        }
        return true;
    }

    private bool StopPulling(EntityUid uid)
    {
        if (TryComp<PullableComponent>(uid, out var pullable) && !_pulling.TryStopPull(uid, pullable))
            return false;
        if (TryComp<PullerComponent>(uid, out var puller) && puller.Pulling is { } pulled &&
            TryComp<PullableComponent>(pulled, out var target))
            return _pulling.TryStopPull(pulled, target);
        return true;
    }

    private void ProtectInfrastructure(BiomeSystem.SurfaceRefresh refresh)
    {
        var terrain = refresh.Terrain.Owner;
        var mapId = Transform(terrain).MapID;
        foreach (var grid in _maps.GetAllGrids(mapId))
        {
            if (grid.Owner != terrain)
            {
                var (position, rotation) = _transform.GetWorldPositionRotation(Transform(grid.Owner));
                var bounds = new Box2Rotated(grid.Comp.LocalAABB.Translated(position), rotation, position).CalcBoundingBox();
                _biomes.ProtectSurfaceArea(refresh, bounds.Enlarged(1f));
            }
        }
        // Preserve the authored, flora-free landing clearing, including its empty sections.
        if (_prototypes.TryIndex<KoronusPlanetSurfacePrototype>(refresh.Surface.SurfaceId, out var surface) &&
            surface.LandingClearanceSize.X > 0f && surface.LandingClearanceSize.Y > 0f)
        {
            var halfSize = surface.LandingClearanceSize / 2f;
            _biomes.ProtectSurfaceArea(refresh, new Box2(-halfSize, halfSize));
        }
    }

    private void Announce(EntityUid map, string key)
    {
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } player && TryComp(player, out TransformComponent? transform) &&
                transform.MapUid == map)
                _chat.DispatchServerMessage(session, _culture.GetPlayerString(player, key));
        }
    }
}
