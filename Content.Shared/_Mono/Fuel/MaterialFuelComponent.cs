using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.Fuel;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MaterialFuelComponent : Component
{
    /// <summary>
    /// If not specified - picks first in composition.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<MaterialPrototype>? Material;

    [DataField]
    public int ConsumptionRate = 1;

    [DataField, AutoNetworkedField]
    public int BaseMaterialComposition;

    /// <summary>
    /// Material remaining in the first item; other items in the stack are unconsumed.
    /// Initialized from the physical composition on map initialization.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int? RemainingMaterial;
}
