using Content.Shared.Mining;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared.Random;

/// <summary>
/// Linter-friendly version of weightedRandom for Ore prototypes.
/// </summary>
[Prototype]
public sealed partial class WeightedRandomOrePrototype : IWeightedRandomPrototype<OrePrototype>
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("weights")]
    public Dictionary<ProtoId<OrePrototype>, float> Weights { get; private set; } = new();
}
