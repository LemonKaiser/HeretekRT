using System.Linq;
using Content.Server.Construction.Components;
using Content.Server.Cargo.Systems;
using Content.Shared._NF.Trade;
using Content.Shared._WH40K.Merchant;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Lathe;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components.SolutionManager;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Economy;

public sealed partial class MaterialMarketSystem
{
    [Dependency] private PricingSystem _legacyPricing = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    private readonly Dictionary<string, Dictionary<string, double>?> _composition = new();
    private readonly Dictionary<string, List<LatheRecipePrototype>> _latheRecipes = new();
    private readonly Dictionary<string, double> _specialGoods = new();

    private void EnsureTables()
    {
        if (_tablesReady)
            return;
        LoadState();
        _tablesReady = true;
        _prices.Clear();
        _composition.Clear();
        _latheRecipes.Clear();
        _specialGoods.Clear();
        _retailValues.Clear();
        foreach (var material in _prototypes.EnumeratePrototypes<MaterialPrototype>())
        {
            var price = Profile.Prices.GetValueOrDefault(material.ID, material.Price);
            _prices[material.ID] = double.IsFinite(price) && price > 0 ? price : 0;
        }
        foreach (var recipe in _prototypes.EnumeratePrototypes<LatheRecipePrototype>())
        {
            if (recipe.Abstract || recipe.Result is not { } result || recipe.ResultCount <= 0)
                continue;
            if (!_latheRecipes.TryGetValue(result.Id, out var recipes))
                _latheRecipes[result.Id] = recipes = new();
            recipes.Add(recipe);
        }
        foreach (var valuation in _prototypes.EnumeratePrototypes<MerchantValuationPrototype>())
            foreach (var (product, price) in valuation.Prices)
                _specialGoods[product.Id] = Math.Max(_specialGoods.GetValueOrDefault(product.Id), price);
    }

    public bool TryGetOwnMaterials(EntityUid entity, out Dictionary<string, double> materials)
    {
        materials = new();
        EnsureTables();
        // Liquids have no approved material conversion here; keep a filled vessel intact instead of discarding its contents.
        if (!HasComp<MaterialComponent>(entity) && TryComp<SolutionContainerManagerComponent>(entity, out var solutions) &&
            _solutions.EnumerateSolutions((entity, solutions)).Any(pair => pair.Solution.Comp.Solution.Volume > 0))
            return false;
        var prototype = MetaData(entity).EntityPrototype;
        var count = TryComp<StackComponent>(entity, out var stack) ? stack.Count : 1;
        if (prototype != null && TryGetPrototypeMaterials(prototype, new HashSet<string>(), out var unit))
            Add(materials, unit, count);
        if (TryComp<MaterialStorageComponent>(entity, out var storage))
            foreach (var (id, volume) in storage.Storage)
                Add(materials, id.Id, volume);
        return materials.Count > 0;
    }

    public void PreserveUnsoldChildren(MaterialMarketQuote quote)
    {
        foreach (var entity in quote.Seen)
        {
            if (quote.Sold.Contains(entity) || !Exists(entity) ||
                !_containers.TryGetContainingContainer((entity, null, null), out var container) ||
                !quote.Sold.Contains(container.Owner))
                continue;
            var coordinates = Transform(container.Owner).Coordinates;
            _containers.Remove(entity, container, reparent: false, force: true);
            _transform.SetCoordinates(entity, coordinates);
            _transform.AttachToGridOrMap(entity);
        }
    }

