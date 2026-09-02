using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Shared.TTS;

/// <summary>
/// Describes a voice that can be selected for text-to-speech.
/// </summary>
[Prototype("ttsVoice")]
public sealed partial class TTSVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    [DataField("name")]
    public string Name { get; set; } = string.Empty;

    [DataField("sex", required: true)]
    public Sex Sex { get; set; }

    [DataField("speaker", required: true)]
    public string Speaker { get; set; } = string.Empty;

    [DataField("roundStart")]
    public bool RoundStart { get; set; } = true;
}
