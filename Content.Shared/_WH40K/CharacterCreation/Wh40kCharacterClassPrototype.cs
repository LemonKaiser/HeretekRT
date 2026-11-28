using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.CharacterCreation;

using Content.Shared._WH40K.ClassProgression;

/// <summary>
///     A starting class in the introductory character creator.
///     Its characteristic modifiers affect onboarding totals; abilities remain descriptive until their gameplay systems exist.
/// </summary>
[Prototype]
public sealed partial class Wh40kCharacterClassPrototype : IPrototype
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

    [DataField]
    public Dictionary<Wh40kCharacteristic, int> CharacteristicModifiers { get; private set; } = new();

    public int GetCharacteristicModifier(Wh40kCharacteristic characteristic)
    {
        return CharacteristicModifiers.GetValueOrDefault(characteristic);
    }

    /// <summary>
    ///     Compact caption for the narrow class navigation card.
    /// </summary>
    [DataField(required: true)]
    public string NavigationDescription { get; private set; } = string.Empty;

    /// <summary>
    ///     The two account-level skill branches. They describe future progression rather than abilities granted by onboarding.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<Wh40kClassSpecializationPrototype>> Specializations { get; private set; } = new();

    [DataField(required: true)]
    public string ActiveAbility { get; private set; } = string.Empty;

    [DataField(required: true)]
    public string PassiveAbility { get; private set; } = string.Empty;
}
