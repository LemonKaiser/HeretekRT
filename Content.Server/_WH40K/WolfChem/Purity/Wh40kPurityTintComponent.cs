using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>
/// Sets <c>Wh40kPurityTintVisuals.Tinted</c> while a reagent in one of the entity's solutions is at or above a grade, so
/// a visualizer can mark top-grade product.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kPurityTintComponent : Component
{
    [DataField]
    public string Solution = "food";

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    /// <summary>The lowest grade that shows the tint.</summary>
    [DataField]
    public float Threshold = 0.991f;
}
