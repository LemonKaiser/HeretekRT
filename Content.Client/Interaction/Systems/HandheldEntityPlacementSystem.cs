using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client.Hands.Systems;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Robust.Client.GameObjects;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Interaction.Systems;

public sealed partial class HandheldEntityPlacementSystem : EntitySystem
{
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly IPlacementManager _placement = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityUid? _pendingBeginPlacementItem;
    private readonly Dictionary<EntityUid, PendingPlacementRequest> _pendingPlacementRequests = new();
    private readonly HashSet<EntityUid> _pendingPlacementCancellationRequests = new();
    private EntityUid? _lastActivePlacementItem;

    public override void Initialize()
    {
        SubscribeLocalEvent<HandheldEntityPlacementComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, HandDeselectedEvent>(OnHandDeselected);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, GotUnequippedHandEvent>(OnGotUnequippedHand);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, ComponentShutdown>(OnPlacementItemShutdown);
    }

    private void OnUseInHand(Entity<HandheldEntityPlacementComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (IsPlacementActiveForItem(ent.Owner))
        {
            ClearPlacementForItem(ent.Owner);
            args.Handled = true;
            return;
        }

        args.Handled = true;
        BeginPlacementDeferred(ent);
    }

    private void OnAfterInteract(Entity<HandheldEntityPlacementComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target != null || !args.CanReach || IsPlacementActiveForItem(ent.Owner))
            return;

