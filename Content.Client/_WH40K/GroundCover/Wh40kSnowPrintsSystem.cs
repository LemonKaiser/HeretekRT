using System.Numerics;
using Content.Shared._WH40K.GroundCover;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.GroundCover;

/// <summary>
/// Footprints in snow cover, laid on this client from the mobs it sees walk, so nothing is networked. They fade with
/// time, with the snow under them as it thins, fill in as fresh snow lands on them, and go when their tile is shoveled
/// or melts bare.
/// </summary>
public sealed partial class Wh40kSnowPrintsSystem : EntitySystem
{
    [Dependency] private Wh40kGroundCoverSystem _cover = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Prints kept at most; the oldest go first.</summary>
    public const int MaxPrints = 1500;

    /// <summary>Prints one tile keeps; a new one replaces its oldest.</summary>
    public const int MaxPerTile = 6;

    /// <summary>Distance walked between prints.</summary>
    public const float StepSize = 0.7f;

    /// <summary>Depth units of fresh snow that fill a print in.</summary>
    public const int FillUnits = Wh40kSnowCover.UnitsPerLevel / 3;

    /// <summary>Depth a print needs to show in full: under it, a print fades as the snow thins, to nothing when bare.</summary>
    public const int FullDepth = Wh40kSnowCover.UnitsPerLevel;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(4);

    private static readonly Vector2 SideOffset = new(0.1f, 0f);
    private static readonly Angle PrintRotation = Angle.FromDegrees(180);
    private const string ShoesSlot = "shoes";

    private readonly Dictionary<EntityUid, Dictionary<Vector2i, List<Wh40kSnowPrint>>> _prints = new();
    private readonly LinkedList<Wh40kSnowPrint> _order = new();
    private readonly Dictionary<EntityUid, (EntityUid Grid, Vector2 Position, bool Right)> _walkers = new();
    private readonly List<EntityUid> _stale = new();
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _changedChunks = new();
    private readonly HashSet<EntityUid> _forgottenGrids = new();
    private readonly List<Vector2i> _recheckTiles = new();

    /// <summary>Prints laid and not yet gone.</summary>
    public int PrintCount { get; private set; }

    /// <summary>Prints by grid and tile.</summary>
    public IReadOnlyDictionary<EntityUid, Dictionary<Vector2i, List<Wh40kSnowPrint>>> Prints => _prints;

