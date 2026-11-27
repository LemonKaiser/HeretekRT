using System.Diagnostics.CodeAnalysis;
using Content.Server._EinsteinEngines.Silicon.Charge;
using Content.Server._WH40K.Augments;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WH40K.Augments;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._WH40K.Synthetics;

/// <summary>
/// Manages the cybernetic torso service bay:
/// - Toggle open/close (3s other / 6s self).
/// - While open: movement ×0.8, incoming damage amplified (exposed plating), and organs or power cells
///   can be pulled/installed by hand (2s / 4s self) bypassing <c>cell_slot</c> lock.
/// </summary>
public sealed class Wh40kChassisBaySystem : SharedWh40kSyntheticsSystem
{
    private static readonly TimeSpan BayDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ComponentDelay = TimeSpan.FromSeconds(2);
    private const float SelfMultiplier = 2f;

    private static readonly SoundSpecifier BaySound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
    private static readonly SoundSpecifier ComponentSound = new SoundPathSpecifier("/Audio/Items/crowbar.ogg");

    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly Wh40kAugmentBedSystem _augmentBed = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly Wh40kSyntheticLimbSystem _limbs = default!;
    [Dependency] private readonly Wh40kLowPowerSystem _lowPower = default!;
    [Dependency] private readonly Wh40kChassisPanelSystem _panel = default!;
    [Dependency] private readonly Wh40kRadCollectorSystem _radCollector = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SiliconChargeSystem _siliconCharge = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kChassisComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<Wh40kChassisComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<Wh40kChassisComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<Wh40kChassisComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<Wh40kChassisComponent, Wh40kChassisBayDoAfterEvent>(OnBayDoAfter);
        SubscribeLocalEvent<Wh40kChassisComponent, Wh40kChassisPullOrganDoAfterEvent>(OnPullDoAfter);
        SubscribeLocalEvent<Wh40kChassisComponent, Wh40kChassisInstallOrganDoAfterEvent>(OnInstallDoAfter);

