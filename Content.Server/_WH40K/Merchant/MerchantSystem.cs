using Content.Server._NF.Bank;
using Content.Server._WH40K.Localizations;
using Content.Shared._WH40K.Merchant;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.SSDIndicator;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;
using System.Linq;

namespace Content.Server._WH40K.Merchant;

public sealed class MerchantSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WH40KPlayerCultureManager _playerCulture = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MerchantComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MerchantSpawnMarkerComponent, MapInitEvent>(OnSpawnMarkerMapInit);
        SubscribeLocalEvent<MerchantComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MerchantComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<MerchantComponent, MerchantBuyMessage>(OnBuy);
        SubscribeLocalEvent<MerchantComponent, MerchantRequestRefreshMessage>(OnRequestRefresh);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MerchantComponent>();
        while (query.MoveNext(out var uid, out var merchant))
        {
            EnsureHomeCoordinates(uid, merchant);
            ReturnToHomeIfNeeded(uid, merchant);
            FaceNearestPlayer(uid, merchant);

            if (merchant.NextRefreshAt > _timing.CurTime)
                continue;

            Refresh(uid, merchant);
        }
    }

    public bool OpenUi(EntityUid user, EntityUid merchant, MerchantComponent? component = null)
    {
        if (!Resolve(merchant, ref component)
            || !HasComp<ActorComponent>(user))
        {
            return false;
        }

        if (!_ui.GetActors(merchant, MerchantUiKey.Key).Contains(user)
            && !_ui.TryOpenUi(merchant, MerchantUiKey.Key, user))
            return false;

        UpdateUi(merchant, component);
        return true;
    }

    private void OnMapInit(EntityUid uid, MerchantComponent component, MapInitEvent args)
    {
        RemComp<SSDIndicatorComponent>(uid);
        EnsureHomeCoordinates(uid, component);
        Refresh(uid, component);
    }

    private void OnSpawnMarkerMapInit(EntityUid uid, MerchantSpawnMarkerComponent component, MapInitEvent args)
    {
        var coordinates = Transform(uid).Coordinates;
        var mapCoordinates = _transform.ToMapCoordinates(coordinates);
        foreach (var (merchant, _) in _lookup.GetEntitiesInRange<MerchantComponent>(mapCoordinates, 1f))
        {
            if (merchant != uid)
                return;
        }

        Spawn("MobWH40KFootfallMerchant", coordinates);
    }

    private void OnStartup(EntityUid uid, MerchantComponent component, ComponentStartup args)
    {
        RemComp<SSDIndicatorComponent>(uid);
        EnsureHomeCoordinates(uid, component);

        if (MetaData(uid).EntityLifeStage == EntityLifeStage.MapInitialized
            && component.RefreshNumber == 0)
        {
            Refresh(uid, component);
        }
    }

    private void EnsureHomeCoordinates(EntityUid uid, MerchantComponent component)
    {
        if (component.HomeCoordinates is not null || !TryComp(uid, out TransformComponent? merchantTransform))
            return;

        var merchantMapCoordinates = _transform.GetMapCoordinates(uid, merchantTransform);
        var nearestDistance = float.MaxValue;
        EntityCoordinates? nearestCoordinates = null;
        var markers = EntityQueryEnumerator<MerchantSpawnMarkerComponent, TransformComponent>();
        while (markers.MoveNext(out _, out _, out var markerTransform))
        {
            var markerCoordinates = _transform.GetMapCoordinates(markerTransform);
            if (markerCoordinates.MapId != merchantMapCoordinates.MapId)
                continue;

            var distance = Vector2.DistanceSquared(markerCoordinates.Position, merchantMapCoordinates.Position);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearestCoordinates = markerTransform.Coordinates;
        }

        component.HomeCoordinates = nearestCoordinates ?? merchantTransform.Coordinates;
    }

    private void ReturnToHomeIfNeeded(EntityUid uid, MerchantComponent component)
    {
        if (component.HomeCoordinates is not { } home || component.ReturnDistance <= 0f ||
            !TryComp(uid, out TransformComponent? merchantTransform))
        {
            return;
        }

        var current = _transform.GetMapCoordinates(uid, merchantTransform);
        var target = _transform.ToMapCoordinates(home);
        if (current.MapId != target.MapId ||
            Vector2.DistanceSquared(current.Position, target.Position) <= component.ReturnDistance * component.ReturnDistance)
        {
            return;
        }

        _transform.SetCoordinates(uid, merchantTransform, home);
    }

    private void FaceNearestPlayer(EntityUid uid, MerchantComponent component)
    {
        if (component.LookAtRange <= 0f || !TryComp(uid, out TransformComponent? merchantTransform))
            return;

        var merchantCoordinates = _transform.GetMapCoordinates(uid, merchantTransform);
        var merchantPosition = merchantCoordinates.Position;
        var rangeSquared = component.LookAtRange * component.LookAtRange;
        EntityUid? nearest = null;
        Vector2 nearestPosition = default;
        var nearestDistance = float.MaxValue;

        foreach (var (actor, _) in _lookup.GetEntitiesInRange<ActorComponent>(merchantCoordinates, component.LookAtRange))
        {
            if (actor == uid || !TryComp(actor, out TransformComponent? actorTransform))
                continue;

            var actorPosition = _transform.GetMapCoordinates(actor, actorTransform).Position;
            var distance = (actorPosition - merchantPosition).LengthSquared();
            if (distance > rangeSquared || distance >= nearestDistance)
                continue;

            nearest = actor;
            nearestPosition = actorPosition;
            nearestDistance = distance;
        }

        if (nearest != null)
            _rotate.TryFaceCoordinates(uid, nearestPosition, merchantTransform);
    }

    private void OnBeforeUiOpen(EntityUid uid, MerchantComponent component, BeforeActivatableUIOpenEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnRequestRefresh(EntityUid uid, MerchantComponent component, MerchantRequestRefreshMessage args)
    {
        UpdateUi(uid, component);
    }

    private void OnBuy(EntityUid uid, MerchantComponent component, MerchantBuyMessage args)
    {
        var buyer = args.Actor;
        if (TerminatingOrDeleted(buyer)
            || !_ui.GetActors(uid, MerchantUiKey.Key).Contains(buyer)
            || !_interaction.InRangeUnobstructed(
                buyer,
                uid,
                Math.Max(0.1f, component.InteractionRange),
                popup: false))
        {
            return;
        }

        var stock = component.CurrentStock.FirstOrDefault(
            entry => entry.Offer.Product.Id == args.Product && entry.Remaining > 0);
        if (stock == null || stock.Remaining <= 0)
        {
            _popup.PopupEntity(_playerCulture.GetPlayerString(buyer, "wh40k-merchant-out-of-stock"), uid, buyer);
            UpdateUi(uid, component);
            return;
        }

        var offer = stock.Offer;
        if (offer.Price <= 0 || !_bank.TryBankWithdraw(buyer, offer.Price))
        {
            _popup.PopupEntity(_playerCulture.GetPlayerString(buyer, "wh40k-merchant-insufficient-funds"), uid, buyer);
            return;
        }

        var product = Spawn(offer.Product, Transform(buyer).Coordinates);
        _hands.TryPickupAnyHand(buyer, product, checkActionBlocker: false);
        stock.Remaining--;

        _popup.PopupEntity(
            _playerCulture.GetPlayerString(buyer, "wh40k-merchant-purchase-complete", ("price", offer.Price)),
            uid,
            buyer);
        UpdateUi(uid, component);
    }

    private void Refresh(EntityUid uid, MerchantComponent component)
    {
        component.CurrentStock.Clear();

        var candidates = component.Offers
            .Where(offer => offer.Weight > 0
                           && offer.Price > 0
                           && offer.MinStock > 0
                           && offer.MaxStock >= offer.MinStock
                           && offer.MaxStock < int.MaxValue
                           && _prototypes.TryIndex<EntityPrototype>(offer.Product, out var product)
                           && !product.Abstract)
            .DistinctBy(offer => offer.Product)
            .ToList();

        var wanted = Math.Clamp(component.DisplayCount, 0, candidates.Count);
        while (component.CurrentStock.Count < wanted && candidates.Count > 0)
        {
            var totalWeight = candidates.Sum(offer => offer.Weight);
            var roll = _random.Next(1, totalWeight + 1);
            MerchantOfferPrototype? selected = null;

            foreach (var candidate in candidates)
            {
                roll -= candidate.Weight;
                if (roll <= 0)
                {
                    selected = candidate;
                    break;
                }
            }

            if (selected == null)
                break;

            candidates.Remove(selected);
            var amount = _random.Next(selected.MinStock, selected.MaxStock + 1);
            component.CurrentStock.Add(new MerchantStockEntry(selected, amount));
        }

        component.RefreshNumber++;
        component.NextRefreshAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1d, component.RefreshInterval.TotalSeconds));
        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, MerchantComponent component)
    {
        if (component.NextRefreshAt <= _timing.CurTime)
        {
            Refresh(uid, component);
            return;
        }

        var state = new MerchantUpdateState(
            component.Title.Id,
            component.Greeting.Id,
            (float)Math.Max(0d, (component.NextRefreshAt - _timing.CurTime).TotalSeconds),
            component.RefreshNumber,
            component.CurrentStock.Select(entry => new MerchantOfferState(
                entry.Offer.Product.Id,
                entry.Offer.Price,
                entry.Remaining,
                entry.InitialStock,
                entry.Offer.DisplayName?.Id,
                entry.Offer.Description?.Id)).ToList());

        _ui.SetUiState(uid, MerchantUiKey.Key, state);
    }
}
