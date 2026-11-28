namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>On an item <see cref="Wh40kNarcoticGoodsSystem"/> marked as a narcotic in play, so only those are unmarked again.</summary>
[RegisterComponent]
public sealed partial class Wh40kNarcoticMarkedComponent : Component
{
    /// <summary>The mark components the item lacked and was given.</summary>
    [ViewVariables]
    public List<string> Added = new();
}