        args.Handled = true;
        BeginPlacementDeferred(ent);
    }

    private void OnHandDeselected(Entity<HandheldEntityPlacementComponent> ent, ref HandDeselectedEvent args)
    {
        ClearPlacementForItem(ent.Owner);
    }

    private void OnGotUnequippedHand(Entity<HandheldEntityPlacementComponent> ent, ref GotUnequippedHandEvent args)
    {
        ClearPlacementForItem(ent.Owner);
    }

    private void OnPlacementItemShutdown(Entity<HandheldEntityPlacementComponent> ent, ref ComponentShutdown args)
    {
        ClearPlacementForItem(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        FlushDeferredActions();

        if (!TryGetActivePlacementItem(out var item))
        {
            if (_lastActivePlacementItem is { } previousItem)
                RequestPlacementCancellation(previousItem);

            _lastActivePlacementItem = null;
            return;
        }

        _lastActivePlacementItem = item;

        if (!IsLocalPlayerHolding(item))
            ClearPlacementForItem(item);
    }

    private void FlushDeferredActions()
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (_pendingBeginPlacementItem is { } beginItem)
        {
            _pendingBeginPlacementItem = null;

            if (Exists(beginItem) &&
                IsLocalPlayerHolding(beginItem) &&
                TryComp(beginItem, out HandheldEntityPlacementComponent? comp))
            {
                BeginPlacement((beginItem, comp));
            }
        }

        if (_pendingPlacementRequests.Count > 0)
        {
            foreach (var request in _pendingPlacementRequests.Values)
            {
                if (!Exists(request.Item) || !IsLocalPlayerHolding(request.Item))
                    continue;

                RequestPlacementInternal(request.Item, request.Coordinates, request.Direction);
            }

            _pendingPlacementRequests.Clear();
        }

        if (_pendingPlacementCancellationRequests.Count > 0)
        {
            foreach (var item in _pendingPlacementCancellationRequests)
            {
                if (Exists(item))
                    RequestPlacementCancellationInternal(item);
            }

            _pendingPlacementCancellationRequests.Clear();
        }
    }

    private void BeginPlacementDeferred(Entity<HandheldEntityPlacementComponent> ent)
    {
        if (!_timing.IsFirstTimePredicted)
        {
            _pendingBeginPlacementItem = ent.Owner;
            return;
        }

        BeginPlacement(ent);
    }

    private void BeginPlacement(Entity<HandheldEntityPlacementComponent> ent)
    {
        _lastActivePlacementItem = ent.Owner;
        _pendingPlacementCancellationRequests.Remove(ent.Owner);

        var previewEntity = ent.Comp.PreviewEntityType ?? ent.Comp.EntityType;
        _placement.BeginPlacing(new PlacementInformation
        {
            MobUid = ent.Owner,
            EntityType = previewEntity,
            PlacementOption = ent.Comp.PlacementMode,
            Range = ent.Comp.Range,
        }, new HandheldEntityPlacementHijack(
            ent.Owner,
            previewEntity,
            ent.Comp.CanRotate,
            _prototype,
            _sprite,
            RequestPlacement));
    }

    private void RequestPlacement(EntityUid item, EntityCoordinates coordinates, Direction direction)
    {
        if (!_timing.IsFirstTimePredicted)
        {
            _pendingPlacementRequests[item] = new PendingPlacementRequest(item, coordinates, direction);
            return;
        }

        RequestPlacementInternal(item, coordinates, direction);
    }

    private void RequestPlacementInternal(EntityUid item, EntityCoordinates coordinates, Direction direction)
    {
        RaiseNetworkEvent(new RequestHandheldEntityPlacementEvent(
            GetNetEntity(item),
            GetNetCoordinates(coordinates),
            direction));
    }

    private void RequestPlacementCancellation(EntityUid item)
    {
        if (!_timing.IsFirstTimePredicted)
        {
            _pendingPlacementCancellationRequests.Add(item);
            return;
        }

        RequestPlacementCancellationInternal(item);
    }

    private void RequestPlacementCancellationInternal(EntityUid item)
    {
        RaiseNetworkEvent(new RequestCancelHandheldEntityPlacementEvent(GetNetEntity(item)));
    }

    private void ClearPlacementForItem(EntityUid item)
    {
        var shouldNotifyServerCancel =
            _pendingBeginPlacementItem == item ||
            _pendingPlacementRequests.ContainsKey(item) ||
            _pendingPlacementCancellationRequests.Contains(item) ||
            _lastActivePlacementItem == item ||
            IsPlacementActiveForItem(item);

        if (_pendingBeginPlacementItem == item)
            _pendingBeginPlacementItem = null;

        _pendingPlacementRequests.Remove(item);
        _pendingPlacementCancellationRequests.Remove(item);

        if (shouldNotifyServerCancel)
            RequestPlacementCancellation(item);

        if (_lastActivePlacementItem == item)
            _lastActivePlacementItem = null;

        if (IsPlacementActiveForItem(item))
            _placement.Clear();
    }

    private bool IsPlacementActiveForItem(EntityUid item)
    {
        return TryGetActivePlacementItem(out var activeItem) && activeItem == item;
    }

    private bool TryGetActivePlacementItem(out EntityUid item)
    {
        item = default;

        if (!_placement.IsActive || _placement.CurrentPermission?.MobUid is not { } placer)
            return false;

        if (!Exists(placer) || !HasComp<HandheldEntityPlacementComponent>(placer))
            return false;

        item = placer;
        return true;
    }

    private bool IsLocalPlayerHolding(EntityUid item)
    {
        if (!_hands.TryGetPlayerHands(out var hands))
            return false;

        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity == item)
                return true;
        }

        return false;
    }

    private sealed class HandheldEntityPlacementHijack : PlacementHijack
    {
        private readonly EntityUid _item;
        private readonly EntProtoId _previewPrototype;
        private readonly bool _canRotate;
        private readonly Action<EntityUid, EntityCoordinates, Direction> _request;
        private readonly IPrototypeManager _prototype;
        private readonly SpriteSystem _sprite;

        public override bool CanRotate => _canRotate;

        public HandheldEntityPlacementHijack(
            EntityUid item,
            EntProtoId previewPrototype,
            bool canRotate,
            IPrototypeManager prototype,
            SpriteSystem sprite,
            Action<EntityUid, EntityCoordinates, Direction> request)
        {
            _item = item;
            _previewPrototype = previewPrototype;
            _canRotate = canRotate;
            _prototype = prototype;
            _sprite = sprite;
            _request = request;
        }

        public override bool HijackPlacementRequest(EntityCoordinates coordinates)
        {
            _request(_item, coordinates, Manager.Direction);
            return true;
        }

        public override void StartHijack(PlacementManager manager)
        {
            base.StartHijack(manager);

            if (!_prototype.TryIndex(_previewPrototype, out EntityPrototype? prototype))
                return;

            var textures = _sprite.GetPrototypeTextures(prototype, out var noRot).ToList();
            manager.PreparePlacementTexList(textures, noRot || !_canRotate, prototype);
        }
    }

    private readonly record struct PendingPlacementRequest(
        EntityUid Item,
        EntityCoordinates Coordinates,
        Direction Direction);
}
