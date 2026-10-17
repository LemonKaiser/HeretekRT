using Content.Server._NF.Bank;
using Content.Shared._NF.Bank.Events;
using Content.Server._WH40K.Localizations;
using Content.Shared._WH40K.Merchant;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.SSDIndicator;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;
using System.Linq;
using Content.Shared.Access.Systems;
using Content.Shared._WH40K.Npcs;
using Content.Server._WH40K.Quests;
using Content.Server._WH40K.Dialogue;
using Content.Server._WH40K.Dialogue.Components;
using Content.Server._WH40K.PersistentInventory;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Merchant;

public sealed partial class MerchantSystem : EntitySystem
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
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private Wh40kQuestInteractionSystem _quests = default!;
    [Dependency] private Wh40kQuestSubmissionSystem _submission = default!;
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;
    [Dependency] private DialogueSystem _dialogue = default!;
    [Dependency] private Content.Server._WH40K.Npcs.Wh40kNpcAnchorSystem _anchors = default!;
    [Dependency] private Content.Server._WH40K.Npcs.Wh40kNpcFacingSystem _facing = default!;
    public event Action? PurchasePrepared;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MerchantComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MerchantSpawnMarkerComponent, MapInitEvent>(OnSpawnMarkerMapInit);
        SubscribeLocalEvent<MerchantComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MerchantComponent, MerchantBuyMessage>(OnBuy);
        SubscribeLocalEvent<MerchantComponent, MerchantRequestRefreshMessage>(OnRequestRefresh);
        SubscribeLocalEvent<MerchantComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<MerchantComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<BalanceChangedEvent>(OnBalanceChanged);
        InitializeBuyback();
    }

    private void OnInteractHand(EntityUid uid, MerchantComponent comp, InteractHandEvent args)
    {
        if (args.Handled || !HasComp<ActorComponent>(args.User) || HasComp<DialogueInteractableComponent>(uid))
            return;

        args.Handled = OpenUi(args.User, uid, comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateSaleRecovery(frameTime);

        var query = EntityQueryEnumerator<MerchantComponent>();
        while (query.MoveNext(out var uid, out var merchant))
        {
            EnsureHomeCoordinates(uid, merchant);
            ReturnToHomeIfNeeded(uid, merchant);

            if (merchant.NextRefreshAt > _timing.CurTime)
                continue;

            Refresh(uid, merchant);
        }
    }

    public bool OpenUi(EntityUid user, EntityUid merchant, MerchantComponent? component = null)
    {
        if (!Resolve(merchant, ref component)
            || !CanTrade(user) || !_interaction.InRangeUnobstructed(user, merchant, Math.Clamp(component.InteractionRange, 0.1f, 3f)))
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
        _facing.Capture(uid);
        RemComp<SSDIndicatorComponent>(uid);
        EnsureHomeCoordinates(uid, component);
        Refresh(uid, component);
    }

    private void OnSpawnMarkerMapInit(EntityUid uid, MerchantSpawnMarkerComponent component, MapInitEvent args)
    {
        var coordinates = Transform(uid).Coordinates;
        if (TryComp(uid, out Wh40kNpcAnchorComponent? anchor))
        {
            if (!_anchors.TryGetAnchor(anchor.Anchor, out var marker) || marker != uid || _anchors.HasRegisteredBody(anchor.Anchor)) return;
            SpawnAttachedTo("MobWH40KFootfallMerchant", coordinates, rotation: Transform(uid).LocalRotation);
            return;
        }
        var mapCoordinates = _transform.ToMapCoordinates(coordinates);
        foreach (var (merchant, _) in _lookup.GetEntitiesInRange<MerchantComponent>(mapCoordinates, 1f))
        {
            if (merchant != uid)
                return;
        }

        SpawnAttachedTo("MobWH40KFootfallMerchant", coordinates, rotation: Transform(uid).LocalRotation);
    }

    private void OnStartup(EntityUid uid, MerchantComponent component, ComponentStartup args)
    {
        _facing.CaptureInitialized(uid);
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
        if (HasComp<Wh40kNpcProtectionComponent>(uid)) return;
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

    private void OnOpened(Entity<MerchantComponent> ent, ref BoundUIOpenedEvent ev)
    { if (ev.UiKey.Equals(MerchantUiKey.Key)) UpdateUi(ent, ent.Comp); }

    private void OnRequestRefresh(EntityUid uid, MerchantComponent component, MerchantRequestRefreshMessage args)
    {
        if (ValidViewer(uid, component, args.Actor)) UpdateUi(uid, component);
    }

    private void OnBalanceChanged(BalanceChangedEvent args)
    {
        if (args.Session.AttachedEntity is not { } buyer ||
            !TryComp<UserInterfaceUserComponent>(buyer, out var user))
            return;

        foreach (var (uid, keys) in user.OpenInterfaces.ToArray())
        {
            if (keys.Contains(MerchantUiKey.Key) && TryComp<MerchantComponent>(uid, out var component))
                UpdateUi(uid, component);
        }
    }

    public bool CanTrade(EntityUid buyer)
    {
        if (TerminatingOrDeleted(buyer) || !TryComp(buyer, out ActorComponent? actor) ||
            actor.PlayerSession.AttachedEntity != buyer || _sessions.IsStopping ||
            HasComp<PersistentInventoryOperationComponent>(buyer)) return false;
        var user = actor.PlayerSession.UserId;
        return !_quests.IsBusy(user) && !_submission.IsBusy(user) && !IsSelling(user) && !_dialogue.IsInDialogue(actor.PlayerSession);
    }

    private bool ValidViewer(EntityUid uid, MerchantComponent component, EntityUid buyer) =>
        !TerminatingOrDeleted(buyer) && TryComp(buyer, out ActorComponent? actor) && actor.PlayerSession.AttachedEntity == buyer &&
        _ui.GetActors(uid, MerchantUiKey.Key).Contains(buyer) && _interaction.InRangeUnobstructed(buyer, uid,
            Math.Clamp(component.InteractionRange, 0.1f, 3f), popup: false);

    private bool Allowed(EntityUid buyer, MerchantOfferPrototype offer) => offer.RequiredAccess == null ||
        _access.FindAccessTags(buyer).Contains(offer.RequiredAccess.Value);

    private void Result(EntityUid uid, EntityUid buyer, Guid operation, string result)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(buyer)) return;
        _ui.ServerSendUiMessage(uid, MerchantUiKey.Key, new MerchantResultMessage(operation, result), buyer);
        _popup.PopupEntity(_playerCulture.GetPlayerString(buyer, result), uid, buyer);
    }

    private void OnBuy(EntityUid uid, MerchantComponent component, MerchantBuyMessage args)
    {
        var buyer = args.Actor;
        if (!ValidViewer(uid, component, buyer))
        {
            return;
        }

        if (component.NextRefreshAt <= _timing.CurTime) Refresh(uid, component);
        if (args.Operation == Guid.Empty || args.CatalogId != component.CatalogId)
        {
            Result(uid, buyer, args.Operation, "wh40k-service-stale");
            UpdateUi(uid, component);
            return;
        }
        var key = (Comp<ActorComponent>(buyer).PlayerSession.UserId.UserId, args.Operation);
        if (component.Receipts.TryGetValue(key, out var prior))
        {
            Result(uid, buyer, args.Operation, prior.Product == args.Product ? prior.Result : "wh40k-service-stale");
            UpdateUi(uid, component);
            return;
        }
        if (!CanTrade(buyer) || component.Receipts.Count >= 2048)
        { Result(uid, buyer, args.Operation, "wh40k-service-busy"); return; }
        var stock = component.CurrentStock.FirstOrDefault(entry => entry.Offer.Product.Id == args.Product);
        var outcome = "wh40k-merchant-out-of-stock";
        if (stock != null && Allowed(buyer, stock.Offer) && stock.Remaining > 0)
        {
            EntityUid? product = null;
            var paid = false;
            try
            {
                // Prepare before charging. Delivery to a hand is optional; the buyer owns the product on the floor too.
                product = Spawn(stock.Offer.Product, Transform(buyer).Coordinates);
                PurchasePrepared?.Invoke();
                if (!CanTrade(buyer) || !ValidViewer(uid, component, buyer) || TerminatingOrDeleted(product.Value))
                    outcome = "wh40k-service-failed";
                else if (stock.Offer.Price <= 0 || !_bank.TryGetBalance(buyer, out var balance))
                    outcome = "wh40k-service-failed";
                else if (balance < stock.Offer.Price)
                    outcome = "wh40k-merchant-insufficient-funds";
                else if (!_bank.TryBankWithdraw(buyer, stock.Offer.Price))
                    outcome = "wh40k-service-failed";
                else
                {
                    paid = true;
                    stock.Remaining--;
                    outcome = "wh40k-service-bought";
                    _hands.TryPickupAnyHand(buyer, product.Value, checkActionBlocker: false);
                }
            }
            catch (Exception error)
            {
                Log.Error($"Merchant purchase failed: {error}");
                // A delivery placement error after payment leaves the paid item at the buyer's feet.
                outcome = paid ? "wh40k-service-bought" : "wh40k-service-failed";
            }
            finally { if (!paid && product != null && Exists(product.Value)) QueueDel(product.Value); }
        }
        else if (stock != null && !Allowed(buyer, stock.Offer)) outcome = "wh40k-service-permission";
        component.Receipts.Add(key, (args.Product, outcome));
        Result(uid, buyer, args.Operation, outcome);
        UpdateUi(uid, component);
    }

    private void Refresh(EntityUid uid, MerchantComponent component)
    {
        component.CurrentStock.Clear();
        component.CatalogId = Guid.NewGuid();
        component.Receipts.Clear();

        var candidates = component.Offers
            .Take(64)
            .Where(offer => offer.Weight is > 0 and <= 10000
                           && offer.Price is > 0 and <= 1000000
                           && offer.MinStock is > 0 and <= 100
                           && offer.MaxStock >= offer.MinStock
                           && offer.MaxStock <= 100
                           && _prototypes.TryIndex<EntityPrototype>(offer.Product, out var product)
                           && !product.Abstract && product.Components.ContainsKey("Item") &&
                           !product.Components.ContainsKey("Wh40kNpcProtection") && !product.Components.ContainsKey("Wh40kQuestItem"))
            .DistinctBy(offer => offer.Product)
            .ToList();

        var wanted = Math.Clamp(component.DisplayCount, 0, Math.Min(32, candidates.Count));
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
        component.NextRefreshAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(component.RefreshInterval.TotalSeconds, 30d, 86400d));
        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, MerchantComponent component)
    {
        if (component.NextRefreshAt <= _timing.CurTime)
        {
            Refresh(uid, component);
            return;
        }

        foreach (var actor in _ui.GetActors(uid, MerchantUiKey.Key).ToArray())
        {
            _bank.TryGetBalance(actor, out var balance);
            var state = new MerchantUpdateState(
                component.Title.Id,
                component.Greeting.Id,
                (float)Math.Max(0d, (component.NextRefreshAt - _timing.CurTime).TotalSeconds),
                component.RefreshNumber,
                component.CurrentStock.Where(entry => Allowed(actor, entry.Offer)).Select(entry => new MerchantOfferState(
                    entry.Offer.Product.Id,
                    entry.Offer.Price,
                    entry.Remaining,
                    entry.InitialStock,
                    entry.Offer.DisplayName?.Id,
                    entry.Offer.Description?.Id)).ToList(), component.CatalogId, component.Buyback != null, balance);
            _ui.ServerSendUiMessage(uid, MerchantUiKey.Key, state, actor);
        }
    }
}
