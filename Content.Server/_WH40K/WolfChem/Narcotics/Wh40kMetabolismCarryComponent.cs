namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// What a metabolizer still owes of rates finer than 0.01 u a tick, so a slow organ removes a slow reagent in
/// whole hundredths every few ticks instead of never.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kMetabolismCarryComponent : Component
{
    /// <summary>Ten-thousandths of a unit owed per reagent and metabolism group, always below one hundredth.</summary>
    public Dictionary<(string Reagent, string Group), int> Owed = new();
}
