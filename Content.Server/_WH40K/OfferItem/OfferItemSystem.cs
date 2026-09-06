using Content.Shared.ActionBlocker;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared._WH40K.OfferItem;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.OfferItem;

/// <summary>
/// Server-authoritative consent item transfer. Client requests are treated only as intent.
/// </summary>
public sealed partial class OfferItemSystem : EntitySystem
{
    private static readonly TimeSpan OfferLifetime = TimeSpan.FromSeconds(10);

    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeNetworkEvent<RequestOfferItemEvent>(OnOfferRequested);
        SubscribeNetworkEvent<AcceptOfferedItemEvent>(OnOfferAccepted);
        SubscribeLocalEvent<OfferingItemComponent, ComponentShutdown>(OnOffererShutdown);
        SubscribeLocalEvent<OfferedItemComponent, ComponentShutdown>(OnReceiverShutdown);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OfferingItemComponent>();
        while (query.MoveNext(out var source, out var offer))
        {
            if (offer.ExpiresAt <= _timing.CurTime || !IsValidOffer(source, offer))
                ClearOffer(source, offer);
        }
    }

    private void OnOfferRequested(RequestOfferItemEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } source ||
            !TryGetEntity(ev.Target, out var targetEntity) || targetEntity is not { } target ||
            !TryGetEntity(ev.Item, out var itemEntity) || itemEntity is not { } item ||
            source == target ||
            !TryComp<OfferingItemComponent>(source, out var offer) ||
            !TryComp<OfferedItemComponent>(target, out var offered) ||
            !CanCreateOffer(source, target, item))
        {
            return;
        }

        ClearOffer(source, offer);
        if (offered.Source is { } previousSource && TryComp<OfferingItemComponent>(previousSource, out var previousOffer))
            ClearOffer(previousSource, previousOffer);

        offer.Target = target;
        offer.Item = item;
        offer.ExpiresAt = _timing.CurTime + OfferLifetime;
        offered.Source = source;
        offered.Item = item;
        Dirty(source, offer);
        Dirty(target, offered);

        _popups.PopupEntity(Loc.GetString("offer-item-try-give", ("item", Identity.Entity(item, EntityManager)),
            ("target", Identity.Entity(target, EntityManager))), source, source);
        _popups.PopupEntity(Loc.GetString("offer-item-try-give-target", ("user", Identity.Entity(source, EntityManager)),
            ("item", Identity.Entity(item, EntityManager))), source, target);
    }

    private void OnOfferAccepted(AcceptOfferedItemEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } target ||
            !TryGetEntity(ev.Source, out var sourceEntity) || sourceEntity is not { } source ||
            !TryComp<OfferingItemComponent>(source, out var offer) ||
            !TryComp<OfferedItemComponent>(target, out var offered) ||
            offer.Target != target || offered.Source != source || offer.Item is not { } item || offered.Item != item ||
            offer.ExpiresAt <= _timing.CurTime || !CanCompleteOffer(source, target, item))
        {
            return;
        }

        if (!_hands.TryDrop(source, item) || !_hands.TryPickupAnyHand(target, item))
        {
            if (!_hands.IsHolding(source, item, out _))
                _hands.TryPickupAnyHand(source, item);
            ClearOffer(source, offer);
            return;
        }

        _popups.PopupEntity(Loc.GetString("offer-item-give", ("item", Identity.Entity(item, EntityManager)),
            ("target", Identity.Entity(target, EntityManager))), source, source);
        _popups.PopupEntity(Loc.GetString("offer-item-give-target", ("user", Identity.Entity(source, EntityManager)),
            ("item", Identity.Entity(item, EntityManager))), source, target);
        ClearOffer(source, offer);
    }

    private bool CanCreateOffer(EntityUid source, EntityUid target, EntityUid item)
    {
        return _hands.IsHolding(source, item, out _) &&
               _blocker.CanInteract(source, target) &&
               _interaction.InRangeUnobstructed(source, target, 2f);
    }

    private bool CanCompleteOffer(EntityUid source, EntityUid target, EntityUid item)
    {
        return _hands.IsHolding(source, item, out _) &&
               _hands.TryGetEmptyHand(target, out _) &&
               _blocker.CanInteract(source, target) &&
               _blocker.CanInteract(target, source) &&
               _interaction.InRangeUnobstructed(source, target, 2f);
    }

    private bool IsValidOffer(EntityUid source, OfferingItemComponent offer)
    {
        return offer.Target is { } target && offer.Item is { } item && Exists(target) && Exists(item) &&
               TryComp<OfferedItemComponent>(target, out var receiver) && receiver.Source == source && receiver.Item == item &&
               _hands.IsHolding(source, item, out _);
    }

    private void ClearOffer(EntityUid source, OfferingItemComponent offer)
    {
        var target = offer.Target;
        offer.Target = null;
        offer.Item = null;
        offer.ExpiresAt = TimeSpan.Zero;
        Dirty(source, offer);

        if (target is { } receiver && TryComp<OfferedItemComponent>(receiver, out var offered) && offered.Source == source)
        {
            offered.Source = null;
            offered.Item = null;
            Dirty(receiver, offered);
        }
    }

    private void OnOffererShutdown(Entity<OfferingItemComponent> ent, ref ComponentShutdown args)
    {
        ClearOffer(ent.Owner, ent.Comp);
    }

    private void OnReceiverShutdown(Entity<OfferedItemComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Source is { } source && TryComp<OfferingItemComponent>(source, out var offer))
            ClearOffer(source, offer);
    }
}
