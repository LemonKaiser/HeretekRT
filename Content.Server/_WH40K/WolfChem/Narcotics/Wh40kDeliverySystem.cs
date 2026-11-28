using Content.Server.Body.Systems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Stack;
using Content.Shared._WH40K.WolfChem.Narcotics.Delivery;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Temperature;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// Snorting and bongs: baggies lay powder lines on tables, a line goes up the nose after a short do-after, and a lit
/// bong takes one hit at a time from its bowl.
/// </summary>
public sealed partial class Wh40kDeliverySystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private FoodSystem _food = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private Wh40kBaggieSystem _baggie = default!;
    [Dependency] private Wh40kSecondhandSmokeSystem _secondhand = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kBaggieComponent, AfterInteractEvent>(OnBaggieAfterInteract);
        SubscribeLocalEvent<Wh40kPowderLineComponent, ActivateInWorldEvent>(OnLineActivate);
        SubscribeLocalEvent<Wh40kPowderLineComponent, Wh40kSnortLineDoAfterEvent>(OnSnortDoAfter);

        SubscribeLocalEvent<Wh40kBongComponent, ComponentInit>(OnBongInit);
        SubscribeLocalEvent<Wh40kBongComponent, ComponentRemove>(OnBongRemove);
        SubscribeLocalEvent<Wh40kBongComponent, InteractUsingEvent>(OnBongInteractUsing);
        SubscribeLocalEvent<Wh40kBongComponent, UseInHandEvent>(OnBongUseInHand);
        SubscribeLocalEvent<Wh40kBongComponent, Wh40kBongHitDoAfterEvent>(OnBongDoAfter);
        SubscribeLocalEvent<Wh40kBongComponent, EntInsertedIntoContainerMessage>(OnBowlLoaded);
        SubscribeLocalEvent<Wh40kBongComponent, EntRemovedFromContainerMessage>(OnBowlEmptied);
        SubscribeLocalEvent<Wh40kBongComponent, ExaminedEvent>(OnBongExamined);
    }

    private void OnBaggieAfterInteract(Entity<Wh40kBaggieComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (TryComp<StorageComponent>(ent, out var storage)
            && HasComp<ItemComponent>(target)
            && !_container.IsEntityInContainer(target)
            && _storage.CanInsert(ent, target, out _, storage))
        {
            args.Handled = true;
            if (_storage.PlayerInsertEntityInWorld((ent, storage), args.User, target))
                _popup.PopupEntity(Loc.GetString("wh40k-baggie-solid-in", ("item", target)), ent, args.User);
            return;
        }

        if (!HasComp<PlaceableSurfaceComponent>(target))
            return;

        if (!HasPowder(ent) && _baggie.TryTakeSolid(ent, out var solid))
        {
            _transform.SetCoordinates(solid, args.ClickLocation);
            _popup.PopupEntity(Loc.GetString("wh40k-baggie-solid-tipped", ("item", solid)), solid, args.User);
            args.Handled = true;
            return;
        }

        args.Handled = TryPlaceLine(ent, args.User, args.ClickLocation) != null;
    }

    private bool HasPowder(Entity<Wh40kBaggieComponent> baggie)
    {
        return _solutions.TryGetSolution(baggie.Owner, baggie.Comp.Solution, out _, out var solution)
               && solution.Volume > FixedPoint2.Zero;
    }

    public EntityUid? TryPlaceLine(Entity<Wh40kBaggieComponent> baggie, EntityUid user, EntityCoordinates coords)
    {
        if (!_solutions.TryGetSolution(baggie.Owner, baggie.Comp.Solution, out var soln, out var solution)
            || solution.Volume <= FixedPoint2.Zero)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-baggie-empty"), baggie, user);
            return null;
        }

        var portion = _solutions.SplitSolution(soln.Value, FixedPoint2.Min(baggie.Comp.LineAmount, solution.Volume));
        var line = SpawnAttachedTo(baggie.Comp.Line, coords);
        if (!TryComp<Wh40kPowderLineComponent>(line, out var lineComp)
            || !_solutions.TryGetSolution(line, lineComp.Solution, out var lineSoln, out _))
        {
            Del(line);
            return null;
        }

        _solutions.TryAddSolution(lineSoln.Value, portion);
        _popup.PopupEntity(Loc.GetString("wh40k-baggie-line-placed"), line, user);
        return line;
    }

    private void OnLineActivate(Entity<Wh40kPowderLineComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || _food.IsMouthBlocked(args.User, args.User))
            return;

        args.Handled = true;
        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new Wh40kSnortLineDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("wh40k-line-snort-start"), args.User, args.User);
    }

    private void OnSnortDoAfter(Entity<Wh40kPowderLineComponent> ent, ref Wh40kSnortLineDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = Snort(ent, args.User);
    }

    public bool Snort(Entity<Wh40kPowderLineComponent> line, EntityUid user)
    {
        if (!_solutions.TryGetSolution(line.Owner, line.Comp.Solution, out var soln, out _))
            return false;

        var dose = _solutions.SplitSolution(soln.Value, soln.Value.Comp.Solution.Volume);
        _bloodstream.TryAddToChemicals(user, dose);
        _popup.PopupEntity(Loc.GetString("wh40k-line-snort-done"), user, user);
        QueueDel(line);
        return true;
    }

    private void OnBongInit(Entity<Wh40kBongComponent> ent, ref ComponentInit args)
    {
        _slots.AddItemSlot(ent, Wh40kBongComponent.BowlSlotId, ent.Comp.Bowl);
        UpdateBong(ent);
    }

    private void OnBongRemove(Entity<Wh40kBongComponent> ent, ref ComponentRemove args)
    {
        _slots.RemoveItemSlot(ent, ent.Comp.Bowl);
    }

    private void SetLit(Entity<Wh40kBongComponent> ent, bool lit)
    {
        ent.Comp.Lit = lit;
        UpdateBong(ent);
    }

    private void UpdateBong(Entity<Wh40kBongComponent> ent)
    {
        _appearance.SetData(ent, Wh40kBongVisuals.Loaded, ent.Comp.Bowl.Item != null);
        _appearance.SetData(ent, Wh40kBongVisuals.Lit, ent.Comp.Lit);
    }

    private void OnBongExamined(Entity<Wh40kBongComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Lit)
            args.PushMarkup(Loc.GetString("wh40k-bong-examine-lit"));
    }

    private void OnBowlLoaded(Entity<Wh40kBongComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != Wh40kBongComponent.BowlSlotId)
            return;

        UpdateBong(ent);
        if (!TryComp<StackComponent>(args.Entity, out var stack) || stack.Count <= 1)
            return;

        if (_stack.Split(args.Entity, stack.Count - 1, Transform(ent).Coordinates, stack) is not { } rest)
            return;

        var holder = _container.TryGetContainingContainer((ent.Owner, null, null), out var held) ? held.Owner : (EntityUid?) null;
        _hands.PickupOrDrop(holder, rest);
    }

    private void OnBowlEmptied(Entity<Wh40kBongComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == Wh40kBongComponent.BowlSlotId)
            SetLit(ent, false);
    }

    private void OnBongInteractUsing(Entity<Wh40kBongComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var hot = new IsHotEvent();
        RaiseLocalEvent(args.Used, hot);
        if (!hot.IsHot)
            return;

        args.Handled = true;
        TryLight(ent, args.User);
    }

    public bool TryLight(Entity<Wh40kBongComponent> ent, EntityUid? user)
    {
        if (ent.Comp.Bowl.Item == null)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wh40k-bong-bowl-empty"), ent, user.Value);
            return false;
        }

        SetLit(ent, true);
        if (user != null)
            _popup.PopupEntity(Loc.GetString("wh40k-bong-lit"), ent, user.Value);
        return true;
    }

    private void OnBongUseInHand(Entity<Wh40kBongComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!CanHit(ent, args.User))
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new Wh40kBongHitDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private bool CanHit(Entity<Wh40kBongComponent> ent, EntityUid user)
    {
        if (!ent.Comp.Lit || ent.Comp.Bowl.Item == null)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-bong-not-lit"), ent, user);
            return false;
        }

        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Water, out _, out var water)
            || water.GetTotalPrototypeQuantity("Water") < ent.Comp.MinWater)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-bong-no-water"), ent, user);
            return false;
        }

        return !_food.IsMouthBlocked(user, user);
    }

    private void OnBongDoAfter(Entity<Wh40kBongComponent> ent, ref Wh40kBongHitDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = Hit(ent, args.User);
    }

    public bool Hit(Entity<Wh40kBongComponent> ent, EntityUid user)
    {
        if (!CanHit(ent, user) || ent.Comp.Bowl.Item is not { } bowl)
            return false;

        var hit = new Solution();
        var wanted = ent.Comp.HitAmount;
        foreach (var (_, soln) in _solutions.EnumerateSolutions(bowl))
        {
            if (wanted <= FixedPoint2.Zero)
                break;

            var taken = _solutions.SplitSolution(soln, FixedPoint2.Min(wanted, soln.Comp.Solution.Volume));
            wanted -= taken.Volume;
            hit.AddSolution(taken, null);
        }

        if (!BowlHasContents(bowl))
        {
            SetLit(ent, false);
            QueueDel(bowl);
        }

        if (hit.Volume <= FixedPoint2.Zero)
            return false;

        _secondhand.Exhale(ent, hit);
        _reactive.DoEntityReaction(user, hit, ReactionMethod.Ingestion);
        _bloodstream.TryAddToChemicals(user, hit);
        _popup.PopupEntity(Loc.GetString("wh40k-bong-hit"), user, user);
        Puff(ent);
        return true;
    }

    private bool BowlHasContents(EntityUid bowl)
    {
        foreach (var (_, soln) in _solutions.EnumerateSolutions(bowl))
        {
            if (soln.Comp.Solution.Volume > FixedPoint2.Zero)
                return true;
        }

        return false;
    }

    private void Puff(Entity<Wh40kBongComponent> ent)
    {
        if (Transform(ent).GridUid is not { } grid)
            return;

        var coords = _map.MapToGrid(grid, _transform.GetMapCoordinates(ent)).SnapToGrid(EntityManager);
        var cloud = SpawnAttachedTo(ent.Comp.Puff, coords);
        if (!TryComp<SmokeComponent>(cloud, out var smoke))
        {
            Del(cloud);
            return;
        }

        var air = new Solution();
        air.AddReagent("Water", FixedPoint2.New(0.5));
        _smoke.StartSmoke(cloud, air, 2f, 0, smoke);
    }
}
