using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Barks;

/// <summary>
///     Describes a set of short sounds used to accompany in-character speech.
/// </summary>
[Prototype("bark")]
public sealed partial class BarkPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("soundFiles", required: true)]
    public List<string> SoundFiles { get; private set; } = [];

    /// <summary>
    ///     Whether players may select this bark in the character editor.
    /// </summary>
    [DataField("roundStart")]
    public bool RoundStart { get; private set; } = true;
}
