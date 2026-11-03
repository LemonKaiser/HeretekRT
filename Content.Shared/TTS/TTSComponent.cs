using Robust.Shared.GameStates;
using Content.Shared._NF.Cloning;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.TTS;

/// <summary>
/// Enables text-to-speech playback for an entity's speech.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TTSComponent : Component, ITransferredByCloning
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("voice", customTypeSerializer: typeof(PrototypeIdSerializer<TTSVoicePrototype>))]
    public string? VoicePrototypeId { get; set; } = "TtsTechpriest";
}
