using Content.Shared._Mono.Company;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Factions;

public static class FactionBankRules
{
    public const int TaxPercent = 1;
    public const int MaxMemoLength = 120;
    public const int HistoryLimit = 20;
    public static bool CanManage(FactionRole? role) => role == FactionRole.Leader;

    // Carry hundredths between receipts so splitting deposits cannot avoid the levy.
    public static (int Net, int Tax, int Remainder) Tax(int gross, int remainder)
    {
        if (gross <= 0 || remainder is < 0 or >= 100)
            throw new ArgumentOutOfRangeException();
        var value = (long) gross * TaxPercent + remainder;
        var tax = (int) (value / 100);
        return (gross - tax, tax, (int) (value % 100));
    }
}

[Serializable, NetSerializable]
public enum FactionBankEntryKind : byte { Income, Payout }

[Serializable, NetSerializable]
public sealed record FactionBankEntry(Guid Id, FactionBankEntryKind Kind, Guid Actor, Guid Recipient,
    string Name, int Gross, int Amount, string Memo, DateTime CreatedAt, bool Pending = false);

[Serializable, NetSerializable]
public sealed record FactionBankSnapshot(long Balance, FactionBankEntry[] Entries, bool Available = true);

[Serializable, NetSerializable]
public sealed record FactionBankPayout(int Amount, string Memo, Guid RequestId);
