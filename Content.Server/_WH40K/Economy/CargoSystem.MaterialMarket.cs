using System.Linq;
using Content.Server._WH40K.Economy;
using Content.Server.Cargo.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Content.Shared.Stacks;
using Content.Shared._NF.Trade;
using Content.Server._NF.Trade;
using Robust.Shared.Containers;
using Content.Shared._WH40K.Economy;
using Content.Shared.Materials;
using System.Diagnostics;
using Robust.Shared.Player;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private MaterialMarketSystem _materialMarket = default!;
    private readonly Dictionary<EntityUid, CargoPalletConsoleInterfaceState> _palletMarketStates = new();
    private readonly Queue<EntityUid> _marketRefreshQueue = new();
    private float _marketRefresh;

    private void UpdateMaterialPallets(float frameTime)
    {
        if (!_materialMarket.Enabled)
            return;
        _marketRefresh -= frameTime;
        if (_marketRefreshQueue.Count == 0 && _marketRefresh <= 0)
        {
            _marketRefresh = 0.5f;
            var query = EntityQueryEnumerator<CargoPalletConsoleComponent>();
            while (query.MoveNext(out var uid, out _))
                if (_uiSystem.IsUiOpen(uid, CargoPalletConsoleUiKey.Sale))
                    _marketRefreshQueue.Enqueue(uid);
        }
        if (!_marketRefreshQueue.TryDequeue(out var next) || !TryComp<CargoPalletConsoleComponent>(next, out var console) ||
            !_uiSystem.IsUiOpen(next, CargoPalletConsoleUiKey.Sale))
            return;
        var start = Stopwatch.GetTimestamp();
        UpdatePalletConsoleInterface((next, console));
        _marketRefresh = Math.Max(_marketRefresh, (float) Math.Clamp(Stopwatch.GetElapsedTime(start).TotalSeconds * 100, 0.5, 5));
    }

    public MaterialMarketQuote AppraiseMaterialPallets(Entity<CargoPalletConsoleComponent> console, EntityUid grid)
    {
        var roots = new HashSet<EntityUid>();
        foreach (var (pallet, _, _) in GetCargoPallets(console, grid, BuySellType.Sell))
        {
            _setEnts.Clear();
            _lookup.GetEntitiesIntersecting(pallet, _setEnts, LookupFlags.Dynamic | LookupFlags.Sundries);
            foreach (var entity in _setEnts)
            {
                if (!_xformQuery.TryGetComponent(entity, out var transform) || transform.Anchored ||
                    !CanSell(entity, transform) || _whitelist.IsWhitelistFail(console.Comp.Whitelist, entity))
                    continue;
                roots.Add(entity);
            }
        }
        var coefficient = TryComp<MarketModifierComponent>(console, out var modifier) && !modifier.Buy ? modifier.Mod : 1;
        var specialPrices = new Dictionary<EntityUid, double>();
        var tradeFactor = TryResolveCargoDomain(console, out var domain) &&
                          TryComp<TradeCrateWildcardDestinationComponent>(domain.Owner, out var wildcard) ? wildcard.ValueMultiplier : 1;
        foreach (var root in roots)
            if (HasComp<TradeCrateComponent>(root))
                specialPrices[root] = _pricing.GetPrice(root, includeContents: false) * tradeFactor;
        var quote = _materialMarket.Quote(roots, coefficient, specialPrices);
        var accounting = GetMaterialSaleAccounting(quote);
        quote.CashAmount = accounting.Cash;
        quote.GrossAmount = accounting.Gross;
        return quote;
    }

    private void UpdateMaterialPalletInterface(Entity<CargoPalletConsoleComponent> console, EntityUid grid)
    {
        var quote = AppraiseMaterialPallets(console, grid);
        var coefficient = quote.Coefficient;
        var factor = quote.OrdinaryAmount > 0 ? quote.Amount / quote.OrdinaryAmount : 1;
        var state = new CargoPalletConsoleInterfaceState(quote.CashAmount, quote.Sold.Count, true, 1, coefficient)
            {
                MarketEnabled = true,
                MarketCycle = _materialMarket.DemandCycleId,
                MarketFactor = factor,
                MarketTax = quote.GrossAmount - quote.CashAmount,
                MarketGross = quote.GrossAmount,
                MarketBeforeCoefficient = MaterialMarketMath.ToCash(quote.BeforeCoefficient),
                MarketBaseline = MaterialMarketMath.ToCash(quote.OrdinaryAmount),
                MarketSpecial = MaterialMarketMath.ToCash(quote.FixedAmount * coefficient),
                MarketCycleSeconds = _materialMarket.DemandSecondsRemaining,
                MarketDemandCycles = _materialMarket.DemandEnabled,
                MarketEntries = GetMaterialMarketEntries(quote),
            };
        if (_palletMarketStates.TryGetValue(console.Owner, out var previous) && SameMarketDisplay(previous, state))
            return;
        _palletMarketStates[console.Owner] = state;
        _uiSystem.SetUiState(console.Owner, CargoPalletConsoleUiKey.Sale, state);
    }

    private static bool SameMarketDisplay(CargoPalletConsoleInterfaceState first, CargoPalletConsoleInterfaceState second)
    {
        if (first.Appraisal != second.Appraisal || first.Count != second.Count || first.MarketCycle != second.MarketCycle ||
            first.OtherMultiplier != second.OtherMultiplier || first.MarketTax != second.MarketTax ||
            first.MarketGross != second.MarketGross || first.MarketSpecial != second.MarketSpecial ||
            first.MarketBeforeCoefficient != second.MarketBeforeCoefficient ||
            Math.Round(first.MarketFactor * 100) != Math.Round(second.MarketFactor * 100) ||
            first.MarketEntries.Count != second.MarketEntries.Count)
            return false;
        for (var i = 0; i < first.MarketEntries.Count; i++)
        {
            var a = first.MarketEntries[i];
            var b = second.MarketEntries[i];
            if (a.Material != b.Material || a.Name != b.Name || a.Unit != b.Unit || a.Value != b.Value ||
                Math.Round(a.Quantity, 2) != Math.Round(b.Quantity, 2) || Math.Round(a.UnitPrice, 2) != Math.Round(b.UnitPrice, 2) ||
                Math.Round(a.Factor * 100) != Math.Round(b.Factor * 100) || Math.Round(a.Demand * 100) != Math.Round(b.Demand * 100))
                return false;
        }
        return true;
    }

    private List<MaterialMarketEntry> GetMaterialMarketEntries(MaterialMarketQuote quote)
    {
        var entries = new List<MaterialMarketEntry>();
        foreach (var id in _materialMarket.Profile.DemandMaterials.Select(material => material.Id).Concat(quote.Materials.Keys).Distinct())
        {
            if (!_protoMan.TryIndex<MaterialPrototype>(id, out var material))
                continue;
            var volume = 1.0;
            if (material.StackEntity is { } stack && _protoMan.TryIndex(stack, out var prototype) &&
                prototype.TryComp<PhysicalCompositionComponent>(out var composition, EntityManager.ComponentFactory) &&
                composition.MaterialComposition.GetValueOrDefault(id) is > 0 and var unitVolume)
                volume = unitVolume;
            var demand = _materialMarket.GetDemandFactor(id);
            var profile = _materialMarket.Profile;
            var factor = _materialMarket.SaturationEnabled
                ? MaterialMarketMath.DemandFactor(_materialMarket.GetLoad(id), profile.GraceVolume, profile.PressureScale,
                    profile.MinimumFactor, demand) : demand;
            var quantity = quote.Materials.GetValueOrDefault(id);
            var value = _materialMarket.AppraiseMaterials(new Dictionary<string, double> { [id] = quantity }, quote.Coefficient, out _, quote.Loads);
            entries.Add(new MaterialMarketEntry
            {
                Material = id,
                Name = material.Name,
                Unit = material.Unit,
                Demand = demand,
                Factor = factor,
                UnitPrice = _materialMarket.GetUnitPrice(id) * volume * quote.Coefficient * factor,
                Quantity = quantity / volume,
                Value = MaterialMarketMath.ToCash(value),
            });
        }
        return entries.OrderByDescending(entry => entry.Demand > 1).ThenBy(entry => entry.Demand < 1 ? 0 : 1)
            .ThenBy(entry => entry.Material, StringComparer.Ordinal).ToList();
    }

    private void SellMaterialPallets(Entity<CargoPalletConsoleComponent> console, EntityUid grid, CargoPalletSellMessage args)
    {
        TrySellMaterialPallets(console, grid, args.Actor);
    }

    public bool TrySellMaterialPallets(Entity<CargoPalletConsoleComponent> console, EntityUid grid, EntityUid actor)
    {
        if (!_materialMarket.Enabled)
            return false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var quote = AppraiseMaterialPallets(console, grid);
            if (quote.CashAmount <= 0)
            {
                SendMaterialSaleResult(console, actor, new(false, 0, 0, 0, "wh40k-market-no-goods"));
                UpdateMaterialPalletInterface(console, grid);
                return false;
            }
            if (!SendSoldAlert(console, quote.Sold))
            {
                SendMaterialSaleResult(console, actor, new(false, 0, 0, 0, "wh40k-market-sale-denied"));
                return false;
            }
            var accounting = GetMaterialSaleAccounting(quote);
            var cash = _stack.Spawn(accounting.Cash, _protoMan.Index<StackPrototype>(console.Comp.CashType), Transform(console).Coordinates);
            if (!_materialMarket.Commit(quote))
            {
                Del(cash);
                if (!_materialMarket.IsCurrent(quote))
                    continue;
                break;
            }
            _materialMarket.PreserveUnsoldChildren(quote);
            ApplyMaterialSaleAccounting(accounting);
            var sold = new EntitySoldEvent(quote.Sold, grid);
            RaiseLocalEvent(ref sold);
            foreach (var entity in quote.Sold)
                if (Exists(entity))
                    Del(entity);
            _audio.PlayPvs(ApproveSound, console);
            SendMaterialSaleResult(console, actor, new(true, quote.Sold.Count, accounting.Cash, accounting.Gross - accounting.Cash));
            UpdateMaterialPalletInterface(console, grid);
            return true;
        }
        SendMaterialSaleResult(console, actor, new(false, 0, 0, 0, "wh40k-market-unavailable"));
        UpdateMaterialPalletInterface(console, grid);
        return false;
    }

    private void SendMaterialSaleResult(EntityUid console, EntityUid actor, MaterialMarketSaleResultMessage result)
    {
        if (HasComp<ActorComponent>(actor))
            _uiSystem.ServerSendUiMessage(console, CargoPalletConsoleUiKey.Sale, result, actor);
    }
}
