using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Economy;

[Serializable, NetSerializable]
public sealed class MaterialMarketEntry
{
    public string Material = string.Empty;
    public string Name = string.Empty;
    public string Unit = string.Empty;
    public double Demand = 1;
    public double Factor = 1;
    public double UnitPrice;
    public double Quantity;
    public int Value;
}
