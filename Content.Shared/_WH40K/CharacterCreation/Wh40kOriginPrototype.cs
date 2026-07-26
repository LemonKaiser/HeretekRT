using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.CharacterCreation;

/// <summary>
///     An origin offered during the introductory character creation.
///     Its characteristic modifiers are applied to the onboarding totals; talents remain descriptive.
/// </summary>
[Prototype("wh40kOrigin")]
public sealed partial class Wh40kOriginPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public int Order { get; private set; }

    [DataField(required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    ///     Short focus caption used by the narrow origin navigation card and the page kicker.
    /// </summary>
    [DataField(required: true)]
    public string Focus { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string FocusDescription { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string LoreNote { get; private set; } = string.Empty;

    [DataField]
    public Dictionary<Wh40kCharacteristic, int> CharacteristicModifiers { get; private set; } = new();

    public int GetCharacteristicModifier(Wh40kCharacteristic characteristic)
    {
        return CharacteristicModifiers.GetValueOrDefault(characteristic);
    }

    [DataField(required: true)]
    public string FutureTalents { get; private set; } = string.Empty;

}
