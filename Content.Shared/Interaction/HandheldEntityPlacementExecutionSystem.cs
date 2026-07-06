using System.Collections.Generic;
using System.Linq;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared.Interaction;

/// <summary>
/// Server execution path for in-hand placement requests.
/// Client-side preview and placement mode lifecycle are handled on the client.
/// </summary>
public sealed partial class HandheldEntityPlacementExecutionSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly INetManager _net = default!;

    private readonly Dictionary<(EntityUid User, EntityUid Item), DoAfterId> _activePlacementDoAfters = new();

    public override void Initialize()
    {
        if (!_net.IsServer)
            return;

        SubscribeNetworkEvent<RequestHandheldEntityPlacementEvent>(OnPlacementRequest);
        SubscribeNetworkEvent<RequestCancelHandheldEntityPlacementEvent>(OnPlacementCancelRequest);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, HandheldEntityPlacementDoAfterEvent>(OnPlacementDoAfter);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, ComponentShutdown>(OnPlacementItemShutdown);
    }

    private void OnPlacementRequest(RequestHandheldEntityPlacementEvent ev, EntitySessionEventArgs args)
    {
        if (!_net.IsServer || args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        var item = GetEntity(ev.Item);
        if (!TryResolvePlacementRequest(user, item, ev.Coordinates, out var placement, out var requestedCoords))
            return;

        var direction = NormalizeDirection(ev.Direction, placement.CanRotate);
        var placementAttempt = new HandheldEntityPlacementAttemptEvent(user, requestedCoords, direction);
        RaiseLocalEvent(item, placementAttempt);

        if (placementAttempt.Cancelled || placementAttempt.DeployDelay <= TimeSpan.Zero)
            return;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            placementAttempt.DeployDelay,
            new HandheldEntityPlacementDoAfterEvent(
                GetNetCoordinates(placementAttempt.Coordinates),
                NormalizeDirection(placementAttempt.Direction, placement.CanRotate)),
            item,
            item,
            used: item)
        {
            BreakOnMove = placementAttempt.BreakOnMove,
            BreakOnDamage = placementAttempt.BreakOnDamage,
            BreakOnHandChange = placementAttempt.BreakOnHandChange,
            NeedHand = placementAttempt.NeedHand,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs, out var doAfterId) || doAfterId == null)
            return;

        _activePlacementDoAfters[(user, item)] = doAfterId.Value;
    }

    private bool TryResolvePlacementRequest(
        EntityUid user,
        EntityUid item,
        NetCoordinates requestedNetCoordinates,
        out HandheldEntityPlacementComponent placement,
        out EntityCoordinates requestedCoords)
    {
        placement = default!;
        requestedCoords = default;

        if (!Exists(item) ||
            !_hands.IsHolding(user, item) ||
            !TryComp(item, out HandheldEntityPlacementComponent? placementComp))
        {
            return false;
        }

        placement = placementComp;
        requestedCoords = GetCoordinates(requestedNetCoordinates);
        if (!_interaction.InRangeUnobstructed(user, requestedCoords, placement.Range, popup: true))
            return false;

        return true;
    }

    private void OnPlacementDoAfter(Entity<HandheldEntityPlacementComponent> ent, ref HandheldEntityPlacementDoAfterEvent args)
    {
        if (!_net.IsServer)
            return;

        CleanupTrackedDoAfter(args.User, ent.Owner, args.DoAfter.Id);

        if (args.Cancelled || args.Handled || !_hands.IsHolding(args.User, ent.Owner))
            return;

        var requestedCoords = GetCoordinates(args.Coordinates);
        if (!_interaction.InRangeUnobstructed(args.User, requestedCoords, ent.Comp.Range))
            return;

        var direction = NormalizeDirection(args.Direction, ent.Comp.CanRotate);
        var completed = new HandheldEntityPlacementCompleteEvent(args.User, requestedCoords, direction);
        RaiseLocalEvent(ent.Owner, completed);
        args.Handled = completed.Handled;
    }

    private void OnPlacementCancelRequest(RequestCancelHandheldEntityPlacementEvent ev, EntitySessionEventArgs args)
    {
        if (!_net.IsServer || args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        var item = GetEntity(ev.Item);
        if (!item.IsValid() || !_activePlacementDoAfters.Remove((user, item), out var doAfterId))
            return;

        _doAfter.Cancel(doAfterId);
    }

    private void CleanupTrackedDoAfter(EntityUid user, EntityUid item, DoAfterId finishedDoAfterId)
    {
        var key = (user, item);
        if (_activePlacementDoAfters.TryGetValue(key, out var trackedDoAfter) &&
            trackedDoAfter == finishedDoAfterId)
        {
            _activePlacementDoAfters.Remove(key);
        }
    }

    private void OnPlacementItemShutdown(Entity<HandheldEntityPlacementComponent> ent, ref ComponentShutdown args)
    {
        if (!_net.IsServer || _activePlacementDoAfters.Count == 0)
            return;

        foreach (var (key, doAfterId) in _activePlacementDoAfters.Where(pair => pair.Key.Item == ent.Owner).ToArray())
        {
            _activePlacementDoAfters.Remove(key);

            if (_doAfter.IsRunning(doAfterId))
                _doAfter.Cancel(doAfterId);
        }
    }

    private static Direction NormalizeDirection(Direction direction, bool canRotate)
    {
        if (!canRotate || direction == Direction.Invalid)
            return Direction.North;

        return direction.ToAngle().GetCardinalDir();
    }
}
