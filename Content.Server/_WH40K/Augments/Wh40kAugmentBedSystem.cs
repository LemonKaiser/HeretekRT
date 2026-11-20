using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Particles;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared._WH40K.Augments;
using Content.Shared.Alert;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Particles;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments;

public sealed partial class Wh40kAugmentBedSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedImplanterSystem _implanter = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ParticleSpawnSystem _particles = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedSubdermalImplantSystem _subdermal = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;

    private const string PendingSlot = "augment-bed-pending";
    private const Wh40kAugmentBedKey UiKey = Wh40kAugmentBedKey.Key;
    private static readonly TimeSpan CompletionEffectTime = TimeSpan.FromSeconds(0.6);

    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kAugmentBedComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<Wh40kAugmentBedComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<Wh40kAugmentBedComponent, UnstrapAttemptEvent>(OnUnstrapAttempt);
        SubscribeLocalEvent<Wh40kAugmentBedComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<Wh40kAugmentBedComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<Wh40kAugmentBedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kAugmentBedComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<Wh40kAugmentBedComponent, Wh40kAugmentBedInstall>(OnInstall);
    }

    private void OnStrapped(Entity<Wh40kAugmentBedComponent> bed, ref StrappedEvent args)
    {
        bed.Comp.Patient = args.Buckle.Owner;
        Open(bed, args.Buckle);
    }

    private void OnUnstrapped(Entity<Wh40kAugmentBedComponent> bed, ref UnstrappedEvent args)
    {
        _ui.CloseUi(bed.Owner, UiKey, args.Buckle.Owner);
        if (bed.Comp.Patient == args.Buckle.Owner)
            bed.Comp.Patient = null;
        if (bed.Comp.ReopenFor == args.Buckle.Owner)
            bed.Comp.ReopenFor = null;
        if (bed.Comp.Procedure?.Patient == args.Buckle.Owner)
            Interrupt(bed);
    }

    private void OnUnstrapAttempt(Entity<Wh40kAugmentBedComponent> bed, ref UnstrapAttemptEvent args)
    {
        if (bed.Comp.Procedure?.Patient != args.Buckle.Owner)
            return;

        if (!CanOperate(bed))
        {
            Interrupt(bed);
            return;
        }

        args.Cancelled = true;
        if (args.Popup && args.User is { } user)
            _popup.PopupEntity(Loc.GetString("wh40k-augment-bed-locked"), bed, user);
    }

    private void OnPowerChanged(Entity<Wh40kAugmentBedComponent> bed, ref PowerChangedEvent args)
        => UpdateAvailability(bed);

    private void OnAnchorChanged(Entity<Wh40kAugmentBedComponent> bed, ref AnchorStateChangedEvent args)
        => UpdateAvailability(bed);

    private void OnShutdown(Entity<Wh40kAugmentBedComponent> bed, ref ComponentShutdown args)
        => Interrupt(bed);

    private void UpdateAvailability(Entity<Wh40kAugmentBedComponent> bed)
    {
        if (!CanOperate(bed))
            Interrupt(bed);

        foreach (var patient in _ui.GetActors(bed.Owner, UiKey))
            Publish(bed, patient);
    }

    private void OnGetVerbs(Entity<Wh40kAugmentBedComponent> bed, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (IsPatient(bed, args.User) && bed.Comp.Procedure == null && bed.Comp.ReopenFor == null)
        {
            var user = args.User;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wh40k-augment-bed-open"),
                Act = () => Open(bed, user),
            });
        }
    }

    private void Open(Entity<Wh40kAugmentBedComponent> bed, EntityUid patient)
    {
        if (bed.Comp.Procedure != null || bed.Comp.ReopenFor != null || !IsPatient(bed, patient) || !_mobState.IsAlive(patient))
            return;

        Publish(bed, patient);
        _ui.TryOpenUi(bed.Owner, UiKey, patient);
    }

    private bool IsPatient(EntityUid bed, EntityUid patient)
        => TryComp(patient, out BuckleComponent? buckle) && buckle.BuckledTo == bed;

    private bool CanOperate(EntityUid bed)
        => !TerminatingOrDeleted(bed) && Transform(bed).Anchored
            && TryComp(bed, out ApcPowerReceiverComponent? receiver)
            && !receiver.PowerDisabled && (!receiver.NeedsPower || _power.IsPowered(bed, receiver));

    private void OnInstall(EntityUid uid, Wh40kAugmentBedComponent comp, Wh40kAugmentBedInstall args)
    {
        if (!IsPatient(uid, args.Actor) || !_ui.IsUiOpen(uid, UiKey, args.Actor))
            return;

        if (TryGetEntity(args.Item, out var item) && TryStart((uid, comp), args.Actor, item.Value, args.Zone))
            return;

        _popup.PopupEntity(Loc.GetString("wh40k-augment-bed-unavailable"), uid, args.Actor);
        Publish((uid, comp), args.Actor);
    }

    private bool TryStart(Entity<Wh40kAugmentBedComponent> bed, EntityUid patient, EntityUid item, Wh40kAugmentBedZone zone)
    {
        if (bed.Comp.Procedure != null || !CanOperate(bed) || !IsPatient(bed, patient)
            || !_mobState.IsAlive(patient) || !CarriedItems(patient).Contains(item)
            || !ResolveTarget(patient, item, zone, out var target) || !CanFitImplant(bed, patient, item, target))
            return false;

        // Reserve the actual item before taking away a hand that might be holding it.
        var slot = _containers.EnsureContainer<ContainerSlot>(bed, PendingSlot);
        if (!_containers.Insert(item, slot))
            return false;

        var procedure = new Operation(patient, item, target, slot);
        bed.Comp.Procedure = procedure;
        EnsureComp<BuckleLockComponent>(bed);
        _alerts.ClearAlertCategory(patient, SharedBuckleSystem.BuckledAlertCategory);
        _ui.CloseUi(bed.Owner, UiKey, patient);
        procedure.Audio = _audio.PlayPvs(bed.Comp.ToolSound, bed)?.Entity;
        SetDeadline(bed, procedure);
        return true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<Wh40kAugmentBedComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            var bed = (uid, component);
            if (component.Procedure is not { } operation)
            {
                if (component.ReopenFor is { } patient && _timing.CurTime >= component.Deadline)
                {
                    component.ReopenFor = null;
                    Open(bed, patient);
                }

                if (component.Patient is { } occupant && IsPatient(uid, occupant) && _timing.CurTime >= component.NextScan)
                    Publish(bed, occupant);

                continue;
            }

            if (!CanOperate(uid) || !IsPatient(uid, operation.Patient) || !_mobState.IsAlive(operation.Patient)
                || operation.Slot.ContainedEntity != operation.Item)
            {
                Interrupt(bed);
                continue;
            }

            if (_timing.CurTime >= component.Deadline)
                Advance(bed, operation);
        }
    }

    private void Advance(Entity<Wh40kAugmentBedComponent> bed, Operation operation)
    {
        var (patient, item, target) = (operation.Patient, operation.Item, operation.Target);
        if (!ResolveTarget(patient, item, target.Zone, out var now)
            || now != (operation.RemovedOriginal ? target with { Existing = null } : target)
            || !CanFitImplant(bed, patient, item, now))
        {
            Interrupt(bed);
            return;
        }

        if (!operation.Fitting && target.Existing is { } old)
        {
            // The normal detach APIs update anatomy and appearance without the wound from amputation.
            var removed = target.Kind == TargetKind.Part
                ? RemovePart(target, old)
                : _body.RemoveOrgan(old);
            if (!removed)
            {
                Interrupt(bed);
                return;
            }

            if (target.Kind == TargetKind.Organ)
                DiscardOrganic(old);

            if (!KeepRemovedPart(old))
                PlayRemovalEffect(patient);
            operation.RemovedOriginal = true;
            operation.Audio = _audio.Stop(operation.Audio);
            SetDeadline(bed, operation);
            return;
        }

        if (!Install(operation))
        {
            Interrupt(bed);
            return;
        }

        bed.Comp.Procedure = null;
        Unlock(bed, patient);
        _audio.Stop(operation.Audio);
        _audio.PlayPvs(bed.Comp.ToolSound, bed);
        Spawn("EffectSparks", Transform(bed).Coordinates);
        _popup.PopupEntity(Loc.GetString("wh40k-augment-bed-complete"), bed, patient);
        bed.Comp.ReopenFor = patient;
        bed.Comp.Deadline = _timing.CurTime + CompletionEffectTime;
    }

    private bool RemovePart(Target target, EntityUid old)
    {
        // Detaching an arm or leg also drops its child parts as separate entities.
        var removedParts = _body.GetBodyPartChildren(old)
            .Select(part => (part.Id, part.Component, ParentSlot: _body.GetParentPartAndSlotOrNull(part.Id))).ToArray();
        if (!_body.DetachPart(target.Parent, target.Slot, old))
            return false;

        var keepRoot = KeepRemovedPart(old);
        foreach (var (part, component, parentSlot) in removedParts)
        {
            if (keepRoot && KeepRemovedPart(part))
            {
                if (part != old && parentSlot is { } child && KeepRemovedPart(child.Parent))
                    _body.AttachPart(child.Parent, child.Slot, part, part: component);
                continue;
            }

            // Recover separate modules before discarding the limb they were installed in.
            foreach (var (organ, _) in _body.GetPartOrgans(part, component).ToArray())
            {
                if (HasComp<AugmentComponent>(organ))
                    _body.RemoveOrgan(organ);
            }

            DiscardOrganic(part);
        }

        return true;
    }

    private bool IsAugmentation(EntityUid item) => HasComp<CyberneticsComponent>(item)
        || HasComp<Wh40kProstheticComponent>(item) || HasComp<AugmentComponent>(item)
        || HasComp<SubdermalImplantComponent>(item);

    private bool KeepRemovedPart(EntityUid item) => IsAugmentation(item)
        || Prototype(item) is { } prototype && Wh40kAugmentCatalog.IsNativeMechanicalOrgan(prototype)
        || TryComp(item, out DamageableComponent? damage) && (damage.DamageContainerID?.Id is "Inorganic" or "Silicon");

    private void DiscardOrganic(EntityUid item)
    {
        if (!KeepRemovedPart(item))
            QueueDel(item);
    }

    private void Unlock(EntityUid bed, EntityUid patient)
    {
        RemComp<BuckleLockComponent>(bed);
        if (!TerminatingOrDeleted(patient) && IsPatient(bed, patient) && TryComp(bed, out StrapComponent? strap))
            _alerts.ShowAlert(patient, strap.BuckledAlertType);
    }

    private void PlayRemovalEffect(EntityUid patient)
    {
        if (HasComp<BloodstreamComponent>(patient))
            _particles.Spawn(patient, "HrtBloodSprayFollowup",
                parameters: new ParticleSpawnParameters(Color: Color.Maroon, Intensity: 0.9f));
    }

    private void SetDeadline(Entity<Wh40kAugmentBedComponent> bed, Operation operation)
        => bed.Comp.Deadline = _timing.CurTime + (operation.Fitting ? bed.Comp.FittingTime : bed.Comp.RemovalTime);

    private void Interrupt(Entity<Wh40kAugmentBedComponent> bed)
    {
        if (bed.Comp.Procedure is not { } operation)
            return;

        bed.Comp.Procedure = null;
        Unlock(bed, operation.Patient);
        _audio.Stop(operation.Audio);
        if (!TerminatingOrDeleted(operation.Item) && operation.Slot.ContainedEntity == operation.Item)
            _containers.Remove(operation.Item, operation.Slot);

        if (!TerminatingOrDeleted(operation.Patient))
            _popup.PopupEntity(Loc.GetString("wh40k-augment-bed-interrupted"), operation.Patient, operation.Patient);
    }

    private void Publish(Entity<Wh40kAugmentBedComponent> bed, EntityUid patient)
    {
        bed.Comp.NextScan = _timing.CurTime + TimeSpan.FromSeconds(1);
        var options = new List<Wh40kAugmentBedOption>();
        foreach (var item in CarriedItems(patient))
        {
            if (InstallPrototype(item) is not { } prototype)
                continue;

            foreach (var zone in Wh40kAugmentCatalog.Zones(prototype, _componentFactory))
            {
                if (!ResolveTarget(patient, item, zone, out var target))
                    continue;

                var existing = target.Existing is { } old ? Prototype(old)?.ID : null;
                options.Add(new Wh40kAugmentBedOption(GetNetEntity(item), prototype.ID, zone, existing, target.RemoveInAdvance));
            }
        }

        var installed = InstalledAugments(patient).ToArray();
        var powered = CanOperate(bed);
        if (bed.Comp.LastScan is { } previous && previous.Powered == powered
            && previous.Options.SequenceEqual(options) && previous.Installed.SequenceEqual(installed))
            return;

        bed.Comp.LastScan = new Wh40kAugmentBedState(powered, options.ToArray(), installed);
        _ui.SetUiState(bed.Owner, UiKey, bed.Comp.LastScan);
    }

    private IEnumerable<Wh40kInstalledAugment> InstalledAugments(EntityUid patient)
    {
        foreach (var (part, component) in _body.GetBodyChildren(patient))
        {
            if (Prototype(part) is { } partPrototype)
            {
                foreach (var zone in Wh40kAugmentCatalog.Zones(partPrototype, _componentFactory))
                    yield return new Wh40kInstalledAugment(partPrototype.ID, zone);
            }

            foreach (var (organ, _) in _body.GetPartOrgans(part, component))
            {
                if (Prototype(organ) is not { } organPrototype)
                    continue;

                foreach (var zone in Wh40kAugmentCatalog.Zones(organPrototype, _componentFactory))
                {
                    if (zone == Wh40kAugmentBedZone.LeftArm && component.Symmetry != BodyPartSymmetry.Left
                        || zone == Wh40kAugmentBedZone.RightArm && component.Symmetry != BodyPartSymmetry.Right)
                        continue;

                    yield return new Wh40kInstalledAugment(organPrototype.ID, zone);
                }
            }
        }

        if (!TryComp(patient, out ImplantedComponent? implants))
            yield break;
        foreach (var item in implants.ImplantContainer.ContainedEntities)
        {
            if (InstallPrototype(item) is { } implantPrototype)
                yield return new Wh40kInstalledAugment(implantPrototype.ID, Wh40kAugmentBedZone.Core);
        }
    }

    private IEnumerable<EntityUid> CarriedItems(EntityUid patient)
    {
        var items = new Queue<EntityUid>(_inventory.GetHandOrInventoryEntities(patient));
        while (items.TryDequeue(out var item))
        {
            if (TerminatingOrDeleted(item))
                continue;

            yield return item;
            if (TryComp(item, out StorageComponent? storage))
            {
                foreach (var child in storage.Container.ContainedEntities)
                    items.Enqueue(child);
            }
        }
    }

    private EntityPrototype? InstallPrototype(EntityUid item)
    {
        if (TryComp(item, out ImplanterComponent? tool))
            return tool.ImplanterSlot.Item is { } implant ? Prototype(implant) : null;

        return Prototype(item);
    }

    private bool ResolveTarget(EntityUid patient, EntityUid item, Wh40kAugmentBedZone zone, out Target target)
    {
        target = default;
        if (InstallPrototype(item) is not { } prototype || !Wh40kAugmentCatalog.Zones(prototype, _componentFactory).Contains(zone))
            return false;

        if (TryComp(item, out BodyPartComponent? part))
        {
            var (parentType, side) = part.PartType switch
            {
                BodyPartType.Hand => (BodyPartType.Arm, part.Symmetry),
                BodyPartType.Foot => (BodyPartType.Leg, part.Symmetry),
                _ => (BodyPartType.Torso, BodyPartSymmetry.None),
            };
            var parent = _body.GetBodyChildrenOfType(patient, parentType, symmetry: side).FirstOrDefault();
            var slot = _body.GetSlotFromBodyPart(part);
            if (part.Body != null || parent.Component == null || !parent.Component.Children.ContainsKey(slot))
                return false;

            var existing = _body.GetBodyPartChildren(parent.Id, parent.Component)
                .FirstOrDefault(child => child.Component.ParentSlot?.Id == slot);
            if (existing.Component != null
                && _body.GetBodyPartChildren(existing.Id, existing.Component).Any(child => child.Component.IsVital))
                return false;

            target = new Target(parent.Id, slot, existing.Component == null ? null : existing.Id, zone, TargetKind.Part);
            return true;
        }

        if (TryComp(item, out OrganComponent? organ))
        {
            var (type, side) = zone switch
            {
                Wh40kAugmentBedZone.Eyes or Wh40kAugmentBedZone.LeftEye or Wh40kAugmentBedZone.RightEye
                    => (BodyPartType.Head, BodyPartSymmetry.None),
                Wh40kAugmentBedZone.LeftArm => (BodyPartType.Arm, BodyPartSymmetry.Left),
                Wh40kAugmentBedZone.RightArm => (BodyPartType.Arm, BodyPartSymmetry.Right),
                _ => (BodyPartType.Torso, BodyPartSymmetry.None),
            };
            var parent = _body.GetBodyChildrenOfType(patient, type, symmetry: side).FirstOrDefault();
            if (organ.Body != null || parent.Component == null)
                return false;

            var vital = organ.SlotId is "heart" or "lungs";
            var optics = Wh40kAugmentCatalog.IsOpticalSlot(organ.SlotId);
            if (vital && !_body.CanInsertOrgan(parent.Id, organ.SlotId, parent.Component))
                return false;

            var existing = optics
                ? ReplacedOptics(parent.Id, organ.SlotId).FirstOrDefault()
                : _body.GetPartOrgans(parent.Id, parent.Component).FirstOrDefault(other => other.Component.SlotId == organ.SlotId);
            if (existing.Component is { Removable: false })
                return false;
            target = new Target(
                parent.Id, organ.SlotId, existing.Component == null ? null : existing.Id, zone,
                optics ? TargetKind.Optics : vital ? TargetKind.VitalOrgan : TargetKind.Organ);
            return true;
        }

        target = new Target(patient, string.Empty, null, zone, TargetKind.Subdermal);
        return true;
    }

    private bool CanFitImplant(EntityUid bed, EntityUid patient, EntityUid item, Target target)
    {
        if (target.Kind != TargetKind.Subdermal)
            return true;

        if (TryComp(item, out ImplanterComponent? tool))
            return _implanter.CanImplant(patient, patient, item, tool, out var loaded, out _)
                && (tool.AllowMultipleImplants || !_implanter.CheckSameImplant(patient, loaded.Value));

        if (!TryComp(item, out SubdermalImplantComponent? implant) || implant.ImplantedEntity != null
            || _implanter.CheckSameImplant(patient, item)
            || !_whitelist.IsWhitelistPassOrNull(implant.Whitelist, patient)
            || !_whitelist.IsBlacklistFailOrNull(implant.Blacklist, patient))
            return false;

        var attempt = new AddImplantAttemptEvent(patient, patient, item, bed);
        RaiseLocalEvent(patient, attempt);
        return !attempt.Cancelled;
    }

    private bool Install(Operation operation)
    {
        var (patient, item, target) = (operation.Patient, operation.Item, operation.Target);
        switch (target.Kind)
        {
            case TargetKind.Part:
                return FitPart(item, target);
            case TargetKind.Organ:
                return _body.TryCreateOrganSlot(target.Parent, target.Slot, out _)
                    && _body.InsertOrgan(target.Parent, item, target.Slot);
            case TargetKind.Optics:
                return InstallOptics(patient, item, target);
            case TargetKind.VitalOrgan:
                // Finish the swap in one tick to avoid leaving a gap in breathing or circulation.
                if (target.Existing is { } old && !_body.RemoveOrgan(old))
                    return false;
                if (_body.InsertOrgan(target.Parent, item, target.Slot))
                {
                    if (target.Existing is { } replaced)
                    {
                        if (!KeepRemovedPart(replaced))
                            PlayRemovalEffect(patient);
                        DiscardOrganic(replaced);
                    }

                    return true;
                }
                if (target.Existing is { } original && !_body.InsertOrgan(target.Parent, original, target.Slot))
                    Log.Error($"Could not restore vital organ after an interrupted swap on {ToPrettyString(patient)}");
                return false;
            default:
                if (TryComp(item, out ImplanterComponent? tool) && tool.ImplanterSlot.Item is { } loaded)
                {
                    _implanter.Implant(patient, patient, item, tool);
                    if (!TryComp(patient, out ImplantedComponent? implants) || !implants.ImplantContainer.Contains(loaded))
                        return false;

                    _containers.Remove(item, operation.Slot);
                    return true;
                }

                var implant = Comp<SubdermalImplantComponent>(item);
                _subdermal.ForceImplant(patient, item, implant);
                return TryComp(patient, out ImplantedComponent? body) && body.ImplantContainer.Contains(item);
        }
    }

    private bool FitPart(EntityUid item, Target target)
    {
        if (!_body.CanAttachPart(target.Parent, target.Slot, item))
            return false;

        // Refit recovered hands and feet after the parent is attached so normal body events run.
        var children = _body.GetBodyPartChildren(item)
            .Select(part => (part.Id, Slot: _body.GetParentPartAndSlotOrNull(part.Id)))
            .Where(part => part.Slot?.Parent == item)
            .ToArray();
        var removed = new List<(EntityUid Id, string Slot)>(children.Length);
        foreach (var (child, connection) in children)
        {
            if (connection is not { } slot || !_body.DetachPart(item, slot.Slot, child))
                break;
            removed.Add((child, slot.Slot));
        }

        var attached = removed.Count == children.Length && _body.AttachPart(target.Parent, target.Slot, item);
        foreach (var (child, slot) in removed)
            _body.AttachPart(item, slot, child);

        return attached;
    }

    private IEnumerable<(EntityUid Id, OrganComponent Component)> ReplacedOptics(EntityUid head, string slot)
    {
        foreach (var organ in _body.GetPartOrgans(head))
        {
            var installedSlot = organ.Component.SlotId;
            if (installedSlot == slot
                || slot == Wh40kAugmentCatalog.Eyes && Wh40kAugmentCatalog.IsOpticalSlot(installedSlot)
                || installedSlot == Wh40kAugmentCatalog.Eyes && KeepRemovedPart(organ.Id))
                yield return organ;
        }
    }

    private bool InstallOptics(EntityUid patient, EntityUid item, Target target)
    {
        var previous = ReplacedOptics(target.Parent, target.Slot).ToArray();
        if (previous.Any(eye => !eye.Component.Removable) || !_body.TryCreateOrganSlot(target.Parent, target.Slot, out _))
            return false;

        var removed = new List<(EntityUid Id, OrganComponent Component)>();
        foreach (var eye in previous)
        {
            if (!_body.RemoveOrgan(eye.Id))
            {
                RestoreOptics(target.Parent, removed);
                return false;
            }

            removed.Add(eye);
        }

        if (!_body.InsertOrgan(target.Parent, item, target.Slot))
        {
            RestoreOptics(target.Parent, removed);
            return false;
        }

        // The natural pair services the remaining side until both independent implants are fitted.
        if (target.Slot != Wh40kAugmentCatalog.Eyes)
        {
            var eyes = _body.GetPartOrgans(target.Parent).ToArray();
            var opposite = target.Slot == Wh40kAugmentCatalog.LeftEye ? Wh40kAugmentCatalog.RightEye : Wh40kAugmentCatalog.LeftEye;
            if (eyes.Any(eye => eye.Component.SlotId == opposite))
            {
                foreach (var eye in eyes)
                {
                    if (eye.Component.SlotId == Wh40kAugmentCatalog.Eyes && eye.Component.Removable
                        && !KeepRemovedPart(eye.Id) && _body.RemoveOrgan(eye.Id))
                        removed.Add(eye);
                }
            }
        }

        if (removed.Any(eye => !KeepRemovedPart(eye.Id)))
            PlayRemovalEffect(patient);
        foreach (var eye in removed)
            DiscardOrganic(eye.Id);

        return true;
    }

    private void RestoreOptics(EntityUid head, List<(EntityUid Id, OrganComponent Component)> removed)
    {
        foreach (var (eye, organ) in removed)
        {
            if (!_body.InsertOrgan(head, eye, organ.SlotId))
                Log.Error($"Could not restore ocular organ {ToPrettyString(eye)}");
        }
    }

    internal enum TargetKind : byte { Part, Organ, Optics, VitalOrgan, Subdermal }

    internal readonly record struct Target(
        EntityUid Parent, string Slot, EntityUid? Existing, Wh40kAugmentBedZone Zone, TargetKind Kind)
    {
        public bool RemoveInAdvance => Existing != null && Kind is TargetKind.Part or TargetKind.Organ;
    }

    internal sealed class Operation(EntityUid patient, EntityUid item, Target target, ContainerSlot slot)
    {
        public readonly EntityUid Patient = patient;
        public readonly EntityUid Item = item;
        public readonly Target Target = target;
        public readonly ContainerSlot Slot = slot;
        public bool RemovedOriginal;
        public bool Fitting => !Target.RemoveInAdvance || RemovedOriginal;
        public EntityUid? Audio;
    }
}
