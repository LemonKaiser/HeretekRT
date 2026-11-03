namespace Content.Server._WH40K.Shipyard;

/// <summary>
/// Records the actual purchase and the delivered stock used to calculate a ship's buyback.
/// </summary>
[RegisterComponent, Access(typeof(ShipyardPricingSystem))]
public sealed partial class ShipyardResaleComponent : Component
{
    [DataField]
    public int PurchasePrice;

    [DataField]
    public double ExcludedStockValue;

    [DataField]
    public Dictionary<string, double> Stock = new();

    [DataField]
    public HashSet<Vector2i> Deck = new();
}
