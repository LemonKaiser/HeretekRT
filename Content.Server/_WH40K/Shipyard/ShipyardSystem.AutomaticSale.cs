using Content.Server.Shuttles.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Station.Components;
using Content.Shared.UserInterface;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    /// <summary>
    /// Finds a direct dock to a facility station. Another purchased vessel is not a station berth.
    /// </summary>
    public bool TryGetStationBerth(EntityUid shuttle, out EntityUid station, out EntityUid dock)
    {
        station = default;
        dock = default;
        foreach (var port in _docking.GetDocks(shuttle))
        {
            if (!port.Comp.Docked || port.Comp.DockedWith is not { } other ||
                !TryComp(other, out TransformComponent? transform) ||
                transform.GridUid is not { } grid || grid == shuttle ||
                _operationalDomains.IsVesselGrid(grid) ||
                _station.GetOwningStation(other) is not { } owner ||
                !HasComp<StationDataComponent>(owner))
                continue;

            station = owner;
            dock = other;
            return true;
        }

        return false;
    }

    public bool TryAutomaticSale(EntityUid shuttle, Func<int, bool> credit, out int proceeds)
    {
        proceeds = 0;
        if (TerminatingOrDeleted(shuttle) || !TryComp<ShuttleDeedComponent>(shuttle, out var deed) ||
            !TryGetStationBerth(shuttle, out var station, out var dock))
            return false;

        var destination = dock;
        ShipyardConsoleComponent? terms = null;
        var consoles = EntityQueryEnumerator<ShipyardConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var console, out var transform))
        {
            if (transform.GridUid != Transform(dock).GridUid ||
                !TryComp<ActivatableUIComponent>(uid, out var ui) ||
                ui.Key is not ShipyardConsoleUiKey.Shipyard)
                continue;

            destination = uid;
            terms = console;
            break;
        }

        if (FindDisableShipyardSaleObjects(shuttle, ShipyardConsoleUiKey.Shipyard,
                GetEntityQuery<ShipyardSellConditionComponent>(), GetEntityQuery<TransformComponent>()) != null)
            return false;

        var payout = 0;
        var result = TrySellShuttle(station, shuttle, destination, out _, appraisal =>
        {
            // Voucher ships are retired without generating a cash payout.
            if (deed.PurchasedWithVoucher)
                return true;

            payout = CalculateSaleProceeds(appraisal, terms);
            if (payout > 0 && !credit(payout))
                return false;

            ApplySaleTerms(appraisal, terms);
            return true;
        });
        if (result.Error != ShipyardSaleError.Success)
            return false;

        proceeds = payout;
        return true;
    }

    public bool TryDeleteAbandonedShuttle(EntityUid shuttle)
    {
        if (TerminatingOrDeleted(shuttle) || EntityManager.IsQueuedForDeletion(shuttle) ||
            !HasComp<ShipOwnershipComponent>(shuttle) || !HasComp<ShuttleComponent>(shuttle) ||
            _passengers.HasPassengers(shuttle))
            return false;

        _docking.UndockDocks(shuttle);
        if (_station.GetOwningStation(shuttle) is { } station &&
            _station.GetLargestGrid(station) == shuttle)
            _station.DeleteStation(station);

        Log.Info($"Removed abandoned shuttle {ToPrettyString(shuttle)} after its owner's absence timeout.");
        QueueDel(shuttle);
        _shuttleRecordsSystem.RefreshStateForAll(true);
        return true;
    }

    private int ApplySaleTerms(int appraisal, ShipyardConsoleComponent? terms)
    {
        var original = appraisal;
        if (terms != null)
        {
            foreach (var (account, rate) in terms.TaxAccounts)
            {
                var tax = CalculateSalesTax(original, rate);
                _bank.TrySectorDeposit(account, tax, LedgerEntryType.ShipyardTax);
            }
        }

        return CalculateSaleProceeds(appraisal, terms);
    }

    private int CalculateSaleProceeds(int appraisal, ShipyardConsoleComponent? terms)
    {
        return Math.Max(0, appraisal - (terms == null ? 0 : CalculateTotalSalesTax(terms, appraisal)));
    }

    /// <summary>
    /// Common quote before station taxes for manual sales, automatic sales, and console previews.
    /// </summary>
    public int GetShipSaleValue(EntityUid shuttle, ShipyardConsoleComponent? terms = null)
    {
        if (TryComp<ShuttleDeedComponent>(shuttle, out var deed) && deed.PurchasedWithVoucher)
            return 0;

        var equipmentRate = terms?.IgnoreBaseSaleRate == true ? 1f : _baseSaleRate;
        if (_vesselPricing.TryGetResaleValue(shuttle, equipmentRate, out var resale))
            return resale;

        return (int) (_pricing.AppraiseGrid(shuttle, LacksPreserveOnSaleComp) * equipmentRate);
    }
}
