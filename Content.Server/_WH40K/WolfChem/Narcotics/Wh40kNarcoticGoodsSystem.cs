using Content.Server.Body.Components;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Cargo.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.Tag;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// Any item or movable structure holding a priced Narcotics reagent gets marked with the Wh40kNarcotic tag and
/// Wh40kNarcoticMarks contraband/tax/stock components, and loses them once emptied.
/// </summary>
public sealed partial class Wh40kNarcoticGoodsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TagSystem _tag = default!;

    public static readonly EntProtoId Marks = "Wh40kNarcoticMarks";
    public static readonly ProtoId<TagPrototype> Tag = "Wh40kNarcotic";
    public const string Group = "Narcotics";

    private ComponentRegistry? _marks;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ItemComponent, SolutionContainerChangedEvent>((Entity<ItemComponent> ent, ref SolutionContainerChangedEvent args) => OnChanged(ent, args.Solution));
        SubscribeLocalEvent<AnchorableComponent, SolutionContainerChangedEvent>((Entity<AnchorableComponent> ent, ref SolutionContainerChangedEvent args) => OnChanged(ent, args.Solution));
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _marks = null);
    }

    private void OnChanged(EntityUid ent, Solution changed)
    {
        if (HasComp<BloodstreamComponent>(ent))
            return;

        if (HoldsNarcotics(changed))
        {
            SetNarcotic(ent, true);
            return;
        }

        if (!HasComp<Wh40kNarcoticMarkedComponent>(ent))
            return;

        foreach (var (_, solution) in _solutions.EnumerateSolutions(ent))
        {
            if (HoldsNarcotics(solution.Comp.Solution))
                return;
        }

        SetNarcotic(ent, false);
    }

    public bool HoldsNarcotics(Solution solution)
    {
        foreach (var reagent in solution.Contents)
        {
            if (reagent.Quantity > FixedPoint2.Zero
                && _prototype.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var proto)
                && proto.Group == Group
                && proto.PricePerUnit > 0f)
                return true;
        }

        return false;
    }

    public double StreetValue(Solution solution)
    {
        var value = 0.0;
        foreach (var reagent in solution.Contents)
        {
            if (reagent.Quantity > FixedPoint2.Zero
                && _prototype.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var proto)
                && proto.Group == Group
                && proto.PricePerUnit > 0f)
                value += reagent.Quantity.Double() * proto.PricePerUnit * solution.GetPriceFactor(reagent.Reagent.Prototype);
        }

        return value;
    }

    public double StreetValue(EntityUid uid)
    {
        var value = 0.0;
        foreach (var (_, solution) in _solutions.EnumerateSolutions(uid, includeSelf: false))
        {
            value += StreetValue(solution.Comp.Solution);
        }

        if (value <= 0 && _tag.HasTag(uid, Tag) && TryComp<StaticPriceComponent>(uid, out var price))
            value = price.Price;

        return value;
    }

    public int TurnIn(EntityUid uid, int reward, ref double pool, HashSet<EntityUid> taken)
    {
        var rate = _cfg.GetCVar(Wh40kNarcoticsCVars.TurnInValue);
        if (rate <= 0 || !_tag.HasTag(uid, Tag))
            return reward;

        var value = StreetValue(uid);
        if (value <= 0)
            return reward;

        var before = (int) (pool / rate);
        pool += value;
        taken.Add(uid);
        return reward + (int) (pool / rate) - before;
    }

    public void SetNarcotic(EntityUid uid, bool narcotic)
    {
        if (TerminatingOrDeleted(uid) || GetMarks() is not { } marks)
            return;

        if (narcotic)
        {
            if (_tag.HasTag(uid, Tag))
                return;

            var marked = EnsureComp<Wh40kNarcoticMarkedComponent>(uid);
            var added = new ComponentRegistry();
            foreach (var (name, entry) in marks)
            {
                if (HasComp(uid, entry.Component.GetType()))
                    continue;

                added.Add(name, entry);
                marked.Added.Add(name);
            }

            EntityManager.AddComponents(uid, added, removeExisting: false);
            _tag.AddTag(uid, Tag);
        }
        else if (TryComp<Wh40kNarcoticMarkedComponent>(uid, out var marked))
        {
            var added = new ComponentRegistry();
            foreach (var name in marked.Added)
            {
                if (marks.TryGetValue(name, out var entry))
                    added.Add(name, entry);
            }

            EntityManager.RemoveComponents(uid, added);
            _tag.RemoveTag(uid, Tag);
            RemComp<Wh40kNarcoticMarkedComponent>(uid);
        }
    }

    private ComponentRegistry? GetMarks()
    {
        if (_marks != null)
            return _marks;

        if (!_prototype.TryIndex<EntityPrototype>(Marks, out var proto))
            return null;

        _marks = new ComponentRegistry();
        foreach (var (name, entry) in proto.Components)
        {
            if (name != "Transform" && name != "MetaData")
                _marks.Add(name, entry);
        }

        return _marks;
    }
}
