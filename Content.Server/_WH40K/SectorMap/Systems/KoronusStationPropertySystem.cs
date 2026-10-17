using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Cargo.Components;
using Content.Server.Storage.Components;
using Content.Shared._WH40K.PersistentInventory;
using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.Damage;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Item;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>Origin-based protection with a one-time snapshot and movement-driven, deferred returns.</summary>
public sealed partial class KoronusStationPropertySystem : EntitySystem
{
    [Dependency] private SharedKoronusStationPropertySystem _property = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan ReturnDelay = TimeSpan.FromSeconds(15);

    private readonly EntityWhitelist _free = new()
    {
        Components = ["Food", "Drink"],
        Tags = ["Trash", "DrinkGlass", "Paper", "Pen", "Cigarette"],
    };

    private readonly EntityWhitelist _local = new()
    {
        Components = ["Healing", "Tool", "Spray"],
        Tags = ["Medkit", "Mop", "Bucket", "Soap"],
    };

    private readonly EntityWhitelist _dangerous = new() { Components = ["Gun", "Explosive", "ProjectileGrenade", "ScatteringGrenade"] };
    private readonly EntityWhitelist _stationaryKits = new() { Tags = ["Medkit"] };
    private readonly HashSet<EntityUid> _pending = new();
    private readonly List<EntityUid> _work = new();
    private readonly List<EntityUid> _ancestors = new();
    private readonly HashSet<EntityUid> _pendingGrids = new();
    // One removable entry per abandoned item, ordered by deadline. No cancelled timers accumulate.
    private readonly SortedSet<(TimeSpan At, long Sequence, EntityUid Uid)> _returns = new(
        Comparer<(TimeSpan At, long Sequence, EntityUid Uid)>.Create(static (a, b) =>
            a.At != b.At ? a.At.CompareTo(b.At) : a.Sequence.CompareTo(b.Sequence)));
    private long _returnSequence;

    public override void Initialize()
    {
        SubscribeLocalEvent<KoronusStationPropertyOriginComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<KoronusStationPropertyTrackingComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<KoronusStationPropertyTrackingComponent, PullStartedMessage>(OnPullChanged);
        SubscribeLocalEvent<KoronusStationPropertyTrackingComponent, PullStoppedMessage>(OnPullChanged);
        SubscribeLocalEvent<KoronusStationPropertyTrackingComponent, ContainerGettingInsertedAttemptEvent>(OnInsertContents);
        SubscribeLocalEvent<KoronusStationPropertyOriginComponent, EntGotInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<KoronusStationPropertyOriginComponent, EntGotRemovedFromContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<KoronusStationPropertyOriginComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KoronusStationPropertyComponent, StackSplitEvent>(OnSplit);
        SubscribeLocalEvent<KoronusStationPropertyComponent, BeforeDamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<KoronusStationPropertyRegistryComponent, MapInitEvent>(OnGridMapInit);
    }

    /// <summary>Only call immediately after authored map contents have finished spawning.</summary>
    public void ProtectGrid(EntityUid grid)
    {
        if (HasComp<KoronusStationPropertyRegistryComponent>(grid))
            return;

        AddComp<KoronusStationPropertyRegistryComponent>(grid);
        // Round-start maps receive their MapInit later than PostGameMapLoad. Storage fills and
        // random spawners must finish before taking the snapshot, or their contents stay unprotected.
        if (!_maps.IsInitialized(Transform(grid).MapID))
            return;

        _pendingGrids.Remove(grid);
        SnapshotGrid(grid);
    }

    private void OnGridMapInit(EntityUid uid, KoronusStationPropertyRegistryComponent registry, MapInitEvent args)
        => _pendingGrids.Add(uid);

