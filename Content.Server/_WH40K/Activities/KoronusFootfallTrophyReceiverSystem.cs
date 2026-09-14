using System.Linq;
using Content.Server._WH40K.Activities.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.Activities;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Resolves the final, physical Footfall hand-off. The system is intentionally a sink: it accepts
/// a closed catalogue, deletes only the accepted root, and cannot issue money, spawn a reward,
/// modify a bank account, or operate outside the actual Footfall map.
/// </summary>
public sealed class KoronusFootfallTrophyReceiverSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusActivityTrophyReceiverComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<KoronusActivityTrophyReceiverComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<KoronusActivityTrophyReceiverComponent, EntInsertedIntoContainerMessage>(OnPatientInserted);
    }

    private void OnInteractUsing(
        EntityUid uid,
        KoronusActivityTrophyReceiverComponent component,
        InteractUsingEvent args)
    {
        if (args.Handled || !IsLivingAttachedPlayer(args.User))
            return;

        args.Handled = true;
        if (!TryAcceptTrophy(uid, args.Used))
        {
            _popup.PopupEntity(Loc.GetString("koronus-activity-delivery-rejected"), uid, args.User, PopupType.SmallCaution);
            return;
        }

        _popup.PopupEntity(Loc.GetString("koronus-activity-delivery-accepted"), uid, args.User, PopupType.Medium);
    }

    private void OnInsertAttempt(
        EntityUid uid,
        KoronusActivityTrophyReceiverComponent component,
        ContainerIsInsertingAttemptEvent args)
    {
        if (!IsFootfallReceiver(uid) ||
            component.Consumer != KoronusActivityTrophyConsumer.Medicae ||
            args.Container.ID != component.PatientContainerId ||
            !CanAcceptPatient(component, args.EntityUid))
        {
            args.Cancel();
        }
    }

    private void OnPatientInserted(
        EntityUid uid,
        KoronusActivityTrophyReceiverComponent component,
        EntInsertedIntoContainerMessage args)
    {
        if (component.Consumer != KoronusActivityTrophyConsumer.Medicae ||
            args.Container.ID != component.PatientContainerId ||
            !TryAcceptPatient(uid, args.Entity))
        {
            return;
        }
    }

    /// <summary>
    /// Authoritative item hand-off used by the interaction handler and integration coverage. It
    /// deliberately validates Footfall and the receiver again rather than trusting the caller.
    /// </summary>
    public bool TryAcceptTrophy(EntityUid receiver, EntityUid item)
    {
        if (!TryComp<KoronusActivityTrophyReceiverComponent>(receiver, out var component) ||
            !IsFootfallReceiver(receiver) ||
            !CanAcceptHandItem(component, item))
        {
            return false;
        }

        QueueDel(item);
        return true;
    }

    /// <summary>
    /// Accepts only the dedicated, already-extracted rescue patient. Ordinary bodies, cargo and
    /// an unresolved activity objective all fail without being moved or deleted.
    /// </summary>
    public bool TryAcceptPatient(EntityUid receiver, EntityUid patient)
    {
        if (!TryComp<KoronusActivityTrophyReceiverComponent>(receiver, out var component) ||
            !IsFootfallReceiver(receiver) ||
            !CanAcceptPatient(component, patient))
        {
            return false;
        }

        QueueDel(patient);
        return true;
    }

    private bool CanAcceptHandItem(KoronusActivityTrophyReceiverComponent receiver, EntityUid item)
    {
        if (HasComp<KoronusActivityRecoveryPatientComponent>(item) ||
            HasComp<KoronusActivityObjectiveComponent>(item) ||
            HasComp<KoronusActivityOwnedComponent>(item) ||
            TerminatingOrDeleted(item) ||
            EntityManager.IsQueuedForDeletion(item))
        {
            return false;
        }

        return MetaData(item).EntityPrototype?.ID is { } prototypeId &&
               KoronusActivityRuntimePolicy.IsApprovedFootfallTrophy(receiver.Consumer, prototypeId, false);
    }

    private bool CanAcceptPatient(KoronusActivityTrophyReceiverComponent receiver, EntityUid patient)
    {
        if (receiver.Consumer != KoronusActivityTrophyConsumer.Medicae ||
            !HasComp<KoronusActivityRecoveryPatientComponent>(patient) ||
            HasComp<KoronusActivityObjectiveComponent>(patient) ||
            HasComp<KoronusActivityOwnedComponent>(patient) ||
            TerminatingOrDeleted(patient) ||
            EntityManager.IsQueuedForDeletion(patient))
        {
            return false;
        }

        return MetaData(patient).EntityPrototype?.ID is { } prototypeId &&
               KoronusActivityRuntimePolicy.IsApprovedFootfallTrophy(receiver.Consumer, prototypeId, true);
    }

    private bool IsFootfallReceiver(EntityUid receiver)
    {
        return _sector.TryGetSystemMap("Footfall", out var footfallMap) &&
               Transform(receiver).MapID == footfallMap;
    }

    private bool IsLivingAttachedPlayer(EntityUid entity)
    {
        return !HasComp<GhostComponent>(entity) &&
               TryComp<MobStateComponent>(entity, out var mob) &&
               mob.CurrentState == MobState.Alive &&
               _players.Sessions.Any(session => session.AttachedEntity == entity);
    }
}
