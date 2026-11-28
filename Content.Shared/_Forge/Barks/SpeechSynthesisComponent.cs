using Robust.Shared.GameStates;
using Content.Shared._NF.Cloning;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Barks;

/// <summary>
///     Enables bark playback for an entity when it speaks.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SpeechSynthesisComponent : Component, ITransferredByCloning
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("voice")]
    public ProtoId<BarkPrototype>? VoicePrototypeId { get; set; }

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
