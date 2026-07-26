using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.CharacterCreation;

/// <summary>
///     A homeworld offered during the introductory character creation.
///     Its characteristic modifiers are applied to the onboarding totals; features and talents remain descriptive.
/// </summary>
[Prototype("wh40kHomeworld")]
public sealed partial class Wh40kHomeworldPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public int Order { get; private set; }

    [DataField(required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string Description { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string LoreNote { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string FeatureName { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string FeatureDescription { get; private set; } = string.Empty;

    [DataField]
    public Dictionary<Wh40kCharacteristic, int> CharacteristicModifiers { get; private set; } = new();

    public int GetCharacteristicModifier(Wh40kCharacteristic characteristic)
    {
        return CharacteristicModifiers.GetValueOrDefault(characteristic);
    }

    [DataField(required: true)]
    public string FutureTalents { get; private set; } = string.Empty;
}
