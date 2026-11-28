using Robust.Shared.GameStates;
using Content.Shared._NF.Cloning;
using Robust.Shared.Prototypes;

namespace Content.Shared.TTS;

/// <summary>
/// Enables text-to-speech playback for an entity's speech.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TTSComponent : Component, ITransferredByCloning
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("voice")]
    public ProtoId<TTSVoicePrototype>? VoicePrototypeId { get; set; } = new("TtsTechpriest");
}
