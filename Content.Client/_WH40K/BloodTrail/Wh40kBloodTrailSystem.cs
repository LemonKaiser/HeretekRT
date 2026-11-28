using System.Numerics;
using Content.Shared._WH40K.BloodTrail;
using Content.Shared.CCVar;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.BloodTrail;

/// <summary>
/// Renders blood footprints and drag trails entirely client-side from lightweight <see cref="Wh40kBloodStainComponent"/>
/// state. Strictly bounded by global and per-tile ring limits so heavy firefights never degrade FPS or server tickrate.
/// </summary>
public sealed partial class Wh40kBloodTrailSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Global cap on live blood prints across all grids; oldest are evicted in O(1).</summary>
    public const int MaxPrints = 800;

    /// <summary>Cap per tile to prevent overdraw stacking in choke points.</summary>
    public const int MaxPerTile = 4;

    /// <summary>Walking step distance between footprints.</summary>
    private const float WalkStepSize = 0.65f;

    /// <summary>Dragging/crawling step distance between smears.</summary>
    private const float DragStepSize = 0.45f;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(150);
    public static readonly TimeSpan FadeWindow = TimeSpan.FromSeconds(45);

    private static readonly Vector2 SideOffset = new(0.1f, 0f);
    private static readonly Angle StepRotationOffset = Angle.FromDegrees(180);
    private static readonly Angle DragRotationOffset = Angle.FromDegrees(-90);
    private const string ShoesSlot = "shoes";

    private readonly Dictionary<EntityUid, Dictionary<Vector2i, List<Wh40kBloodPrint>>> _prints = new();
    private readonly LinkedList<Wh40kBloodPrint> _order = new();
    private readonly Dictionary<EntityUid, WalkerState> _walkers = new();
    private readonly List<EntityUid> _staleWalkers = new();
    private readonly List<EntityUid> _staleGrids = new();

    private EntityQuery<HumanoidAppearanceComponent> _humanoidQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    private Wh40kBloodTrailOverlay _bloodOverlay = default!;
    private bool _enabled = true;

    public int PrintCount { get; private set; }
    public IReadOnlyDictionary<EntityUid, Dictionary<Vector2i, List<Wh40kBloodPrint>>> Prints => _prints;

    public override void Initialize()
    {
        base.Initialize();

        _humanoidQuery = GetEntityQuery<HumanoidAppearanceComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();

        _bloodOverlay = new Wh40kBloodTrailOverlay(this);
        Subs.CVar(_cfg, CCVars.WH40KBloodTrails, OnEnabledChanged, true);

        SubscribeNetworkEvent<Wh40kBloodTrailCleanEvent>(OnCleanTile);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay(_bloodOverlay);
        Clear();
    }

    private void OnEnabledChanged(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            _overlay.AddOverlay(_bloodOverlay);
        }
        else
        {
            _overlay.RemoveOverlay(_bloodOverlay);
            Clear();
        }
    }

    public void Clear()
    {
        _prints.Clear();
        _order.Clear();
        _walkers.Clear();
        PrintCount = 0;
    }

    public float Visibility(Wh40kBloodPrint print)
    {
        var remaining = Lifetime - (_timing.CurTime - print.Laid);
        if (remaining <= TimeSpan.Zero)
            return 0f;

        if (remaining >= FadeWindow)
            return 1f;

        return (float) (remaining / FadeWindow);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_enabled)
            return;

        var now = _timing.CurTime;
        while (_order.First is { } oldest && now - oldest.Value.Laid >= Lifetime)
            Remove(oldest.Value);

        UpdateWalkers();

        if (_timing.CurFrame % 300 == 0)
            PruneStale();
    }

    private void UpdateWalkers()
    {
        var query = EntityQueryEnumerator<Wh40kBloodStainComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var stain, out var xform))
        {
            if (xform.GridUid is not { } grid || xform.ParentUid != grid)
                continue;

            var pos = xform.LocalPosition;
            if (!_walkers.TryGetValue(uid, out var state))
            {
                state = new WalkerState
                {
                    Grid = grid,
                    LastPos = pos,
                    LastEpoch = stain.StainEpoch,
                    Remaining = stain.MaxPrints,
                    MaxPrints = Math.Max((byte) 1, stain.MaxPrints),
                    BaseColor = stain.StainColor.WithAlpha(Math.Clamp(stain.StainColor.A * stain.StainStrength, 0.1f, 0.9f)),
                };
                _walkers[uid] = state;
                continue;
            }

            if (state.LastEpoch != stain.StainEpoch)
            {
                state.LastEpoch = stain.StainEpoch;
                state.Grid = grid;
                state.LastPos = pos;
                state.MaxPrints = Math.Max((byte) 1, stain.MaxPrints);
                state.Remaining = state.MaxPrints;
                state.BaseColor = stain.StainColor.WithAlpha(Math.Clamp(stain.StainColor.A * stain.StainStrength, 0.1f, 0.9f));
                continue;
            }

            if (state.Remaining <= 0)
                continue;

            if (state.Grid != grid)
            {
                state.Grid = grid;
                state.LastPos = pos;
                continue;
            }

            var delta = pos - state.LastPos;
            var distSq = delta.LengthSquared();
            if (distSq < DragStepSize * DragStepSize)
                continue;

            var dragging = _standing.IsDown(uid) || _mobState.IsIncapacitated(uid) || !_humanoidQuery.HasComp(uid);
            var stepSize = dragging ? DragStepSize : WalkStepSize;
            if (distSq < stepSize * stepSize)
                continue;

            // Skip teleports / large snaps without stamping across the room.
            if (distSq > 9f)
            {
                state.LastPos = pos;
                continue;
            }

            state.LastPos = pos;

            if (_gravity.IsWeightless(uid) || (_physicsQuery.TryComp(uid, out var body) && body.BodyStatus != BodyStatus.OnGround))
                continue;

            if (!_gridQuery.TryComp(grid, out var gridComp))
                continue;

            var tile = _map.LocalToTile(grid, gridComp, xform.Coordinates);
            var fadeFactor = state.Remaining / (float) state.MaxPrints;
            var color = state.BaseColor.WithAlpha(state.BaseColor.A * (0.25f + 0.75f * fadeFactor));

            state.RightStep = !state.RightStep;
            state.Remaining--;

            if (dragging)
            {
                var kind = (Wh40kBloodPrintKind) ((int) Wh40kBloodPrintKind.Drag1 + _random.Next(5));
                Lay(grid, tile, pos, delta.ToAngle() + DragRotationOffset, kind, color);
            }
            else
            {
                var facing = delta.ToWorldAngle();
                var side = state.RightStep ? facing + StepRotationOffset : facing;
                Wh40kBloodPrintKind kind;
                if (stain.HeavySuit)
                    kind = Wh40kBloodPrintKind.Suit;
                else if (_inventory.TryGetSlotEntity(uid, ShoesSlot, out _))
                    kind = Wh40kBloodPrintKind.Shoes;
                else
                    kind = state.RightStep ? Wh40kBloodPrintKind.BareRight : Wh40kBloodPrintKind.BareLeft;

                Lay(grid, tile, pos + side.RotateVec(SideOffset), facing + StepRotationOffset, kind, color);
            }
        }
    }

    private void Lay(EntityUid grid, Vector2i tile, Vector2 position, Angle rotation, Wh40kBloodPrintKind kind, Color color)
    {
        if (!_prints.TryGetValue(grid, out var tiles))
        {
            tiles = new Dictionary<Vector2i, List<Wh40kBloodPrint>>();
            _prints[grid] = tiles;
        }

        if (!tiles.TryGetValue(tile, out var list))
        {
            list = new List<Wh40kBloodPrint>(MaxPerTile);
            tiles[tile] = list;
        }

        if (list.Count >= MaxPerTile)
            Remove(list[0]);

        var print = new Wh40kBloodPrint(grid, tile, position, rotation, kind, color, _timing.CurTime);
        list.Add(print);
        print.OrderNode = _order.AddLast(print);
        PrintCount++;

        if (PrintCount > MaxPrints && _order.First is { } oldest)
            Remove(oldest.Value);
    }

    private void OnCleanTile(Wh40kBloodTrailCleanEvent ev)
    {
        if (!TryGetEntity(ev.Grid, out var gridUid) || !_prints.TryGetValue(gridUid.Value, out var tiles))
            return;

        var center = ev.Tile + new Vector2(0.5f, 0.5f);
        var radiusSq = ev.Radius * ev.Radius;
        var span = (int) MathF.Ceiling(ev.Radius);

        for (var dx = -span; dx <= span; dx++)
        {
            for (var dy = -span; dy <= span; dy++)
            {
                var tile = new Vector2i(ev.Tile.X + dx, ev.Tile.Y + dy);
                if (!tiles.TryGetValue(tile, out var list))
                    continue;

                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if ((list[i].Position - center).LengthSquared() <= radiusSq)
                    {
                        _order.Remove(list[i].OrderNode);
                        list.RemoveAt(i);
                        PrintCount--;
                    }
                }

                if (list.Count == 0)
                    tiles.Remove(tile);
            }
        }

        if (tiles.Count == 0)
            _prints.Remove(gridUid.Value);
    }

    private void PruneStale()
    {
        if (_walkers.Count > 0)
        {
            _staleWalkers.Clear();
            foreach (var uid in _walkers.Keys)
            {
                if (TerminatingOrDeleted(uid) || !HasComp<Wh40kBloodStainComponent>(uid))
                    _staleWalkers.Add(uid);
            }

            foreach (var uid in _staleWalkers)
            {
                _walkers.Remove(uid);
            }
        }

        if (_prints.Count > 0)
        {
            _staleGrids.Clear();
            foreach (var grid in _prints.Keys)
            {
                if (TerminatingOrDeleted(grid))
                    _staleGrids.Add(grid);
            }

            foreach (var grid in _staleGrids)
            {
                foreach (var list in _prints[grid].Values)
                {
                    foreach (var print in list)
                    {
                        _order.Remove(print.OrderNode);
                        PrintCount--;
                    }
                }

                _prints.Remove(grid);
            }
        }
    }

    private void Remove(Wh40kBloodPrint print)
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

    private sealed class WalkerState
    {
        public EntityUid Grid;
        public Vector2 LastPos;
        public int LastEpoch;
        public int Remaining;
        public int MaxPrints;
        public bool RightStep;
        public Color BaseColor;
    }
}

public enum Wh40kBloodPrintKind : byte
{
    BareLeft = 0,
    BareRight = 1,
    Shoes = 2,
    Suit = 3,
    Drag1 = 4,
    Drag2 = 5,
    Drag3 = 6,
    Drag4 = 7,
    Drag5 = 8,
}

public sealed class Wh40kBloodPrint(
    EntityUid grid,
    Vector2i tile,
    Vector2 position,
    Angle rotation,
    Wh40kBloodPrintKind kind,
    Color color,
    TimeSpan laid)
{
    public readonly EntityUid Grid = grid;
    public readonly Vector2i Tile = tile;
    public readonly Vector2 Position = position;
    public readonly Angle Rotation = rotation;
    public readonly Wh40kBloodPrintKind Kind = kind;
    public readonly Color Color = color;
    public readonly TimeSpan Laid = laid;
    internal LinkedListNode<Wh40kBloodPrint> OrderNode = default!;
}
