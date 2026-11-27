using System.Linq;
using Content.Server._CorvaxNext.Silicons.Borgs;
using Content.Server._EinsteinEngines.Radio;
using Content.Shared._CorvaxNext.Silicons.Borgs;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared._WH40K.Cyborgs;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Merges encryption key channels onto a cyborg's built-in channels and restores the cyborg's channels
/// cleanly when a remotely controlling Station AI leaves.
/// </summary>
public sealed class Wh40kBorgRadioKeysServerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kBorgRadioKeysComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<Wh40kBorgRadioKeysComponent, EncryptionChannelsChangedEvent>(OnKeysChanged,
            after: new[] { typeof(IntrinsicRadioKeySystem) });
        SubscribeLocalEvent<Wh40kBorgRadioKeysComponent, Wh40kBorgTypeSelectedEvent>(OnTypeSelected);
        SubscribeLocalEvent<Wh40kBorgRadioKeysComponent, ReturnMindIntoAiEvent>(OnAiReturned,
            after: new[] { typeof(AiRemoteControlSystem) });
    }

    /// <summary>
    /// Restores a cyborg's own channels (built-in + encryption keys) after an AI stops controlling it.
    /// </summary>
    public void RestoreAfterAi(EntityUid uid)
    {
        if (!TryComp<Wh40kBorgRadioKeysComponent>(uid, out var comp)
            || !TryComp<AiRemoteControllerComponent>(uid, out var remote)
            || remote.AiHolder != null
            || remote.PreviouslyTransmitterChannels == null && remote.PreviouslyActiveRadioChannels == null)
            return;

        remote.PreviouslyTransmitterChannels = null;
        remote.PreviouslyActiveRadioChannels = null;

        if (TryComp<EncryptionKeyHolderComponent>(uid, out var keys))
            Apply((uid, comp), keys);
    }

    private void OnAiReturned(Entity<Wh40kBorgRadioKeysComponent> ent, ref ReturnMindIntoAiEvent args)
    {
        RestoreAfterAi(ent);
    }

    private void OnInit(Entity<Wh40kBorgRadioKeysComponent> ent, ref ComponentInit args)
    {
        if (TryComp<IntrinsicRadioTransmitterComponent>(ent, out var transmitter))
            ent.Comp.BaseTransmit = new(transmitter.Channels);

        if (TryComp<ActiveRadioComponent>(ent, out var receiver))
            ent.Comp.BaseReceive = new(receiver.Channels);
    }

    private void OnKeysChanged(Entity<Wh40kBorgRadioKeysComponent> ent, ref EncryptionChannelsChangedEvent args)
    {
        Apply(ent, args.Component);
    }

    private void OnTypeSelected(Entity<Wh40kBorgRadioKeysComponent> ent, ref Wh40kBorgTypeSelectedEvent args)
    {
        var ai = DrivingAi(ent);

        if (TryComp<IntrinsicRadioTransmitterComponent>(ent, out var transmitter))
            ent.Comp.BaseTransmit = ai?.PreviouslyTransmitterChannels is { } savedTransmit ? ToSet(savedTransmit) : new(transmitter.Channels);

        if (TryComp<ActiveRadioComponent>(ent, out var receiver))
            ent.Comp.BaseReceive = ai?.PreviouslyActiveRadioChannels is { } savedReceive ? ToSet(savedReceive) : new(receiver.Channels);

        if (TryComp<EncryptionKeyHolderComponent>(ent, out var keys))
            Apply(ent, keys);
    }

    private void Apply(Entity<Wh40kBorgRadioKeysComponent> ent, EncryptionKeyHolderComponent keys)
    {
        var ai = DrivingAi(ent);

        if (TryComp<IntrinsicRadioTransmitterComponent>(ent, out var transmitter))
        {
            var own = Merge(ent.Comp.BaseTransmit, keys.Channels);
            transmitter.Channels = own;

            if (ai != null)
            {
                ai.PreviouslyTransmitterChannels = own.Select(c => c.Id).ToArray();
                if (TryComp<IntrinsicRadioTransmitterComponent>(ai.AiHolder, out var aiTransmitter))
                    transmitter.Channels = new(aiTransmitter.Channels);
            }
        }

        if (TryComp<ActiveRadioComponent>(ent, out var receiver))
        {
            var own = Merge(ent.Comp.BaseReceive, keys.Channels);
            receiver.Channels = own;

            if (ai != null)
            {
                ai.PreviouslyActiveRadioChannels = own.Select(c => c.Id).ToArray();
                if (TryComp<ActiveRadioComponent>(ai.AiHolder, out var aiReceiver))
                    receiver.Channels = new(aiReceiver.Channels);
            }
        }
    }

    private AiRemoteControllerComponent? DrivingAi(EntityUid uid)
    {
        return TryComp<AiRemoteControllerComponent>(uid, out var remote) && remote.AiHolder != null ? remote : null;
    }

    private static HashSet<ProtoId<RadioChannelPrototype>> Merge(HashSet<ProtoId<RadioChannelPrototype>> builtIn, HashSet<string> keys)
    {
        var channels = new HashSet<ProtoId<RadioChannelPrototype>>(builtIn);
        foreach (var key in keys)
            channels.Add(key);

        return channels;
    }

    private static HashSet<ProtoId<RadioChannelPrototype>> ToSet(string[] channels)
    {
        return channels.Select(c => new ProtoId<RadioChannelPrototype>(c)).ToHashSet();
    }
}
