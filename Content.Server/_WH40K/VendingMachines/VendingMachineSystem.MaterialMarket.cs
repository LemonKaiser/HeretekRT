using System.Linq;
using Content.Server._WH40K.Economy;
using Content.Shared._NF.Bank.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;

namespace Content.Server.VendingMachines;

public sealed partial class VendingMachineSystem
{
    [Dependency] private MaterialMarketSystem _materialMarket = default!;

    public int GetActualPrice(EntityUid uid, VendingMachineComponent component, EntityPrototype prototype)
    {
        if (!component.RequiresCash)
            return 0;
        var price = _pricing.GetEstimatedPrice(prototype);
        if (price == 0)
            price = 20;
        if (TryComp<MarketModifierComponent>(uid, out var modifier))
            price *= modifier.Mod;
        var vendPrice = _pricing.GetEstimatedVendPrice(prototype);
        var amount = vendPrice > 0 ? vendPrice : price;
        var offered = (int) Math.Clamp(amount, 0, int.MaxValue);
        return _materialMarket.GetPurchasePrice(prototype.ID, offered);
    }

    private void RefreshMarketPrices(EntityUid uid, VendingMachineComponent component)
    {
        component.MarketPrices.Clear();
        if (_materialMarket.Enabled)
            foreach (var entry in GetAllInventory(uid, component))
                if (_prototypeManager.TryIndex<EntityPrototype>(entry.ID, out var product))
                    component.MarketPrices[entry.ID] = GetActualPrice(uid, component, product);
        Dirty(uid, component);
    }

    private void OnMarketUIOpened(EntityUid uid, VendingMachineComponent component, BoundUIOpenedEvent args)
    {
        RefreshMarketPrices(uid, component);
    }
}