    private bool TryGetPrototypeMaterials(EntityPrototype prototype, HashSet<string> path, out Dictionary<string, double> result)
    {
        EnsureTables();
        if (_composition.TryGetValue(prototype.ID, out var cached))
        {
            result = cached ?? new();
            return cached != null;
        }
        result = new();
        if (!path.Add(prototype.ID))
            return false;

        Dictionary<string, double>? best = null;
        if (prototype.TryComp<PhysicalCompositionComponent>(out var physical, _factory))
        {
            var candidate = new Dictionary<string, double>();
            foreach (var (id, amount) in physical.MaterialComposition)
                Add(candidate, id, amount);
            if (candidate.Count > 0)
                best = candidate;
        }

        // Raw stacks must have the same value regardless of the entity used to spawn or merge them.
        if (prototype.TryComp<MaterialComponent>(out _, _factory))
        {
            path.Remove(prototype.ID);
            _composition[prototype.ID] = best;
            result = best ?? new();
            return best != null;
        }

        var spawnCount = prototype.TryComp<StackComponent>(out var stack, _factory) ? Math.Max(1, stack.Count) : 1;
        if (prototype.TryComp<ConstructionComponent>(out var construction, _factory) &&
            _prototypes.TryIndex<ConstructionGraphPrototype>(construction.Graph, out var graph) &&
            graph.Start is { } start && graph.Nodes.ContainsKey(construction.Node) &&
            graph.TryPath(start, construction.Node, out var nodes))
        {
            var candidate = new Dictionary<string, double>();
            var previous = graph.Nodes[start];
            foreach (var node in nodes)
            {
                var edge = previous.GetEdge(node.Name);
                previous = node;
                if (edge == null)
                    continue;
                foreach (var step in edge.Steps)
                {
                    // Stored parts remain real children and are appraised independently.
                    if (step is not MaterialConstructionGraphStep material || !string.IsNullOrEmpty(material.Store) ||
                        !_prototypes.TryIndex<StackPrototype>(material.MaterialPrototypeId, out var source) ||
                        !_prototypes.TryIndex(source.Spawn, out var ingredient) ||
                        !TryGetPrototypeMaterials(ingredient, path, out var composition))
                        continue;
                    Add(candidate, composition, (double) material.Amount / spawnCount);
                }
            }
            Choose(candidate, ref best);
        }

        if (_latheRecipes.TryGetValue(prototype.ID, out var recipes))
        {
            foreach (var recipe in recipes)
            {
                var candidate = new Dictionary<string, double>();
                foreach (var (id, amount) in recipe.Materials)
                {
                    var consumed = Math.Max(0, SharedLatheSystem.AdjustMaterial(amount, recipe.MaterialDiscountScale,
                        (float) Math.Min(Profile.LatheMaterialRate, _observedLatheMultiplier)) - recipe.MaterialResult.GetValueOrDefault(id));
                    Add(candidate, id.Id, consumed / (recipe.ResultCount * (double) spawnCount));
                }
                // Entity/reagent ingredients are deliberately not counted as an extra manufacturing premium.
                Choose(candidate, ref best);
                if (recipe.Materials.Count > 0 && candidate.Count == 0)
                    best = candidate;
            }
        }
        path.Remove(prototype.ID);
        _composition[prototype.ID] = best;
        result = best ?? new();
        return best != null;
    }

    private void Choose(Dictionary<string, double> candidate, ref Dictionary<string, double>? best)
    {
        if (candidate.Count > 0 && (best == null || BaseValue(candidate) < BaseValue(best)))
            best = candidate;
    }

    private double BaseValue(Dictionary<string, double> materials) =>
        materials.Sum(pair => pair.Value * _prices.GetValueOrDefault(pair.Key));

    public static void Add(Dictionary<string, double> target, IReadOnlyDictionary<string, double> source, double factor = 1)
    {
        foreach (var (id, amount) in source)
            Add(target, id, amount * factor);
    }

    public static void Add(Dictionary<string, double> target, string id, double amount)
    {
        if (double.IsFinite(amount) && amount > 0)
            target[id] = target.GetValueOrDefault(id) + amount;
    }