        Subs.BuiEvents<Wh40kChassisComponent>(Wh40kChassisPanelUiKey.Key, subs =>
        {
            subs.Event<Wh40kChassisToggleBayMessage>(OnToggleBayMessage);
            subs.Event<Wh40kChassisDisconnectOrganMessage>(OnDisconnectMessage);
        });
    }

    private void OnDamageModify(Entity<Wh40kChassisComponent> ent, ref DamageModifyEvent args)
    {
        _radCollector.FilterRadiationDamage(ent, ref args);

        if (!ent.Comp.BayOpen || args.TargetPart is not (null or TargetBodyPart.Torso) || args.Damage.GetTotal() <= 0)
            return;

        // Only amplify actual incoming damage (not healing).
        var amplified = new DamageSpecifier(args.Damage);
        foreach (var key in new List<string>(amplified.DamageDict.Keys))
        {
            if (amplified.DamageDict[key] > 0)
                amplified.DamageDict[key] *= ent.Comp.BayOpenDamageMultiplier;
        }

        args.Damage = amplified;
    }

    public bool TryStartToggle(EntityUid body, EntityUid user, bool open)
    {
        if (!TryComp<Wh40kChassisComponent>(body, out var chassis) || chassis.BayOpen == open)
            return false;

        if (!IsSyntheticChassis(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-synthetics-not-synthetic"), body, user);
            return false;
        }

        if (GetTorso(body) == null)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-no-torso"), body, user);
            return false;
        }

        var self = user == body;
        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            user,
            self ? BayDelay * SelfMultiplier : BayDelay,
            new Wh40kChassisBayDoAfterEvent(open),
            body,
            body)
        {
            BreakOnMove = true,
            NeedHand = true,
        });

        if (!started)
            return false;

        var target = Identity.Entity(body, EntityManager);
        if (self)
        {
            _popup.PopupEntity(Loc.GetString(open ? "wh40k-chassis-bay-opening-self" : "wh40k-chassis-bay-closing-self"), body, user);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString(open ? "wh40k-chassis-bay-opening" : "wh40k-chassis-bay-closing", ("target", target)), body, user);
            _popup.PopupEntity(
                Loc.GetString(open ? "wh40k-chassis-bay-opening-other" : "wh40k-chassis-bay-closing-other",
                    ("user", Identity.Entity(user, EntityManager))),
                body,
                body,
                PopupType.MediumCaution);
        }

        return true;
    }

    private void OnBayDoAfter(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisBayDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        if (!IsSyntheticChassis(ent) || GetTorso(ent) == null)
            return;

        SetBay(ent, args.Open);
    }

    public void SetBay(Entity<Wh40kChassisComponent> ent, bool open)
    {
        if (ent.Comp.BayOpen == open)
            return;

        ent.Comp.BayOpen = open;
        Dirty(ent);

        _movement.RefreshMovementSpeedModifiers(ent);
        _audio.PlayPvs(BaySound, ent);
        _popup.PopupEntity(Loc.GetString(open ? "wh40k-chassis-bay-opened" : "wh40k-chassis-bay-closed"), ent);
    }

    private void OnToggleBayMessage(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisToggleBayMessage args)
    {
        if (_panel.CanUsePanel(ent, args.Actor))
            TryStartToggle(ent, args.Actor, !ent.Comp.BayOpen);
    }

    public List<EntityUid> GetComponents(EntityUid body)
    {
        var found = new List<EntityUid>();
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
            found.Add(organ);

        if (TryGetCellContainer(body, out var container))
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (HasComp<PowerCellComponent>(contained))
                    found.Add(contained);
            }
        }

        return found;
    }

    public bool TryStartPull(EntityUid body, EntityUid item, EntityUid user)
    {
        if (!TryComp<Wh40kChassisComponent>(body, out var chassis))
            return false;

        if (!IsSyntheticChassis(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-synthetics-not-synthetic"), body, user);
            return false;
        }

        if (!chassis.BayOpen)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-closed-refusal"), body, user);
            return false;
        }

        if (RequiresOperatingTable(item) && !_augmentBed.IsOnPoweredTable(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-table-required-organ"), body, user);
            return false;
        }

        if (!IsInstalled(body, item))
            return false;

        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            user,
            user == body ? ComponentDelay * SelfMultiplier : ComponentDelay,
            new Wh40kChassisPullOrganDoAfterEvent(GetNetEntity(item)),
            body,
            body)
        {
            BreakOnMove = true,
            NeedHand = true,
        });

        if (!started)
            return false;

        _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-pulling", ("item", item)), body, user);
        if (user != body)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-chassis-bay-pulling-other",
                    ("user", Identity.Entity(user, EntityManager)),
                    ("item", item)),
                body,
                body,
                PopupType.MediumCaution);
        }

        return true;
    }

    private void OnPullDoAfter(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisPullOrganDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !TryGetEntity(args.Organ, out var item))
            return;

        args.Handled = TryPull(ent, item.Value, args.Args.User);
    }

    public bool TryPull(Entity<Wh40kChassisComponent> body, EntityUid item, EntityUid user)
    {
        if (!body.Comp.BayOpen || !IsSyntheticChassis(body))
            return false;

        if (RequiresOperatingTable(item) && !_augmentBed.IsOnPoweredTable(body))
            return false;

        if (TryComp<OrganComponent>(item, out var organ))
        {
            if (organ.Body != body.Owner || !organ.Removable || !_body.RemoveOrgan(item, organ))
                return false;
        }
        else if (!IsCell(body, item, out var container) || !_containers.Remove(item, container, force: true))
        {
            return false;
        }
        else
        {
            RefreshPower(body);
        }

        if (!_hands.TryPickupAnyHand(user, item))
            _transform.DropNextTo(item, body.Owner);

        _audio.PlayPvs(ComponentSound, body);
        _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-pulled", ("item", item)), body, user);
        if (user != body.Owner)
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-pulled-other", ("item", item)), body, body, PopupType.MediumCaution);

        return true;
    }

    private void OnDisconnectMessage(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisDisconnectOrganMessage args)
    {
        if (_panel.CanUsePanel(ent, args.Actor) && TryGetEntity(args.Organ, out var organ))
            TryStartPull(ent, organ.Value, args.Actor);
    }

    public bool TryStartInstall(EntityUid body, EntityUid item, EntityUid user)
    {
        if (!TryComp<Wh40kChassisComponent>(body, out var chassis))
            return false;

        if (!IsSyntheticChassis(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-synthetics-not-synthetic"), body, user);
            return false;
        }

        if (!chassis.BayOpen)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-closed-refusal"), body, user);
            return false;
        }

        if (RequiresOperatingTable(item) && !_augmentBed.IsOnPoweredTable(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-table-required-install"), body, user);
            return false;
        }

        var refusal = FindSlot(body, item, out var part, out var slot);
        if (refusal != null)
        {
            _popup.PopupEntity(Loc.GetString(refusal), body, user);
            return false;
        }

        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            user,
            user == body ? ComponentDelay * SelfMultiplier : ComponentDelay,
            new Wh40kChassisInstallOrganDoAfterEvent(GetNetEntity(item), GetNetEntity(part), slot),
            body,
            body,
            item)
        {
            BreakOnMove = true,
            NeedHand = true,
        });

        if (started)
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-installing", ("item", item)), body, user);

        return started;
    }

    private void OnInstallDoAfter(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisInstallOrganDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !TryGetEntity(args.Organ, out var item))
            return;

        args.Handled = TryInstall(ent, item.Value, args.Args.User);
    }

    public bool TryInstall(Entity<Wh40kChassisComponent> body, EntityUid item, EntityUid user)
    {
        if (!body.Comp.BayOpen || !IsSyntheticChassis(body))
            return false;

        if (RequiresOperatingTable(item) && !_augmentBed.IsOnPoweredTable(body))
            return false;

        if (FindSlot(body, item, out var part, out var slot) != null)
            return false;

        if (TryComp<OrganComponent>(item, out var organ))
        {
            if (!_body.InsertOrgan(part, item, slot, organ: organ))
                return false;
        }
        else if (!TryGetCellContainer(body, out var container) || !_containers.Insert(item, container, force: true))
        {
            return false;
        }
        else
        {
            RefreshPower(body);
        }

        _audio.PlayPvs(ComponentSound, body);
        _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-installed", ("item", item)), body, user);
        if (user != body.Owner)
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-bay-installed-other", ("item", item)), body, body);

        return true;
    }

    private string? FindSlot(EntityUid body, EntityUid item, out EntityUid part, out string slot)
    {
        part = default;
        slot = string.Empty;

        if (HasComp<PowerCellComponent>(item) && TryGetCellSlot(body, out var cellSlot))
        {
            if (cellSlot.HasItem)
                return "wh40k-chassis-bay-slot-full";

            if (_whitelist.IsWhitelistFail(cellSlot.Whitelist, item) || _whitelist.IsBlacklistPass(cellSlot.Blacklist, item))
                return "wh40k-chassis-bay-no-slot";

            part = GetTorso(body) ?? body;
            slot = cellSlot.ContainerSlot?.ID ?? string.Empty;
            return null;
        }

        if (!TryComp<OrganComponent>(item, out var organ) || organ.Body != null)
            return "wh40k-chassis-bay-no-slot";

        var exists = false;
        TryComp<Wh40kImplantSpecComponent>(item, out var spec);
        foreach (var (partId, partComp) in _body.GetBodyChildren(body))
        {
            if (!partComp.Organs.ContainsKey(organ.SlotId))
                continue;

            if (spec != null && !Wh40kAugmentCatalog.CanHost(EntityManager, body, partId, spec))
                continue;

            exists = true;
            if (_containers.TryGetContainer(partId, SharedBodySystem.GetOrganContainerId(organ.SlotId), out var container) &&
                container.ContainedEntities.Count > 0)
            {
                continue;
            }

            part = partId;
            slot = organ.SlotId;
            return null;
        }

        return exists ? "wh40k-chassis-bay-slot-full" : "wh40k-chassis-bay-no-slot";
    }

    private bool IsInstalled(EntityUid body, EntityUid item) =>
        TryComp<OrganComponent>(item, out var organ) ? organ.Body == body && organ.Removable : IsCell(body, item, out _);

    private bool IsCell(EntityUid body, EntityUid item, [NotNullWhen(true)] out BaseContainer? container) =>
        TryGetCellContainer(body, out container) && HasComp<PowerCellComponent>(item) && container.Contains(item);

    private bool TryGetCellContainer(EntityUid body, [NotNullWhen(true)] out BaseContainer? container)
    {
        container = null;
        if (!TryGetCellSlot(body, out var slot) || slot.ContainerSlot is not { } cellContainer)
            return false;

        container = cellContainer;
        return true;
    }

    private bool TryGetCellSlot(EntityUid body, [NotNullWhen(true)] out ItemSlot? slot)
    {
        slot = null;
        return TryComp<PowerCellSlotComponent>(body, out var cellSlot) &&
               _itemSlots.TryGetSlot(body, cellSlot.CellSlotId, out slot);
    }

    private void RefreshPower(EntityUid body)
    {
        if (TryComp<SiliconComponent>(body, out var silicon) && silicon.BatteryPowered)
        {
            short state = 0;
            if (_siliconCharge.TryGetSiliconBattery(body, out var battery) && battery.MaxCharge > 0f)
                state = (short) MathF.Round(battery.CurrentCharge / battery.MaxCharge * 10f);

            _siliconCharge.UpdateChargeState(body, state, silicon);
        }

        _lowPower.RefreshPowerState(body);
    }

    private void OnInteractUsing(Entity<Wh40kChassisComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<BodyPartComponent>(args.Used))
        {
            args.Handled = _limbs.TryStartReattach(ent, args.Used, args.User);
            return;
        }

        if (_limbs.TryStartRepairLoose(ent, args.Used, args.User))
        {
            args.Handled = true;
            return;
        }

        var isCell = HasComp<PowerCellComponent>(args.Used) &&
                     TryGetCellSlot(ent, out var cellSlot) &&
                     (cellSlot.Locked || ent.Comp.BayOpen);
        if (!HasComp<OrganComponent>(args.Used) && !isCell)
            return;

        args.Handled = true;
        TryStartInstall(ent, args.Used, args.User);
    }

    private void OnGetVerbs(Entity<Wh40kChassisComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || !IsSyntheticChassis(ent))
            return;

        var user = args.User;
        var open = ent.Comp.BayOpen;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(open ? "wh40k-chassis-bay-verb-close" : "wh40k-chassis-bay-verb-open"),
            Act = () => TryStartToggle(ent, user, !open),
            Priority = -1,
        });

        if (!open || !_hands.TryGetEmptyHand(user, out _))
            return;

        var tableReady = _augmentBed.IsOnPoweredTable(ent);
        foreach (var item in GetComponents(ent))
        {
            if (!IsInstalled(ent, item) || RequiresOperatingTable(item) && !tableReady)
                continue;

            var captured = item;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wh40k-chassis-bay-verb-pull", ("item", captured)),
                Act = () => TryStartPull(ent, captured, user),
                Priority = -2,
            });
        }
    }

    private bool RequiresOperatingTable(EntityUid item) => !HasComp<PowerCellComponent>(item);

    private void OnExamined(Entity<Wh40kChassisComponent> ent, ref ExaminedEvent args)
    {
        if (!IsSyntheticChassis(ent))
            return;

        var target = Identity.Entity(ent, EntityManager);
        if (ent.Comp.BayOpen)
            args.PushMarkup(Loc.GetString("wh40k-chassis-bay-examine-open", ("target", target)));

        _limbs.AddLooseExamine(ent, target, args);
    }

    private EntityUid? GetTorso(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildrenOfType(body, BodyPartType.Torso))
            return part;

        return null;
    }
}
