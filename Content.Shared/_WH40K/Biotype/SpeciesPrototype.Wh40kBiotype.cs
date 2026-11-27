using Content.Shared._WH40K.Biotype;

namespace Content.Shared.Humanoid.Prototypes;

public sealed partial class SpeciesPrototype
{
    [DataField]
    public Wh40kBiotype DefaultBiotype = Wh40kBiotype.Organic;

    [DataField]
    public bool CanChooseBiotype = true;
}
