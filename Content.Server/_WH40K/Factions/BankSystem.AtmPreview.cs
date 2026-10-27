using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared._Mono.Company;

namespace Content.Server._NF.Bank;

public sealed partial class BankSystem
{
    private void SetBankAtmState(EntityUid uid, Enum key, BankATMComponent component, EntityUid? player, BankATMMenuInterfaceState state)
    {
        if (state.Deposit >= 0 && player is { } body && _playerManager.TryGetSessionByEntity(body, out var session))
        {
            var deposit = state.Deposit;
            foreach (var coefficient in component.TaxAccounts.Values)
            {
                if (float.IsFinite(coefficient) && coefficient > 0)
                    deposit -= (int) Math.Floor(state.Deposit * coefficient);
            }
            GetTaxedDepositAmount(Math.Max(0, deposit), state.Balance, out var personal, out _);
            state.FactionTax = GetFactionIncomeTax(session.UserId, personal, out var faction);
            state.FactionName = faction != null && _prototypeManager.TryIndex<CompanyPrototype>(faction, out var prototype) ? prototype.Name : null;
            state.NetDeposit = personal - state.FactionTax;
        }
        _uiSystem.SetUiState(uid, key, state);
    }
}
