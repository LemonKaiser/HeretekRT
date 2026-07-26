using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.CharacterCreation;

/// <summary>
///     A selectable portrait for the introductory character creator.
///     The prototype owns both rendered variants, so content can add or remove portraits without client code changes.
/// </summary>
[Prototype("wh40kPortrait")]
public sealed partial class Wh40kPortraitPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public int Order { get; private set; }

    [DataField(required: true)]
    public SpriteSpecifier SmallSprite { get; private set; } = SpriteSpecifier.Invalid;

    [DataField(required: true)]
    public SpriteSpecifier LargeSprite { get; private set; } = SpriteSpecifier.Invalid;
}
