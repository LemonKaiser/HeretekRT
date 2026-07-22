using System.Numerics;
using System.Linq;
using Content.Server._Mono.Planets;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.LandingPads;
using Content.Shared.Maps;
using Content.Shared._NF.Shipyard.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Resolves authored landing-pad entities into runtime pads. Resolution is performed only when a
/// console state or planetary transfer needs it, avoiding a per-frame scan of every placed tile.
/// </summary>
public sealed class KoronusLandingPadSystem : EntitySystem
{
    private static readonly Vector2i[] CardinalDirections =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private IGameTiming _timing = default!;

    private TimeSpan _nextUiUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusLandingPadConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<KoronusLandingPadConsoleComponent, KoronusLandingPadConfigureMessage>(OnConfigure);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUiUpdate)
            return;

        _nextUiUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<KoronusLandingPadConsoleComponent>();
        while (query.MoveNext(out var uid, out var component))
            UpdateUi((uid, component));
    }

    public List<KoronusLandingPadRuntime> GetPads(EntityUid terrainGrid)
    {
        if (!TryComp<MapGridComponent>(terrainGrid, out var grid) ||
            !TryComp<TransformComponent>(terrainGrid, out var terrainTransform))
            return new List<KoronusLandingPadRuntime>();

        // PlanetSystem loads an authored Grid map over its biome grid. The pad markers in that
        // authored map therefore belong to a separate, stationary grid even though their world
        // coordinates describe the biome terrain. Include those grids only when resolving a real
        // planet map; ordinary grids retain strict per-grid grouping.
        var includePlanetMapGrids = HasComp<PlanetMapComponent>(terrainGrid);
        var tiles = new Dictionary<Vector2i, EntityUid>();
        // Preloaded planet surfaces are paused until somebody lands. Landing targets must still be
        // discoverable while the shuttle is in orbit, so these scans deliberately include paused entities.
        var tileQuery = EntityManager.AllEntityQueryEnumerator<KoronusLandingPadComponent, TransformComponent>();
        while (tileQuery.MoveNext(out var uid, out _, out var transform))
        {
            if (!TryGetTileOnTerrain(
                    terrainGrid,
                    grid,
                    terrainTransform,
                    transform,
                    includePlanetMapGrids,
                    out var tile))
                continue;

            tiles[tile] = uid;
        }

        if (tiles.Count == 0)
            return new List<KoronusLandingPadRuntime>();

        var consoles = new List<(EntityUid Uid, Vector2i Tile, KoronusLandingPadConsoleComponent Component)>();
        var consoleQuery = EntityManager.AllEntityQueryEnumerator<KoronusLandingPadConsoleComponent, TransformComponent>();
        while (consoleQuery.MoveNext(out var uid, out var component, out var transform))
        {
            if (!TryGetTileOnTerrain(
                    terrainGrid,
                    grid,
                    terrainTransform,
                    transform,
                    includePlanetMapGrids,
                    out var tile))
                continue;

            consoles.Add((uid, tile, component));
        }

        consoles.Sort((left, right) => left.Uid.Id.CompareTo(right.Uid.Id));
        var unvisited = new HashSet<Vector2i>(tiles.Keys);
        var result = new List<KoronusLandingPadRuntime>();
        while (unvisited.Count > 0)
        {
            var start = unvisited.First();
            var connected = new HashSet<Vector2i>();
            var pending = new Queue<Vector2i>();
            unvisited.Remove(start);
            pending.Enqueue(start);

            while (pending.TryDequeue(out var current))
            {
                connected.Add(current);
                foreach (var direction in CardinalDirections)
                {
                    var neighbour = current + direction;
                    if (unvisited.Remove(neighbour))
                        pending.Enqueue(neighbour);
                }
            }

            var attachedConsoles = consoles
                .Where(candidate => CardinalDirections.Any(direction => connected.Contains(candidate.Tile + direction)))
                .ToList();
            if (attachedConsoles.Count == 0)
                continue;

            var primary = attachedConsoles[0];
            var minX = connected.Min(tile => tile.X);
            var minY = connected.Min(tile => tile.Y);
            var maxX = connected.Max(tile => tile.X) + 1;
            var maxY = connected.Max(tile => tile.Y) + 1;
            var bounds = new Box2(minX, minY, maxX, maxY);
            result.Add(new KoronusLandingPadRuntime(
                primary.Uid,
                string.IsNullOrWhiteSpace(primary.Component.PadId)
                    ? $"pad:{primary.Uid}"
                    : primary.Component.PadId,
                SanitizeName(primary.Component.PadName),
                bounds.Center,
                bounds.Size,
                bounds,
                connected,
                connected.ToDictionary(tile => tile, tile => tiles[tile]),
                primary.Component,
                attachedConsoles.Select(entry => entry.Uid).ToArray()));
        }

        result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return result;
    }

    private bool TryGetTileOnTerrain(
        EntityUid terrainGrid,
        MapGridComponent grid,
        TransformComponent terrainTransform,
        TransformComponent transform,
        bool includePlanetMapGrids,
        out Vector2i tile)
    {
        tile = default;
        if (!transform.Anchored || transform.GridUid is not { } sourceGrid)
            return false;

        EntityCoordinates coordinates;
        if (sourceGrid == terrainGrid)
        {
            coordinates = transform.Coordinates;
        }
        else
        {
            if (!includePlanetMapGrids ||
                transform.MapID != terrainTransform.MapID ||
                HasComp<ShuttleComponent>(sourceGrid))
            {
                return false;
            }

            var worldPosition = _transform.GetWorldPosition(transform);
            var localPosition = Vector2.Transform(worldPosition, _transform.GetInvWorldMatrix(terrainTransform));
            coordinates = new EntityCoordinates(terrainGrid, localPosition);
        }

        tile = _map.TileIndicesFor(terrainGrid, grid, coordinates);
        return true;
    }

    public bool TryGetPad(EntityUid terrainGrid, string id, out KoronusLandingPadRuntime pad)
    {
        foreach (var candidate in GetPads(terrainGrid))
        {
            if (candidate.Id != id)
                continue;

            pad = candidate;
            return true;
        }

        pad = default!;
        return false;
    }

    public bool IsPowered(EntityUid console)
    {
        return !TryComp<ApcPowerReceiverComponent>(console, out var receiver) ||
               !receiver.PowerDisabled && (!receiver.NeedsPower || _power.IsPowered(console, receiver));
    }

    /// <summary>
    /// Hides only pad plating covered by a landed shuttle. The remaining outline stays visible,
    /// while shuttle floor tiles can never be overdrawn by the pad entity sprites.
    /// </summary>
    public void SetCovered(IEnumerable<EntityUid> tiles, bool covered)
    {
        foreach (var tile in tiles)
        {
            if (TerminatingOrDeleted(tile) || !HasComp<AppearanceComponent>(tile))
                continue;

            _appearance.SetData(tile, KoronusLandingPadVisuals.Covered, covered);
        }
    }

    private void OnUiOpened(Entity<KoronusLandingPadConsoleComponent> entity, ref BoundUIOpenedEvent args)
    {
        UpdateUi(entity);
    }

    private void OnConfigure(
        Entity<KoronusLandingPadConsoleComponent> entity,
        ref KoronusLandingPadConfigureMessage args)
    {
        if (entity.Comp.Locked || !IsPowered(entity.Owner) || !TryGetOwnedPad(entity.Owner, out var pad) ||
            pad.Console != entity.Owner || TryGetPadOccupant(pad, out _, out _))
        {
            UpdateUi(entity);
            return;
        }

        if (!TryConfigure(
                entity.Owner,
                args.Name,
                args.ParkingTime,
                args.PublicAccess,
                args.Enabled))
        {
            UpdateUi(entity);
            return;
        }
        UpdateUi(entity);
    }

    public bool TryConfigure(
        EntityUid console,
        string name,
        int parkingTime,
        bool publicAccess,
        bool enabled,
        string? stableId = null)
    {
        if (!TryComp<KoronusLandingPadConsoleComponent>(console, out var component) || component.Locked)
            return false;

        var sanitizedName = SanitizeName(name);
        if (string.IsNullOrWhiteSpace(sanitizedName) ||
            parkingTime < 0 ||
            parkingTime > KoronusLandingPadConsoleComponent.MaxParkingTime)
        {
            return false;
        }

        component.PadName = sanitizedName;
        component.ParkingTime = parkingTime;
        component.PublicAccess = publicAccess;
        component.Enabled = enabled;
        if (!string.IsNullOrWhiteSpace(stableId))
            component.PadId = stableId;

        // The console component is server-only. Its configuration is sent to clients through the
        // bound UI state in UpdateUi, so trying to dirty it would violate the networked-component
        // contract and fail in DebugOpt integration tests.
        return true;
    }

    public void UpdateUi(Entity<KoronusLandingPadConsoleComponent> entity)
    {
        var primary = TryGetOwnedPad(entity.Owner, out var pad) && pad.Console == entity.Owner;
        EntityUid? occupant = null;
        KoronusLandingSession? session = null;
        if (primary)
            TryGetPadOccupant(pad, out occupant, out session);

        string? shuttleName = null;
        string? shuttleOwner = null;
        float? remaining = null;
        if (occupant is { } shuttle && !TerminatingOrDeleted(shuttle))
        {
            shuttleName = MetaData(shuttle).EntityName;
            if (TryComp<ShuttleDeedComponent>(shuttle, out var deed))
            {
                shuttleName = string.IsNullOrWhiteSpace(deed.ShuttleName) ? shuttleName : deed.ShuttleName;
                shuttleOwner = deed.ShuttleOwner;
            }

            if (session?.Deadline is { } deadline)
                remaining = Math.Max(0f, (float) (deadline - _timing.CurTime).TotalSeconds);
        }

        _ui.SetUiState(entity.Owner, KoronusLandingPadUiKey.Key,
            new KoronusLandingPadBoundUserInterfaceState(
                SanitizeName(entity.Comp.PadName),
                entity.Comp.ParkingTime,
                entity.Comp.PublicAccess,
                entity.Comp.Enabled,
                entity.Comp.Locked,
                IsPowered(entity.Owner),
                primary,
                occupant != null,
                shuttleName,
                shuttleOwner,
                remaining));
    }

    private bool TryGetOwnedPad(EntityUid console, out KoronusLandingPadRuntime pad)
    {
        var gridUid = Transform(console).GridUid;
        if (gridUid != null)
        {
            foreach (var candidate in GetPads(gridUid.Value))
            {
                if (!candidate.Consoles.Contains(console))
                    continue;

                pad = candidate;
                return true;
            }
        }

        pad = default!;
        return false;
    }

    private bool TryGetPadOccupant(
        KoronusLandingPadRuntime pad,
        out EntityUid? occupant,
        out KoronusLandingSession? session)
    {
        occupant = null;
        session = null;
        var query = EntityQueryEnumerator<KoronusSectorRuleComponent>();
        if (!query.MoveNext(out _, out var rule))
            return false;

        var suffix = "/" + pad.Id;
        foreach (var (reservationKey, shuttle) in rule.LandingReservations)
        {
            if (!reservationKey.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            occupant = shuttle;
            session = rule.LandingSessions.Values.FirstOrDefault(candidate =>
                candidate.ReservationKey == reservationKey);
            return true;
        }

        return false;
    }

    private static string SanitizeName(string name)
    {
        var filtered = new string(name
            .Where(character => !char.IsControl(character) && character is not '[' and not ']')
            .Take(KoronusLandingPadConsoleComponent.MaxNameLength)
            .ToArray());
        return filtered.Trim();
    }
}

public sealed class KoronusLandingPadRuntime
{
    public EntityUid Console { get; }
    public string Id { get; }
    public string Name { get; }
    public Vector2 Position { get; }
    public Vector2 Size { get; }
    public Box2 Bounds { get; }
    public HashSet<Vector2i> Tiles { get; }
    public IReadOnlyDictionary<Vector2i, EntityUid> TileEntities { get; }
    public KoronusLandingPadConsoleComponent Component { get; }
    public EntityUid[] Consoles { get; }

    public KoronusLandingPadRuntime(
        EntityUid console,
        string id,
        string name,
        Vector2 position,
        Vector2 size,
        Box2 bounds,
        HashSet<Vector2i> tiles,
        IReadOnlyDictionary<Vector2i, EntityUid> tileEntities,
        KoronusLandingPadConsoleComponent component,
        EntityUid[] consoles)
    {
        Console = console;
        Id = id;
        Name = name;
        Position = position;
        Size = size;
        Bounds = bounds;
        Tiles = tiles;
        TileEntities = tileEntities;
        Component = component;
        Consoles = consoles;
    }
}
