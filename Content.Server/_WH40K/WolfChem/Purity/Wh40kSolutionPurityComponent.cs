using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WolfChem.Purity;

/// <summary>
/// Sets fixed grades on reagents in one of the entity's solutions at map init, for vendor stock, dealer stock and loot.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kSolutionPurityComponent : Component
{
    /// <summary>The solution to grade.</summary>
    [DataField(required: true)]
    public string Solution = string.Empty;

    /// <summary>Grade of each reagent, 0-1.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, float> Reagents = new();
}
