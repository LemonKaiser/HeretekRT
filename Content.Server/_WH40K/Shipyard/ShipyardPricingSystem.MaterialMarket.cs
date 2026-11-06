using Content.Server._WH40K.Economy;
using Content.Shared._NF.Shipyard.Components;
using Robust.Shared.Containers;

namespace Content.Server._WH40K.Shipyard;

public sealed partial class ShipyardPricingSystem
{
    [Dependency] private MaterialMarketSystem _materialMarket = default!;
    private readonly Dictionary<EntityUid, MaterialMarketQuote> _materialSaleQuotes = new();

    public int GetMaterialResaleValue(EntityUid grid, double coefficient, bool includeStock = false)
    {
        var original = !includeStock && TryComp<ShipyardResaleComponent>(grid, out var resale)
            ? new Dictionary<string, double>(resale.Stock) : new();
        var fractions = new Dictionary<EntityUid, double>();
        var seen = new HashSet<EntityUid>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
            Visit(child);
        var quote = _materialMarket.Quote(fractions.Keys, coefficient, fractions: fractions);
        _materialSaleQuotes[grid] = quote;
        return quote.Amount;

        void Visit(EntityUid entity)
        {
            if (!seen.Add(entity) || _passengers.IsPassenger(entity) ||
                TryComp<ShipyardSellConditionComponent>(entity, out var condition) && condition.PreserveOnSale)
                return;
            var prototype = MetaData(entity).EntityPrototype?.ID ?? string.Empty;
            var value = _pricing.GetPrice(entity, includeContents: false) * EquipmentCondition(entity);
            var retained = Math.Min(original.GetValueOrDefault(prototype), value);
            original[prototype] = Math.Max(0, original.GetValueOrDefault(prototype) - retained);
            fractions[entity] = value > 0 ? Math.Clamp((value - retained) / value, 0, 1) * EquipmentCondition(entity) : 1;
            if (TryComp<ContainerManagerComponent>(entity, out var containers))
                foreach (var container in containers.Containers.Values)
                    foreach (var nested in container.ContainedEntities)
                        Visit(nested);
        }
    }

    public bool ApproveMaterialSale(EntityUid grid, Func<bool> approve)
    {
        if (!_materialMarket.Enabled || !_materialSaleQuotes.Remove(grid, out var quote))
            return approve();
        return _materialMarket.TryTransaction(quote, approve);
    }

    public bool UsesMaterialMarket => _materialMarket.Enabled;
}
