using Content.Shared.Inventory;
using Content.Shared.TTS;
using Content.Shared.VoiceMask;

namespace Content.Server.VoiceMask;

public sealed partial class VoiceMaskSystem
{
    private void InitializeTTS()
    {
        SubscribeLocalEvent<VoiceMaskComponent, InventoryRelayedEvent<TransformSpeakerVoiceEvent>>(OnSpeakerVoiceTransform);
        SubscribeLocalEvent<VoiceMaskComponent, VoiceMaskChangeVoiceMessage>(OnChangeVoice);
    }

    private static void OnSpeakerVoiceTransform(
        EntityUid uid,
        VoiceMaskComponent component,
        InventoryRelayedEvent<TransformSpeakerVoiceEvent> args)
    {
        args.Args.VoiceId = component.VoiceId;
    }

    private void OnChangeVoice(Entity<VoiceMaskComponent> entity, ref VoiceMaskChangeVoiceMessage message)
    {
        if (!_proto.HasIndex<TTSVoicePrototype>(message.Voice))
            return;

        entity.Comp.VoiceId = message.Voice;
        _popupSystem.PopupEntity(Loc.GetString("voice-mask-voice-popup-success"), entity, message.Actor);
        UpdateUI(entity);
    }
}
