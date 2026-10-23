using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Gravity;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.SectorMap;

/// <summary>
/// Tracks continuous exposure per victim, rather than restarting the countdown on each tile.
/// </summary>
public sealed partial class KoronusQuicksandSystem : EntitySystem
{
    public static readonly TimeSpan SinkDuration = TimeSpan.FromSeconds(5);

    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KoronusQuicksandComponent, StartCollideEvent>(OnContact);
        SubscribeLocalEvent<MobStateComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<MobStateComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<KoronusQuicksandSinkingComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<KoronusQuicksandSinkingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KoronusQuicksandSinkingComponent, AfterAutoHandleStateEvent>(OnHandleState);
        UpdatesAfter.Add(typeof(SharedPhysicsSystem));
    }

    private void OnMove(Entity<MobStateComponent> ent, ref MoveEvent args)
    {
        Track(ent);
    }

    private void OnMapInit(Entity<MobStateComponent> ent, ref MapInitEvent args)
    {
        Track(ent);
    }

    private void Track(EntityUid uid)
    {
        if (_net.IsClient || HasComp<KoronusQuicksandSinkingComponent>(uid) ||
            !HasComp<MovementSpeedModifierComponent>(uid) || !IsStandingOnQuicksand(Transform(uid), true))
            return;

        EnsureComp<KoronusQuicksandSinkingComponent>(uid);
    }

    private void OnContact(Entity<KoronusQuicksandComponent> ent, ref StartCollideEvent args)
    {
        if (_net.IsClient || !args.OtherFixture.Hard || TerminatingOrDeleted(args.OtherEntity) ||
            !HasComp<MobStateComponent>(args.OtherEntity) || !HasComp<MovementSpeedModifierComponent>(args.OtherEntity))
            return;

        EnsureComp<KoronusQuicksandSinkingComponent>(args.OtherEntity);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<KoronusQuicksandSinkingComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sinking, out var physics, out var transform))
        {
            // Read the actual tile, not cached physics contacts: sleeping bodies can be teleported.
            if (!IsStandingOnQuicksand(transform, true) || !HasComp<MobStateComponent>(uid))
            {
                RemCompDeferred<KoronusQuicksandSinkingComponent>(uid);
                SetSpeed((uid, sinking), 1f);
                continue;
            }

            if (!IsStandingOnQuicksand(transform) || physics.BodyStatus == BodyStatus.InAir || _gravity.IsWeightless(uid) ||
                _containers.IsEntityInContainer(uid) || HasComp<GodmodeComponent>(uid))
            {
                SetSpeed((uid, sinking), 1f);
                continue;
            }

            if (sinking.SpeedModifier == 1f)
                sinking.EnteredAt = _timing.CurTime;

            var elapsed = _timing.CurTime - sinking.EnteredAt;
            SetSpeed((uid, sinking), elapsed.TotalSeconds >= 4 ? 0f : elapsed.TotalSeconds >= 2 ? 0.25f : 0.5f);
            if (elapsed >= SinkDuration)
                QueueDel(uid);
        }
    }

    private bool IsStandingOnQuicksand(TransformComponent transform, bool ignoreCatwalk = false)
    {
        return TryComp<MapGridComponent>(transform.GridUid, out var grid) &&
               IsQuicksandTile((transform.GridUid.Value, grid),
                   _maps.LocalToTile(transform.GridUid.Value, grid, transform.Coordinates), ignoreCatwalk);
    }

    /// <summary>Used by evacuation as well, so a resource reset never returns someone to quicksand.</summary>
    public bool IsQuicksandTile(Entity<MapGridComponent> grid, Vector2i tile, bool ignoreCatwalk = false)
    {
        // Catwalks and newly placed foundations cover the hazard without having to reset the map.
        if (_tileDefinitions[_maps.GetTileRef(grid, tile).Tile.TypeId].ID != "FloorKoronusQuicksand")
            return false;

        var present = false;
        foreach (var anchored in _maps.GetAnchoredEntities(grid, tile))
        {
            if (!ignoreCatwalk && _tags.HasTag(anchored, "Catwalk"))
                return false;
            present |= HasComp<KoronusQuicksandComponent>(anchored) && !TerminatingOrDeleted(anchored);
        }
        return present;
    }

    private void SetSpeed(Entity<KoronusQuicksandSinkingComponent> ent, float speed)
    {
        if (ent.Comp.SpeedModifier == speed)
            return;

        ent.Comp.SpeedModifier = speed;
        Dirty(ent);
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRefreshSpeed(EntityUid uid, KoronusQuicksandSinkingComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.SpeedModifier, component.SpeedModifier);
    }

    private void OnShutdown(Entity<KoronusQuicksandSinkingComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.SpeedModifier = 1f;
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnHandleState(Entity<KoronusQuicksandSinkingComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);
    }
}
