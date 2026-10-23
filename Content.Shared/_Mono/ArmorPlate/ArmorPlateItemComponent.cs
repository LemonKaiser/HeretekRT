using Robust.Shared.GameStates;

namespace Content.Shared._Mono.ArmorPlate;

/// <summary>
/// Component for armor plates that can be inserted into compatible clothing.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ArmorPlateItemComponent : Component
{
    /// <summary>
    /// Maximum durability of this plate before destruction. Should match the destruction threshold in DestructibleComponent.
    /// A non-positive value provides no replaceable wear reserve.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int MaxDurability = -1;

    /// <summary>
    /// Walk speed modifier applied when this plate is active in worn clothing.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float WalkSpeedModifier = 1.0f;

    /// <summary>
    /// Sprint speed modifier applied when this plate is active in worn clothing.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float SprintSpeedModifier = 1.0f;

    /// <summary>
    /// Plate durability spent per unit of transferred armor wear, by damage type.
    /// A matching positive AbsorptionRatio is required; zero leaves wear on the armor.
    /// A value of 2 spends two plate durability for each unit of armor wear transferred.
    /// </summary>
    [DataField("damageToPlate")]
    public Dictionary<string, float> DamageToPlate = new();

    /// <summary>
    /// Fraction of armor wear transferred to this plate, by damage type, from zero to one.
    /// This does not change damage dealt to the wearer.
    /// </summary>
    [DataField("absorptionRatios")]
    public Dictionary<string, float> AbsorptionRatios = new();

}

