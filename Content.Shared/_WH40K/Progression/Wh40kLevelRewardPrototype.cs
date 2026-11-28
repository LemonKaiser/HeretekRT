using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Progression;

/// <summary>
/// Additional persistent rewards granted when an account reaches a configured level.
/// </summary>
[Prototype]
public sealed partial class Wh40kLevelRewardPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public int Level;

    [DataField]
    public long Currency;

    [DataField]
    public List<Wh40kLevelRewardItem> Items = new();
}

[DataDefinition]
public sealed partial class Wh40kLevelRewardItem
{
    [DataField(required: true)]
    public EntProtoId Id;

    [DataField]
    public int Count = 1;
}
