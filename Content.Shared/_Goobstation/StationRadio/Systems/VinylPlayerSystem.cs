using Content.Goobstation.Shared.StationRadio.Components;
using Content.Goobstation.Shared.StationRadio.Events;
using Content.Shared.Destructible;
using Content.Shared.DeviceLinking;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Station;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Map.Components;

namespace Content.Goobstation.Shared.StationRadio.Systems;

public sealed class VinylPlayerSystem : EntitySystem
{

    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedStationSystem _stations = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VinylPlayerComponent, EntInsertedIntoContainerMessage>(OnVinylInserted);
        SubscribeLocalEvent<VinylPlayerComponent, EntRemovedFromContainerMessage>(OnVinylRemove);
        SubscribeLocalEvent<VinylPlayerComponent, DestructionEventArgs>(OnDestruction);
        SubscribeLocalEvent<VinylPlayerComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<VinylPlayerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VinylPlayerComponent, EntParentChangedMessage>(OnSourceMoved);
        SubscribeLocalEvent<StationRadioReceiverComponent, EntParentChangedMessage>(OnReceiverMoved);
        SubscribeLocalEvent<EntParentChangedMessage>(OnGridMoved);
    }

    private void OnPowerChanged(EntityUid uid, VinylPlayerComponent comp, PowerChangedEvent args)
    {
        if (_net.IsClient || args.Powered)
            return;
        comp.SoundEntity = _audio.Stop(comp.SoundEntity);

        StopBroadcast(uid);
    }

    private void OnDestruction(EntityUid uid, VinylPlayerComponent comp, DestructionEventArgs args)
    {
        if (!_net.IsClient)
            StopBroadcast(uid);
    }

    private void OnShutdown(Entity<VinylPlayerComponent> ent, ref ComponentShutdown args)
    {
        if (!_net.IsClient)
            StopBroadcast(ent);
    }

    private void OnVinylInserted(EntityUid uid, VinylPlayerComponent comp, EntInsertedIntoContainerMessage args)
    {
        if (!TryComp(args.Entity, out VinylComponent? vinylcomp) || _net.IsClient || vinylcomp.Song == null || !_power.IsPowered(uid))
            return;

        var audio = _audio.PlayPredicted(vinylcomp.Song, uid, uid, AudioParams.Default.WithVolume(3f).WithMaxDistance(4.5f));
        if (audio != null)
            comp.SoundEntity = audio.Value.Entity;

        // Used by VinylSummonRuleSystem
        var ev = new VinylInsertedEvent(args.Entity);
        RaiseLocalEvent(uid, ref ev);

        if (!CheckForRadioRig(uid))
            return;

        var query = EntityQueryEnumerator<StationRadioReceiverComponent>();
        while (query.MoveNext(out var receiver, out var receiverComponent))
        {
            if (receiverComponent.SoundEntity.HasValue || !CanReceiveBroadcast(uid, receiver))
                continue;

            receiverComponent.BroadcastSource = uid;
            RaiseLocalEvent(receiver, new StationRadioMediaPlayedEvent(vinylcomp.Song));
        }
    }

    private void OnVinylRemove(EntityUid uid, VinylPlayerComponent comp, EntRemovedFromContainerMessage args)
    {
        if (_net.IsClient || !HasComp<VinylComponent>(args.Entity))
            return;
        if (comp.SoundEntity != null)
            comp.SoundEntity = _audio.Stop(comp.SoundEntity);

        // Used by VinylSummonRuleSystem
        var ev = new VinylRemovedEvent(args.Entity);
        RaiseLocalEvent(uid, ref ev);

        StopBroadcast(uid);
    }

    private void StopBroadcast(EntityUid source)
    {
        var query = EntityQueryEnumerator<StationRadioReceiverComponent>();
        while (query.MoveNext(out var receiver, out var component))
        {
            if (component.BroadcastSource == source)
                RaiseLocalEvent(receiver, new StationRadioMediaStoppedEvent());
        }
    }

    private bool CanReceiveBroadcast(EntityUid sourceUid, EntityUid receiverUid)
    {
        var source = Transform(sourceUid);
        var receiver = Transform(receiverUid);
        if (source.GridUid != null && source.GridUid == receiver.GridUid)
            return true;

        return source.MapUid == receiver.MapUid &&
               _stations.GetOwningStation(sourceUid, source) is { } station &&
               station == _stations.GetOwningStation(receiverUid, receiver);
    }

    private void OnSourceMoved(Entity<VinylPlayerComponent> ent, ref EntParentChangedMessage args)
    {
        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<StationRadioReceiverComponent>();
        while (query.MoveNext(out var uid, out var receiver))
        {
            if (receiver.BroadcastSource == ent.Owner && !CanReceiveBroadcast(ent, uid))
                RaiseLocalEvent(uid, new StationRadioMediaStoppedEvent());
        }
    }

    private void OnReceiverMoved(Entity<StationRadioReceiverComponent> ent, ref EntParentChangedMessage args)
    {
        if (!_net.IsClient && ent.Comp.BroadcastSource is { } source && !CanReceiveBroadcast(source, ent))
            RaiseLocalEvent(ent, new StationRadioMediaStoppedEvent());
    }

    private void OnGridMoved(ref EntParentChangedMessage args)
    {
        if (_net.IsClient || !HasComp<MapGridComponent>(args.Entity) || args.OldMapId == args.Transform.MapUid)
            return;

        var query = EntityQueryEnumerator<StationRadioReceiverComponent>();
        while (query.MoveNext(out var uid, out var receiver))
        {
            if (receiver.BroadcastSource is { } source && !CanReceiveBroadcast(source, uid))
                RaiseLocalEvent(uid, new StationRadioMediaStoppedEvent());
        }
    }

    private bool CheckForRadioRig(EntityUid uid)
    {
        if (TryComp<DeviceLinkSourceComponent>(uid, out var source))
        {
            foreach (var linked in source.LinkedPorts.Keys)
            {
                if (HasComp<RadioRigComponent>(linked) && CheckForRadioServer(linked))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool CheckForRadioServer(EntityUid uid)
    {
        if (TryComp<DeviceLinkSinkComponent>(uid, out var source))
        {
            foreach (var linked in source.LinkedSources)
            {
                if (HasComp<StationRadioServerComponent>(linked))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
