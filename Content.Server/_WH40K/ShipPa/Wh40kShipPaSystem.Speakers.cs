using Content.Server.Power.Components;
using Content.Shared._WH40K.ShipPa;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Power;

namespace Content.Server._WH40K.ShipPa;

public sealed partial class Wh40kShipPaSystem
{
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _gridSpeakers = new();
    private readonly Dictionary<EntityUid, EntityUid> _speakerGrids = new();

    private const float DamagedThreshold = 0.25f;

    private void InitializeSpeakers()
    {
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, ComponentStartup>(OnSpeakerStartup);
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, MapInitEvent>(OnSpeakerMapInit);
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, ComponentShutdown>(OnSpeakerShutdown);
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, AnchorStateChangedEvent>(OnSpeakerAnchorChanged);
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, EntParentChangedMessage>(OnSpeakerParentChanged);
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, PowerChangedEvent>(OnSpeakerPowerChanged);
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, DamageChangedEvent>(OnSpeakerDamageChanged);
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, BreakageEventArgs>(OnSpeakerBreakage);
        SubscribeLocalEvent<Wh40kShipPaSpeakerComponent, ExaminedEvent>(OnSpeakerExamined);
    }

    private void OnSpeakerStartup(Entity<Wh40kShipPaSpeakerComponent> ent, ref ComponentStartup args)
    {
        IndexSpeaker(ent);
    }

    private void IndexSpeaker(EntityUid uid, bool remove = false)
    {
        if (_speakerGrids.Remove(uid, out var previous))
        {
            if (_gridSpeakers.TryGetValue(previous, out var oldMembers))
            {
                oldMembers.Remove(uid);
                if (oldMembers.Count == 0)
                    _gridSpeakers.Remove(previous);
            }

            QueueRefresh(previous);
        }

        if (remove || !TryComp(uid, out TransformComponent? xform) || !xform.Anchored
            || xform.GridUid is not { } grid || TerminatingOrDeleted(uid))
            return;

        if (!_gridSpeakers.TryGetValue(grid, out var members))
            _gridSpeakers[grid] = members = new HashSet<EntityUid>();
        members.Add(uid);
        _speakerGrids[uid] = grid;
        QueueRefresh(grid);
    }

    private void OnSpeakerMapInit(Entity<Wh40kShipPaSpeakerComponent> ent, ref MapInitEvent args)
    {
        IndexSpeaker(ent);
        UpdateDamage(ent);
        UpdateAppearance(ent);
        QueueRefresh(GetSpeakerGrid(ent));
    }

    private void OnSpeakerShutdown(Entity<Wh40kShipPaSpeakerComponent> ent, ref ComponentShutdown args)
    {
        DisableSpeaker(ent);
        IndexSpeaker(ent, remove: true);
    }

    private void OnSpeakerAnchorChanged(Entity<Wh40kShipPaSpeakerComponent> ent, ref AnchorStateChangedEvent args)
    {
        IndexSpeaker(ent);
        if (!args.Anchored)
            DisableSpeaker(ent);

        UpdateAppearance(ent);
        QueueRefresh(args.Transform.GridUid);
    }

    private void OnSpeakerParentChanged(Entity<Wh40kShipPaSpeakerComponent> ent, ref EntParentChangedMessage args)
    {
        DisableSpeaker(ent);
        IndexSpeaker(ent);
        QueueRefresh(args.OldParent);
        QueueRefresh(args.Transform.GridUid);
    }

    private void OnSpeakerPowerChanged(Entity<Wh40kShipPaSpeakerComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            DisableSpeaker(ent);

        UpdateAppearance(ent);
        QueueRefresh(GetSpeakerGrid(ent));
    }

    private void OnSpeakerDamageChanged(EntityUid uid, Wh40kShipPaSpeakerComponent comp, DamageChangedEvent args)
    {
        UpdateDamage((uid, comp));
    }

    private void OnSpeakerBreakage(EntityUid uid, Wh40kShipPaSpeakerComponent comp, BreakageEventArgs args)
    {
        if (comp.Broken)
            return;

        comp.Broken = true;
        Dirty(uid, comp);

        DisableSpeaker((uid, comp));
        UpdateAppearance((uid, comp));
        QueueRefresh(GetSpeakerGrid(uid));
    }

    private void OnSpeakerExamined(Entity<Wh40kShipPaSpeakerComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Fallback)
            return;

        var key = ent.Comp.Broken
            ? "wh40k-ship-pa-speaker-examine-broken"
            : !IsSpeakerPowered(ent)
                ? "wh40k-ship-pa-speaker-examine-unpowered"
                : ent.Comp.Distortion > 0f
                    ? "wh40k-ship-pa-speaker-examine-damaged"
                    : "wh40k-ship-pa-speaker-examine-ok";

        args.PushMarkup(Loc.GetString(key));
    }

    private bool IsSpeakerPowered(EntityUid uid)
    {
        return !TryComp(uid, out ApcPowerReceiverComponent? receiver) || receiver.Powered;
    }

    private void UpdateDamage(Entity<Wh40kShipPaSpeakerComponent> ent)
    {
        var total = TryComp(ent.Owner, out DamageableComponent? damageable) ? damageable.TotalDamage : FixedPoint2.Zero;
        var limit = ent.Comp.DistortionDamage;
        var distortion = limit <= FixedPoint2.Zero ? 0f : Math.Clamp(total.Float() / limit.Float(), 0f, 1f);

        var changed = false;

        if (Math.Abs(ent.Comp.Distortion - distortion) > 0.001f)
        {
            ent.Comp.Distortion = distortion;
            changed = true;
        }

        if (ent.Comp.Broken && total <= FixedPoint2.Zero)
        {
            ent.Comp.Broken = false;
            changed = true;
            QueueRefresh(GetSpeakerGrid(ent));
        }

        if (!changed)
            return;

        Dirty(ent);
        UpdateAppearance(ent);
    }

    private void UpdateAppearance(Entity<Wh40kShipPaSpeakerComponent> ent)
    {
        if (!TryComp(ent.Owner, out AppearanceComponent? appearance))
            return;

        _appearance.SetData(ent.Owner, Wh40kShipPaSpeakerVisuals.State, GetVisualState(ent), appearance);
    }

    private Wh40kShipPaSpeakerState GetVisualState(Entity<Wh40kShipPaSpeakerComponent> ent)
    {
        if (ent.Comp.Broken)
            return Wh40kShipPaSpeakerState.Broken;

        if (!IsFunctional(ent))
            return Wh40kShipPaSpeakerState.Unpowered;

        if (ent.Comp.BroadcastingUntil is { } until && _timing.CurTime < until)
            return Wh40kShipPaSpeakerState.Broadcasting;

        if (ent.Comp.Distortion >= DamagedThreshold)
            return Wh40kShipPaSpeakerState.Damaged;

        return Wh40kShipPaSpeakerState.Idle;
    }

    private void ClearExpiredBroadcasts()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<Wh40kShipPaSpeakerComponent>();

        while (query.MoveNext(out var uid, out var speaker))
        {
            if (speaker.BroadcastingUntil is not { } until || now < until)
                continue;

            speaker.BroadcastingUntil = null;
            UpdateAppearance((uid, speaker));
        }
    }

    private void DisableSpeaker(Entity<Wh40kShipPaSpeakerComponent> speaker)
    {
        speaker.Comp.Enabled = false;
        Dirty(speaker);
    }

    private EntityUid? GetSpeakerGrid(EntityUid uid)
    {
        return TryComp(uid, out TransformComponent? xform) ? xform.GridUid : null;
    }
}
