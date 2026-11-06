using Content.Shared._Mono.Company;
using Content.Shared._Mono.ItemTax.Components;
using Content.Server._Mono.Store.Components;
using Content.Server._WH40K.Economy;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Bank.BUI;
using Content.Shared.FixedPoint;
using System.Linq;
using Content.Shared._WH40K.Economy;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    private sealed class MaterialSaleAccounting
    {
        public int Cash;
        public int Gross;
        public readonly Dictionary<SectorBankAccount, int> Taxes = new();
        public readonly Dictionary<ProtoId<CompanyPrototype>, Dictionary<string, FixedPoint2>> Injections = new();
    }

    private MaterialSaleAccounting GetMaterialSaleAccounting(MaterialMarketQuote quote)
    {
        var accounting = new MaterialSaleAccounting { Cash = quote.Amount, Gross = quote.Amount };
        foreach (var (entity, basis) in quote.OwnValues)
        {
            var gross = basis;
            if (TryComp<ItemTaxComponent>(entity, out var tax))
            {
                var rates = tax.TaxAccounts.Where(pair => float.IsFinite(pair.Value) && pair.Value > 0).ToArray();
                var remaining = Math.Min(MaterialMarketMath.ToCash(basis), accounting.Cash);
                foreach (var (account, rate) in rates)
                {
                    var amount = Math.Min(MaterialMarketMath.ToCash(gross * rate), remaining);
                    remaining -= amount;
                    accounting.Cash -= amount;
                    accounting.Taxes[account] = accounting.Taxes.GetValueOrDefault(account) + amount;
                }
            }
            if (!TryComp<CurrencyInjectionOnSellComponent>(entity, out var injection))
                continue;
            var legacy = _pricing.GetPrice(entity, includeContents: false);
            var scale = legacy > 0 ? Math.Clamp(basis / legacy, 0, 1) : 0;
            if (!accounting.Injections.TryGetValue(injection.Company, out var currencies))
                accounting.Injections[injection.Company] = currencies = new();
            foreach (var (currency, amount) in injection.Amount)
            {
                var scaled = FixedPoint2.New(amount.Double() * scale);
                currencies[currency] = currencies.GetValueOrDefault(currency) + scaled;
            }
        }
        return accounting;
    }

    private void ApplyMaterialSaleAccounting(MaterialSaleAccounting accounting)
    {
        foreach (var (account, amount) in accounting.Taxes)
            if (amount > 0)
                _bank.TrySectorDeposit(account, amount, LedgerEntryType.CargoTax);
        foreach (var (company, currencies) in accounting.Injections)
            _currencyInjection.InjectCurrency(company, currencies);
    }
}