    public override void Initialize()
    {
        base.Initialize();
        _cover.SnowChunkChanged += OnSnowChunkChanged;
        _cover.SnowGridForgotten += OnSnowGridForgotten;
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        _overlay.AddOverlay(new Wh40kSnowPrintsOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cover.SnowChunkChanged -= OnSnowChunkChanged;
        _cover.SnowGridForgotten -= OnSnowGridForgotten;
        _overlay.RemoveOverlay<Wh40kSnowPrintsOverlay>();
        Clear();
    }

    /// <summary>Forgets every print and walker.</summary>
    public void Clear()
    {
        _prints.Clear();
        _order.Clear();
        _walkers.Clear();
        _changedChunks.Clear();
        _forgottenGrids.Clear();
        PrintCount = 0;
    }

    /// <summary>How visible a print is now, from 1 fresh to 0 gone.</summary>
    public float Visibility(Wh40kSnowPrint print)
    {
        var age = (float) ((_timing.CurTime - print.Laid) / Lifetime);
        return Math.Clamp(1f - age, 0f, 1f) * (1f - print.Filled) * print.Thickness;
    }

    /// <summary>How much of a print's snow is left, 1 while the snow is as deep as it was or past <see cref="FullDepth"/>, 0 when bare.</summary>
    public static float Thickness(int laid, int depth)
    {
        return Math.Clamp(depth / (float) Math.Min(laid, FullDepth), 0f, 1f);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        RecheckChangedSnow();

        var now = _timing.CurTime;
        while (_order.First is { } oldest && now - oldest.Value.Laid > Lifetime)
            Remove(oldest.Value);

        if (_cover.HasKnownSnow)
            Walk();
        else
            _walkers.Clear();
    }

    /// <summary>Lays a print for every mob that walked a step on snow cover since last frame.</summary>
    private void Walk()
    {
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || xform.ParentUid != grid || !_cover.HasSnow(grid))
            {
                _walkers.Remove(uid);
                continue;
            }

            var position = xform.LocalPosition;
            if (!_walkers.TryGetValue(uid, out var last) || last.Grid != grid)
            {
                _walkers[uid] = (grid, position, false);
                continue;
            }

            var delta = position - last.Position;
            if (delta.LengthSquared() < StepSize * StepSize)
                continue;

            var right = !last.Right;
            _walkers[uid] = (grid, position, right);
            if (_gravity.IsWeightless(uid) || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            var tile = _map.LocalToTile(grid, gridComp, xform.Coordinates);
            var depth = _cover.GetDepth(grid, tile);
            if (depth < Wh40kSnowCover.CoverFrom)
                continue;

            var facing = delta.ToWorldAngle();
            var side = right ? facing + PrintRotation : facing;
            var kind = _inventory.TryGetSlotEntity(uid, ShoesSlot, out _)
                ? Wh40kSnowPrintKind.Shoes
                : right ? Wh40kSnowPrintKind.BareRight : Wh40kSnowPrintKind.BareLeft;
            Lay(grid, tile, position + side.RotateVec(SideOffset), facing + PrintRotation, kind, depth);
        }

        if (_walkers.Count == 0 || _timing.CurFrame % 600 != 0)
            return;

        _stale.Clear();
        foreach (var uid in _walkers.Keys)
        {
            if (TerminatingOrDeleted(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _walkers.Remove(uid);
        }
    }

    /// <summary>Lays one print on a tile, making room on it if it's full.</summary>
    public Wh40kSnowPrint Lay(EntityUid grid, Vector2i tile, Vector2 position, Angle rotation, Wh40kSnowPrintKind kind, int depth)
    {
        if (!_prints.TryGetValue(grid, out var tiles))
        {
            tiles = new Dictionary<Vector2i, List<Wh40kSnowPrint>>();
            _prints[grid] = tiles;
        }

        if (!tiles.TryGetValue(tile, out var list))
        {
            list = new List<Wh40kSnowPrint>(MaxPerTile);
            tiles[tile] = list;
        }

        if (list.Count >= MaxPerTile)
            Remove(list[0]);

        var print = new Wh40kSnowPrint(grid, tile, position, rotation, kind, _timing.CurTime, depth);
        list.Add(print);
        print.OrderNode = _order.AddLast(print);
        PrintCount++;
        if (PrintCount > MaxPrints && _order.First is { } oldest)
            Remove(oldest.Value);

        return print;
    }

    private void OnSnowChunkChanged(NetEntity net, Vector2i chunk)
    {
        if (PrintCount == 0 || !TryGetEntity(net, out var grid) || !_prints.ContainsKey(grid.Value) || _forgottenGrids.Contains(grid.Value))
            return;

        if (!_changedChunks.TryGetValue(grid.Value, out var chunks))
            _changedChunks.Add(grid.Value, chunks = new HashSet<Vector2i>());
        chunks.Add(chunk);
    }

    private void OnSnowGridForgotten(NetEntity net)
    {
        if (TryGetEntity(net, out var grid) && _prints.ContainsKey(grid.Value))
            _forgottenGrids.Add(grid.Value);
    }

    private void RecheckChangedSnow()
    {
        // A frame can receive several chunk updates. Use the last depth, as when rechecking the whole cover.
        foreach (var grid in _forgottenGrids)
            RecheckGrid(grid, null);
        foreach (var (grid, chunks) in _changedChunks)
        {
            if (!_forgottenGrids.Contains(grid))
                RecheckGrid(grid, chunks);
        }

        _changedChunks.Clear();
        _forgottenGrids.Clear();
    }

    private void RecheckGrid(EntityUid grid, HashSet<Vector2i>? chunks)
    {
        if (!_prints.TryGetValue(grid, out var tiles))
            return;

        if (chunks != null && chunks.Count * Wh40kSnowCover.ChunkArea < tiles.Count)
        {
            foreach (var chunk in chunks)
            {
                for (var i = 0; i < Wh40kSnowCover.ChunkArea; i++)
                {
                    var tile = Wh40kSnowCover.TileAt(chunk, i);
                    if (tiles.TryGetValue(tile, out var prints))
                        RecheckTile(grid, tile, prints);
                }
            }

            return;
        }

        _recheckTiles.Clear();
        foreach (var tile in tiles.Keys)
        {
            if (chunks == null || chunks.Contains(Wh40kSnowCover.ChunkOf(tile)))
                _recheckTiles.Add(tile);
        }

        foreach (var tile in _recheckTiles)
            RecheckTile(grid, tile, tiles[tile]);
    }

    private void RecheckTile(EntityUid grid, Vector2i tile, List<Wh40kSnowPrint> prints)
    {
        var depth = _cover.GetDepth(grid, tile);
        for (var i = prints.Count - 1; i >= 0; i--)
        {
            var print = prints[i];
            print.Filled = Math.Clamp((depth - print.Depth) / (float) FillUnits, 0f, 1f);
            print.Thickness = Thickness(print.Depth, depth);
            if (depth == 0 || print.Filled >= 1f)
                Remove(print);
        }
    }

    private void OnGridRemoved(GridRemovalEvent ev)
    {
        var grid = ev.EntityUid;
        _changedChunks.Remove(grid);
        _forgottenGrids.Remove(grid);
        if (!_prints.Remove(grid, out var tiles))
            return;

        foreach (var prints in tiles.Values)
        {
            foreach (var print in prints)
            {
                _order.Remove(print.OrderNode);
                PrintCount--;
            }
        }
    }

    private void Remove(Wh40kSnowPrint print)
    {
        _order.Remove(print.OrderNode);
        PrintCount--;
        if (!_prints.TryGetValue(print.Grid, out var tiles) || !tiles.TryGetValue(print.Tile, out var list))
            return;

        list.Remove(print);
        if (list.Count > 0)
            return;

        tiles.Remove(print.Tile);
        if (tiles.Count == 0)
            _prints.Remove(print.Grid);
    }
}

/// <summary>Which footprint sprite a print uses.</summary>
public enum Wh40kSnowPrintKind : byte
{
    BareLeft,
    BareRight,
    Shoes,
}

/// <summary>One footprint in snow, in its grid's space.</summary>
public sealed class Wh40kSnowPrint(EntityUid grid, Vector2i tile, Vector2 position, Angle rotation, Wh40kSnowPrintKind kind, TimeSpan laid, int depth)
{
    public readonly EntityUid Grid = grid;
    public readonly Vector2i Tile = tile;
    public readonly Vector2 Position = position;
    public readonly Angle Rotation = rotation;
    public readonly Wh40kSnowPrintKind Kind = kind;
    public readonly TimeSpan Laid = laid;

    /// <summary>Snow depth on its tile when it was laid.</summary>
    public readonly int Depth = depth;

    /// <summary>How far fresh snow has filled it, 0 to 1.</summary>
    public float Filled;

    /// <summary>How much of its snow is left, 1 to 0, as melt thins the cover.</summary>
    public float Thickness = 1f;

    internal LinkedListNode<Wh40kSnowPrint> OrderNode = default!;
}
