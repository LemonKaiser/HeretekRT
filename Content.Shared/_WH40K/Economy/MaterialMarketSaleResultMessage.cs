using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Economy;

[Serializable, NetSerializable]
public sealed class MaterialMarketSaleResultMessage(bool success, int count, int amount, int fees, string reason = "") : BoundUserInterfaceMessage
{
    public bool Success = success;
    public int Count = count;
    public int Amount = amount;
    public int Fees = fees;
    public string Reason = reason;
}