    public MaterialMarketQuote Quote(IEnumerable<EntityUid> roots, double coefficient = 1,
        IReadOnlyDictionary<EntityUid, double>? specialPrices = null,
        IReadOnlyDictionary<EntityUid, double>? fractions = null)
    {
        EnsureTables();
        EnsureDemandCycle();
        var quote = new MaterialMarketQuote
        {
            Revision = _revision,
            Cycle = DemandCycleId,
            CreatedAt = _timing.CurTime,
            Saturation = SaturationEnabled,
            Coefficient = double.IsFinite(coefficient) ? Math.Clamp(coefficient, 0, Profile.MaximumCoefficient) : 0,
        };
        if (specialPrices != null)
            foreach (var (entity, price) in specialPrices)
                quote.SpecialPrices[entity] = price;
        foreach (var root in roots)
        {
            quote.Roots.Add(root);
            Visit(root);
        }
        foreach (var material in quote.Materials.Keys)
            quote.Loads[material] = GetLoad(material);
        var materialAmount = AppraiseMaterials(quote.Materials, quote.Coefficient, out var ordinary, quote.Loads);
        quote.BeforeCoefficient = quote.Coefficient > 0 ? materialAmount / quote.Coefficient + quote.FixedAmount : 0;
        quote.OrdinaryAmount = ordinary + quote.FixedAmount * quote.Coefficient;
        quote.Amount = Content.Shared._WH40K.Economy.MaterialMarketMath.ToCash(materialAmount + quote.FixedAmount * quote.Coefficient);
        var unitPayouts = quote.Materials.ToDictionary(pair => pair.Key, pair =>
            AppraiseMaterials(new Dictionary<string, double> { [pair.Key] = pair.Value }, quote.Coefficient, out _, quote.Loads) / pair.Value);
        foreach (var (entity, composition) in quote.OwnMaterials)
            quote.OwnValues[entity] = composition.Sum(pair => pair.Value * unitPayouts.GetValueOrDefault(pair.Key));
        return quote;

        void Visit(EntityUid entity)
        {
            if (!Exists(entity) || !quote.Seen.Add(entity))
                return;
            var fraction = fractions == null ? 1 : Math.Clamp(fractions.GetValueOrDefault(entity), 0, 1);
            var id = MetaData(entity).EntityPrototype?.ID;
            if (fraction > 0 && quote.Coefficient > 0 && HasComp<TradeCrateComponent>(entity))
            {
                var price = specialPrices?.GetValueOrDefault(entity) ?? _legacyPricing.GetPrice(entity, includeContents: false);
                if (double.IsFinite(price) && price > 0)
                {
                    quote.FixedAmount += price * fraction;
                    quote.Sold.Add(entity);
                    quote.OwnValues[entity] = price * fraction * quote.Coefficient;
                }
            }
            else if (fraction > 0 && quote.Coefficient > 0 && TryGetOwnMaterials(entity, out var materials) && BaseValue(materials) > 0)
            {
                Add(quote.Materials, materials, fraction);
                quote.Sold.Add(entity);
                var own = new Dictionary<string, double>();
                Add(own, materials, fraction);
                quote.OwnMaterials[entity] = own;
            }
            else if (fraction > 0 && quote.Coefficient > 0 && id != null && (_specialGoods.ContainsKey(id) && !_latheRecipes.ContainsKey(id) &&
                         !HasComp<ConstructionComponent>(entity) || HasComp<TradeCrateComponent>(entity)))
            {
                var price = specialPrices?.GetValueOrDefault(entity) ?? _legacyPricing.GetPrice(entity, includeContents: false);
                if (double.IsFinite(price) && price > 0)
                {
                    quote.FixedAmount += price * fraction;
                    quote.Sold.Add(entity);
                    quote.OwnValues[entity] = price * fraction * quote.Coefficient;
                }
            }

            if (!TryComp<ContainerManagerComponent>(entity, out var containers))
                return;
            foreach (var container in containers.Containers.Values)
                foreach (var child in container.ContainedEntities)
                    Visit(child);
        }
    }

    public bool MatchesInventory(MaterialMarketQuote quote)
    {
        var current = Quote(quote.Roots, quote.Coefficient, quote.SpecialPrices);
        return current.FixedAmount == quote.FixedAmount && current.Coefficient == quote.Coefficient &&
               current.Seen.SetEquals(quote.Seen) && current.Sold.SetEquals(quote.Sold) &&
               current.Materials.Count == quote.Materials.Count && current.Materials.All(pair =>
                   quote.Materials.TryGetValue(pair.Key, out var amount) && amount == pair.Value);
    }
}
