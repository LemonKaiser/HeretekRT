using System.Linq;
using Content.Server.Destructible;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.Shipyard;

public sealed partial class ShipyardPricingSystem
{
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private ShipyardPassengerSystem _passengers = default!;

    public const double StockBuybackRate = 0.80;

    public void RecordPurchase(EntityUid grid, int purchasePrice)
    {
        if (HasComp<ShipyardResaleComponent>(grid))
            return;

        var resale = AddComp<ShipyardResaleComponent>(grid);
        resale.PurchasePrice = Math.Max(0, purchasePrice);
        resale.Stock = AppraiseEquipment(grid, out var includedValue);
        // Items returned to the seller and evacuated passengers are not paid for a second time.
        resale.ExcludedStockValue = Math.Max(0, _pricing.AppraiseGrid(grid) - includedValue);
        if (TryComp<MapGridComponent>(grid, out var mapGrid))
            resale.Deck = _maps.GetAllTiles(grid, mapGrid).Where(tile => !tile.Tile.IsEmpty)
                .Select(tile => tile.GridIndices).ToHashSet();
    }

    public bool TryGetResaleValue(EntityUid grid, float additionalEquipmentRate, out int value)
    {
        value = 0;
        if (!TryComp<ShipyardResaleComponent>(grid, out var resale))
            return false;

        var equipment = AppraiseEquipment(grid, out _);
        var retainedStock = 0.0;
        var additionalEquipment = 0.0;
        foreach (var (prototype, amount) in equipment)
        {
            var original = resale.Stock.GetValueOrDefault(prototype);
            retainedStock += Math.Min(original, amount);
            additionalEquipment += Math.Max(0, amount - original);
        }

        var initialStock = resale.Stock.Values.Sum();
        var stockCondition = initialStock > 0 ? Math.Clamp(retainedStock / initialStock, 0, 1) : 1;
        var deckCondition = 1.0;
        if (resale.Deck.Count > 0 && TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            var remainingDeck = _maps.GetAllTiles(grid, mapGrid)
                .Count(tile => !tile.Tile.IsEmpty && resale.Deck.Contains(tile.GridIndices));
            deckCondition = (double) remainingDeck / resale.Deck.Count;
        }

        var paidStock = Math.Max(0, resale.PurchasePrice - resale.ExcludedStockValue);
        var buyback = paidStock * StockBuybackRate * Math.Min(stockCondition, deckCondition);
        buyback += _materialMarket.Enabled
            ? GetMaterialResaleValue(grid, Math.Clamp(additionalEquipmentRate, 0, 1))
            : additionalEquipment * Math.Clamp(additionalEquipmentRate, 0, 1);
        // Appraisal and condition ratios use floating point; do not lose a credit to rounding noise.
        value = (int) Math.Clamp(Math.Floor(Math.Round(buyback, 6)), 0, int.MaxValue);
        return true;
    }

    private Dictionary<string, double> AppraiseEquipment(EntityUid grid, out double includedValue)
    {
        var equipment = new Dictionary<string, double>();
        var visited = new HashSet<EntityUid>();
        var included = 0.0;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
            Appraise(child);
        includedValue = included;
        return equipment;

        void Appraise(EntityUid entity)
        {
            if (!visited.Add(entity) || _passengers.IsPassenger(entity) ||
                TryComp<ShipyardSellConditionComponent>(entity, out var condition) && condition.PreserveOnSale)
                return;

            var price = _pricing.GetPrice(entity, includeContents: false);
            included += price;
            var prototype = MetaData(entity).EntityPrototype?.ID ?? string.Empty;
            equipment[prototype] = equipment.GetValueOrDefault(prototype) + price * EquipmentCondition(entity);

            if (!TryComp<ContainerManagerComponent>(entity, out var containers))
                return;
            foreach (var container in containers.Containers.Values)
            {
                foreach (var item in container.ContainedEntities)
                    Appraise(item);
            }
        }
    }

    private double EquipmentCondition(EntityUid entity)
    {
        if (!TryComp<DamageableComponent>(entity, out var damage) || damage.TotalDamage <= FixedPoint2.Zero)
            return 1;

        var destroyedAt = _destructible.DestroyedAt(entity);
        return destroyedAt <= FixedPoint2.Zero || destroyedAt == FixedPoint2.MaxValue
            ? 1
            : Math.Clamp(1.0 - damage.TotalDamage.Float() / destroyedAt.Float(), 0, 1);
    }
}
