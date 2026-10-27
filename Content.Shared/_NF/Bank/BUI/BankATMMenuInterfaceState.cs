using Robust.Shared.Serialization;

namespace Content.Shared._NF.Bank.BUI;

[NetSerializable, Serializable]
public sealed class BankATMMenuInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// bank balance of the character using the atm
    /// </summary>
    public int Balance;

    /// <summary>
    /// Savings of the player using the ATM.
    /// </summary>
    public long Savings;

    /// <summary>
    /// are the buttons enabled
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// how much cash is inserted
    /// </summary>
    public int Deposit;
    public int NetDeposit = -1;
    public int FactionTax;
    public string? FactionName;

    public BankATMMenuInterfaceState(int balance, long savings, bool enabled, int deposit)
    {
        Balance = balance;
        Savings = savings;
        Enabled = enabled;
        Deposit = deposit;
    }
}
