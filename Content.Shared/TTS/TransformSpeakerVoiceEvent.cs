using Content.Shared.Inventory;

namespace Content.Shared.TTS;

/// <summary>
/// Allows equipped items to replace the TTS voice of their wearer.
/// </summary>
public sealed class TransformSpeakerVoiceEvent : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.MASK;
    public EntityUid Sender;
    public string VoiceId;

    public TransformSpeakerVoiceEvent(EntityUid sender, string voiceId)
    {
        Sender = sender;
        VoiceId = voiceId;
    }
}
