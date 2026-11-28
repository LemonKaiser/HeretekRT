namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>Marks a narcotic so <see cref="Wh40kNarcoticStockSystem"/> counts it toward the stock aboard a ship.</summary>
[RegisterComponent]
public sealed partial class Wh40kNarcoticStockComponent : Component
{
}

/// <summary>On the narcotic that shows a ship's stock on radar.</summary>
[RegisterComponent]
public sealed partial class Wh40kNarcoticSignatureComponent : Component
{
    /// <summary>Whether the radar blip was put on with the signature, and so comes off with it.</summary>
    [DataField]
    public bool OwnBlip;
}
