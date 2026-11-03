using Content.Shared._Mono.Traits.Physical;
using Content.Shared.Stacks;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    /// <summary>
    /// Pays the validated Heretek purchase price with cash and, if permitted, the buyer's bank account.
    /// </summary>
    public bool TryPayForShuttle(EntityUid console, EntityUid buyer, int price)
    {
        if (price <= 0)
            return false;

        _cash.TryGetCash(console, out _, out var cashBalance);
        var cashPayment = Math.Min(price, cashBalance);
        var bankPayment = price - cashPayment;

        if (bankPayment > 0 && (HasComp<IronmanComponent>(buyer) || !_bank.TryBankWithdraw(buyer, bankPayment)))
            return false;

        if (cashPayment == 0 || _cash.TryCashPayment(console, cashPayment, out _))
            return true;

        if (bankPayment > 0 && !_bank.TryBankCredit(buyer, bankPayment, factionTax: false))
            _stackSystem.Spawn(bankPayment, _prototypeManager.Index<StackPrototype>(CreditPrototype), Transform(buyer).Coordinates);

        return false;
    }

    private bool TryCreditShuttleSale(EntityUid console, EntityUid buyer, int amount)
    {
        if (!HasComp<IronmanComponent>(buyer))
            return _bank.TryBankCredit(buyer, amount);

        _stackSystem.Spawn(amount, _prototypeManager.Index<StackPrototype>(CreditPrototype), Transform(console).Coordinates);
        return true;
    }
}
