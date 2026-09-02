using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Forge.Barks;

/// <summary>
///     Enables bark playback for an entity when it speaks.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SpeechSynthesisComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("voice", customTypeSerializer: typeof(PrototypeIdSerializer<BarkPrototype>))]
    public string? VoicePrototypeId { get; set; }

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("playbackSpeed")]
    public float PlaybackSpeed { get; set; } = 1f;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("pitch")]
    public float Pitch { get; set; } = 1f;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("expression")]
    public float Expression { get; set; } = 1f;
}
