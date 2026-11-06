using Robust.Shared.Serialization;
using Content.Shared._WH40K.Economy;

namespace Content.Shared.Cargo.BUI;

[NetSerializable, Serializable]
public sealed class CargoPalletConsoleInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// estimated apraised value of all the entities on top of pallets on the same grid as the console
    /// </summary>
    public int Appraisal;

    /// <summary>
    /// number of entities on top of pallets on the same grid as the console
    /// </summary>
    public int Count;

    /// <summary>
    /// are the buttons enabled
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// the multiplier for the given cargo sell
    /// </summary>
    public double TradeCrateMultiplier;

    /// <summary>
    /// the multiplier for the given cargo sell
    /// </summary>
    public double OtherMultiplier;
    public bool MarketEnabled;
    public Guid MarketCycle;
    public double MarketFactor = 1;
    public int MarketTax;
    public int MarketGross;
    public int MarketBeforeCoefficient;
    public int MarketBaseline;
    public int MarketSpecial;
    public int MarketCycleSeconds;
    public bool MarketDemandCycles;
    public List<MaterialMarketEntry> MarketEntries = new();

    public CargoPalletConsoleInterfaceState(int appraisal, int count, bool enabled, double tradeCrateMultiplier = 1, double otherMultiplier = 1)
    {
        Appraisal = appraisal;
        Count = count;
        Enabled = enabled;
        TradeCrateMultiplier = tradeCrateMultiplier;
        OtherMultiplier = otherMultiplier;
    }
}
