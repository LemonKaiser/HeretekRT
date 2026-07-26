using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Progression;

/// <summary>
/// Network-safe prototype shape for one server-authoritative XP source.
/// Clients load the data definition but never choose or submit a source or amount.
/// </summary>
[Prototype("wh40kExperienceSource")]
public sealed partial class Wh40kExperienceSourcePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Wh40kExperienceSourceType SourceType;

    /// <summary>
    /// Base reward in tenths of XP before difficulty and anti-farm multipliers.
    /// </summary>
    [DataField(required: true)]
    public long AmountTenths;

    [DataField]
    public Wh40kParticipationMode Participation = Wh40kParticipationMode.Radius;

    [DataField]
    public float Radius = 100f;
}

public enum Wh40kExperienceSourceType : byte
{
    Mission,
    Objective,
    Combat,
    Support,
    Story,
    Admin,
}

public enum Wh40kParticipationMode : byte
{
    Radius,
    Grid,
    Sector,
    Expedition,
}
