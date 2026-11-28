namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>
/// Grades every reagent in a grown produce's solution by the plant's potency: <see cref="MinPurity"/> at potency 0 up
/// to <see cref="MaxPurity"/> at potency 100.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kProducePurityComponent : Component
{
    [DataField]
    public float MinPurity = 0.5f;

    [DataField]
    public float MaxPurity = 1f;
}