    private void SnapshotGrid(EntityUid grid)
    {
        var pending = new Stack<(EntityUid Uid, bool Local)>();
        pending.Push((grid, false));
        while (pending.TryPop(out var entry))
        {
            var (uid, local) = entry;
            if (HasComp<MobStateComponent>(uid))
                continue; // NPC inventories and players have their own ownership/protection rules.

            var xform = Transform(uid);
            if (uid != grid && xform.GridUid != grid)
                continue;

            if (uid != grid)
            {
                var stationaryKit = _whitelist.IsWhitelistPass(_stationaryKits, uid);
                if (HasComp<StorageComponent>(uid) || HasComp<EntityStorageComponent>(uid))
                {
                    var storage = EnsureComp<KoronusStationPropertyStorageComponent>(uid);
                    storage.HomeGrid = grid;
                    Dirty(uid, storage);
                }

                if (TryComp<KoronusStationPropertyComponent>(uid, out var configured))
                    local = stationaryKit || configured.Policy == KoronusStationPropertyPolicy.LocalUse;
                else
                    local = !_whitelist.IsWhitelistPass(_dangerous, uid) &&
                            (local || _whitelist.IsWhitelistPass(_local, uid));

                if ((HasComp<ItemComponent>(uid) || !xform.Anchored && HasComp<PullableComponent>(uid)) &&
                    (configured != null || _whitelist.IsWhitelistPass(_dangerous, uid) || !_whitelist.IsWhitelistPass(_free, uid)))
                {
                    var property = configured ?? AddComp<KoronusStationPropertyComponent>(uid);
                    property.HomeGrid = grid;
                    if (configured == null)
                        property.Policy = local && !stationaryKit
                            ? KoronusStationPropertyPolicy.LocalUse
                            : KoronusStationPropertyPolicy.Display;
                    Dirty(uid, property);
                    if (_property.IsProperty(uid))
                        Track(uid, grid);
                }
            }

            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var child))
                pending.Push((child, local));
        }
    }

    private void Track(EntityUid uid, EntityUid home)
    {
        var origin = EnsureComp<KoronusStationPropertyOriginComponent>(uid);
        origin.OriginalPosition = Transform(uid).Coordinates;
        origin.HomePosition = _transform.ToCoordinates(home, _transform.GetMapCoordinates(uid));
        _containers.TryGetContainingContainer((uid, null, null), out origin.OriginalContainer);
        // Reuse the persistence exclusion: station supplies cannot be restored later as unowned items.
        EnsureComp<NoPersistentInventoryComponent>(uid);
        EnsureComp<CargoSellBlacklistComponent>(uid);
        RebuildAncestors(uid, origin);
    }

    private void RebuildAncestors(EntityUid uid, KoronusStationPropertyOriginComponent origin)
    {
        _ancestors.Clear();
        var current = uid;
        while (TryComp(current, out TransformComponent? xform) &&
               !HasComp<MapGridComponent>(current) && !HasComp<MapComponent>(current))
        {
            _ancestors.Add(current);
            current = xform.ParentUid;
        }

        foreach (var previous in origin.Ancestors)
        {
            if (!_ancestors.Contains(previous))
                RemoveAncestor(uid, previous);
        }
        foreach (var ancestor in _ancestors)
        {
            if (!origin.Ancestors.Contains(ancestor))
                EnsureComp<KoronusStationPropertyTrackingComponent>(ancestor).Items.Add(uid);
        }
        origin.Ancestors.Clear();
        origin.Ancestors.AddRange(_ancestors);
    }

    private void Untrack(EntityUid uid, KoronusStationPropertyOriginComponent origin)
    {
        foreach (var ancestor in origin.Ancestors)
            RemoveAncestor(uid, ancestor);
        origin.Ancestors.Clear();
    }

    private void RemoveAncestor(EntityUid uid, EntityUid ancestor)
    {
        if (!TryComp<KoronusStationPropertyTrackingComponent>(ancestor, out var tracking))
            return;

        tracking.Items.Remove(uid);
        if (tracking.Items.Count == 0)
            RemComp<KoronusStationPropertyTrackingComponent>(ancestor);
    }

    private void OnShutdown(EntityUid uid, KoronusStationPropertyOriginComponent origin, ComponentShutdown args)
    {
        CancelReturn(uid, origin);
        Untrack(uid, origin);
        _pending.Remove(uid);
    }

    private void OnMove(EntityUid uid, KoronusStationPropertyOriginComponent origin, ref MoveEvent args)
    {
        // Walking with a tool does not move its local transform. Only the item itself needs this
        // listener, for displacement/throwing. Once scheduled, motion cannot extend the deadline.
        if (origin.ReturnAt == null && !IsAtHome(uid, origin))
            _pending.Add(uid);
    }

    private void OnParentChanged(EntityUid uid, KoronusStationPropertyTrackingComponent tracking, ref EntParentChangedMessage args)
        => _pending.UnionWith(tracking.Items);

    private void OnPullChanged(EntityUid uid, KoronusStationPropertyTrackingComponent tracking, PullMessage args)
        => _pending.UnionWith(tracking.Items);

    private void OnContainerChanged(EntityUid uid, KoronusStationPropertyOriginComponent origin, ContainerModifiedMessage args)
        => _pending.Add(uid);

    private void OnInsertContents(EntityUid uid, KoronusStationPropertyTrackingComponent tracking, ContainerGettingInsertedAttemptEvent args)
    {
        // Entering a bed/locker must not disable a passenger's normal interactions. Any supplies
        // in their hands are returned separately if that destination is unsuitable.
        if (HasComp<MobStateComponent>(uid))
            return;

        foreach (var item in tracking.Items)
        {
            if (TryComp<KoronusStationPropertyComponent>(item, out var property) && !_property.CanInsert(property, args.Container))
            {
                args.Cancel();
                return;
            }
        }
    }

    private void OnDamage(EntityUid uid, KoronusStationPropertyComponent property, ref BeforeDamageChangedEvent args)
    {
        if (_property.IsProperty(uid) && args.Damage.GetTotal() > 0)
            args.Cancelled = true;
    }

    private void OnSplit(EntityUid uid, KoronusStationPropertyComponent property, ref StackSplitEvent args)
    {
        if (property.HomeGrid is not { } home || !_property.IsProperty(uid))
            return;

        var split = EnsureComp<KoronusStationPropertyComponent>(args.NewId);
        split.HomeGrid = home;
        split.Policy = property.Policy;
        Dirty(args.NewId, split);
        Track(args.NewId, home);
        // A split belongs to the same home storage, even if split in somebody's hands or off-grid.
        var source = Comp<KoronusStationPropertyOriginComponent>(uid);
        var origin = Comp<KoronusStationPropertyOriginComponent>(args.NewId);
        origin.HomePosition = source.HomePosition;
        origin.OriginalPosition = source.OriginalPosition;
        origin.OriginalContainer = source.OriginalContainer;
        _pending.Add(args.NewId);
    }

    public override void Update(float frameTime)
    {
        foreach (var grid in _pendingGrids)
        {
            if (!TerminatingOrDeleted(grid))
                SnapshotGrid(grid);
        }
        _pendingGrids.Clear();

        ProcessChanges();
        ProcessReturns();
    }

    private void ProcessChanges()
    {
        if (_pending.Count == 0)
            return;

        _work.Clear();
        _work.AddRange(_pending);
        _pending.Clear();
        // Return outer containers first, preserving their contents and remaining supplies.
        _work.Sort((first, second) =>
            (TryComp<KoronusStationPropertyOriginComponent>(first, out var a) ? a.Ancestors.Count : 0).CompareTo(
                TryComp<KoronusStationPropertyOriginComponent>(second, out var b) ? b.Ancestors.Count : 0));
        foreach (var uid in _work)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<KoronusStationPropertyOriginComponent>(uid, out var origin) ||
                !TryComp<KoronusStationPropertyComponent>(uid, out var property) || property.HomeGrid is not { } home ||
                TerminatingOrDeleted(home))
                continue;

            if (!IsAllowed(uid, property, origin))
                Return(uid, property, origin);
            else if (property.Policy == KoronusStationPropertyPolicy.LocalUse &&
                     !IsAtHome(uid, origin) && !IsInUse(uid))
                ScheduleReturn(uid, origin);
            else
                CancelReturn(uid, origin);

            RebuildAncestors(uid, origin);
        }
    }

    private bool IsAtHome(EntityUid uid, KoronusStationPropertyOriginComponent origin)
        => Transform(uid).Coordinates == origin.OriginalPosition &&
           (origin.OriginalContainer == null || origin.OriginalContainer.Contains(uid));

    private bool IsInUse(EntityUid uid)
    {
        var current = uid;
        while (true)
        {
            if (_pulling.IsPulled(current))
                return true;

            if (!_containers.TryGetContainingContainer((current, null, null), out var container))
                return false;

            if (TryComp<HandsComponent>(container.Owner, out var hands) &&
                _hands.IsHolding(container.Owner, current, out _, hands))
                return true;

            current = container.Owner;
        }
    }

    private void ScheduleReturn(EntityUid uid, KoronusStationPropertyOriginComponent origin)
    {
        if (origin.ReturnAt != null)
            return;

        origin.ReturnAt = _timing.CurTime + ReturnDelay;
        origin.ReturnSequence = ++_returnSequence;
        _returns.Add((origin.ReturnAt.Value, origin.ReturnSequence, uid));
    }

    private void CancelReturn(EntityUid uid, KoronusStationPropertyOriginComponent origin)
    {
        if (origin.ReturnAt is not { } at)
            return;

        _returns.Remove((at, origin.ReturnSequence, uid));
        origin.ReturnAt = null;
    }

    private void ProcessReturns()
    {
        // Only inspect the next deadline each tick; item checks run once when it expires.
        while (_returns.Count > 0 && _returns.Min.At <= _timing.CurTime)
        {
            var next = _returns.Min;
            _returns.Remove(next);
            if (!TryComp<KoronusStationPropertyOriginComponent>(next.Uid, out var origin))
                continue;

            origin.ReturnAt = null;
            if (TerminatingOrDeleted(next.Uid) || !TryComp<KoronusStationPropertyComponent>(next.Uid, out var property) ||
                property.HomeGrid is not { } home || TerminatingOrDeleted(home) ||
                IsAtHome(next.Uid, origin) || IsInUse(next.Uid))
                continue;

            Return(next.Uid, property, origin);
            RebuildAncestors(next.Uid, origin);
        }
    }

    private bool IsAllowed(EntityUid uid, KoronusStationPropertyComponent property, KoronusStationPropertyOriginComponent origin)
    {
        if (Transform(uid).GridUid != property.HomeGrid)
            return false;

        if (property.Policy == KoronusStationPropertyPolicy.Display)
            return Transform(uid).Coordinates == origin.OriginalPosition;

        var current = uid;
        while (_containers.TryGetContainingContainer((current, null, null), out var container))
        {
            if (!_property.CanInsert(property, container))
                return false;
            current = container.Owner;
        }
        return true;
    }

    private void Return(EntityUid uid, KoronusStationPropertyComponent property, KoronusStationPropertyOriginComponent origin)
    {
        CancelReturn(uid, origin);
        EntityUid? carrier = null;
        if (_containers.TryGetContainingContainer((uid, null, null), out var container))
        {
            carrier = container.Owner;
            _containers.Remove(uid, container, force: true);
        }
        if (TryComp<PullableComponent>(uid, out var pullable))
            _pulling.TryStopPull(uid, pullable);

        var destination = origin.HomePosition;
        if (origin.OriginalContainer == null &&
            !TerminatingOrDeleted(origin.OriginalPosition.EntityId) &&
            (origin.OriginalPosition.EntityId == property.HomeGrid ||
             Transform(origin.OriginalPosition.EntityId).GridUid == property.HomeGrid))
            destination = origin.OriginalPosition;
        _transform.SetCoordinates(uid, destination);
        if (origin.OriginalContainer is { } original && !TerminatingOrDeleted(original.Owner) &&
            Transform(original.Owner).GridUid == property.HomeGrid &&
            _containers.TryGetContainer(original.Owner, original.ID, out var existing) && existing == original &&
            (original is not ContainerSlot slot || slot.ContainedEntity == null))
            _containers.Insert(uid, original, force: property.Policy == KoronusStationPropertyPolicy.Display);

        // A deleted/full/exported original container cannot be the destination of repeated returns.
        origin.OriginalPosition = Transform(uid).Coordinates;
        _containers.TryGetContainingContainer((uid, null, null), out origin.OriginalContainer);

        if (TryComp<PhysicsComponent>(uid, out var body))
        {
            _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
            _physics.SetAngularVelocity(uid, 0f, body: body);
        }
        if (carrier is { } user)
            _popup.PopupEntity(Loc.GetString("koronus-property-returned"), user, user);
    }
}
