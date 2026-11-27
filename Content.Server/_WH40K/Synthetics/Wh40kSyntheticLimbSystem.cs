using System.Linq;
using Content.Server._WH40K.Augments;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Synthetics;

/// <summary>
/// Handles clean field detachment and "Lego limb" snap-reattachment for cybernetic/IPC chassis:
/// - Panel Detach drops mechanical/prosthetic limbs whole without bleeding or rot.
/// - Clicking a mechanical/prosthetic limb onto a cybernetic body snaps it on (4s / 8s self)
///   with <see cref="Wh40kLooseCouplingComponent"/> (move ×0.7, DoAfter ×1.5).
/// - Tightening with cable coils (5 passes with remaining count popup) or welding seats the coupling.
/// </summary>
public sealed class Wh40kSyntheticLimbSystem : SharedWh40kSyntheticsSystem
{
    private static readonly TimeSpan ReattachDelay = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CableRepairDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan WeldRepairDelay = TimeSpan.FromSeconds(3);
    private const float SelfMultiplier = 2f;

    private static readonly ProtoId<TagPrototype> CableCoilTag = "CableCoil";
    private static readonly SoundSpecifier ReattachSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
    private static readonly SoundSpecifier TightenSound = new SoundPathSpecifier("/Audio/Items/wirecutter.ogg");

    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly Wh40kChassisIsolationSystem _isolation = default!;
    [Dependency] private readonly Wh40kAugmentBedSystem _augmentBed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kChassisComponent, Wh40kLimbReattachDoAfterEvent>(OnReattachDoAfter);
        SubscribeLocalEvent<Wh40kChassisComponent, Wh40kLooseCouplingRepairDoAfterEvent>(OnRepairDoAfter);
    }

    public bool TryDetach(EntityUid body, EntityUid part)
    {
        if (!TryComp<BodyPartComponent>(part, out var bodyPart) ||
            bodyPart.Body != body ||
            !Wh40kChassisIsolationSystem.CanIsolate(bodyPart))
        {
            return false;
        }

        // Restore isolation markers on the part and its children so detached limbs never carry stale flags.
        foreach (var (child, _) in _body.GetBodyPartChildren(part, bodyPart).ToList())
            _isolation.TryRestore(child);
        _isolation.TryRestore(part);

        if (!_containers.TryGetContainingContainer((part, null, null), out var container))
            return false;

        var parent = container.Owner;
        if (!TryComp<BodyPartComponent>(parent, out var parentPart))
            return false;

        string? matchedSlotId = null;
        foreach (var (slotId, _) in parentPart.Children)
        {
            if (SharedBodySystem.GetPartSlotContainerId(slotId) == container.ID)
            {
                matchedSlotId = slotId;
                break;
            }
        }

        if (matchedSlotId == null || !parentPart.Children.TryGetValue(matchedSlotId, out var slot))
            return false;

        var disableEv = new BodyPartEnableChangedEvent(false);
        RaiseLocalEvent(part, ref disableEv);

        if (!_augmentBed.RemovePart(parent, slot.Id, part))
            return false;

        RemComp<Wh40kLooseCouplingComponent>(part);
        RemComp<PerishableComponent>(part);
        _transform.DropNextTo(part, body);
        _body.RefreshBaseLayersFromParts(body);
        _speed.RefreshMovementSpeedModifiers(body);
        return true;
    }

    public bool TryStartReattach(EntityUid body, EntityUid limb, EntityUid user)
    {
        if (!TryComp<BodyPartComponent>(limb, out var part) || part.Body != null)
            return false;

        if (!IsSyntheticChassis(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-synthetics-not-synthetic"), body, user);
            return true;
        }

        if (!IsMechanicalPart(limb))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-synthetic-limb-not-chassis"), body, user);
            return true;
        }

        if (!_augmentBed.IsOnPoweredTable(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-table-required-install"), body, user);
            return true;
        }

        if (!TryFindSocket(body, (limb, part), out var parent, out var slot))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-synthetic-limb-no-socket"), body, user);
            return true;
        }

        var delay = user == body ? ReattachDelay * SelfMultiplier : ReattachDelay;
        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            user,
            delay,
            new Wh40kLimbReattachDoAfterEvent(GetNetEntity(limb), GetNetEntity(parent), slot),
            body,
            body,
            limb)
        {
            BreakOnMove = true,
            NeedHand = true,
        });

        if (!started)
            return true;

        _popup.PopupEntity(Loc.GetString("wh40k-synthetic-limb-reattaching", ("limb", limb)), body, user);
        if (user != body)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-synthetic-limb-reattaching-other",
                    ("user", Identity.Entity(user, EntityManager)),
                    ("limb", limb)),
                body,
                body);
        }

        return true;
    }

    public bool TryStartRepairLoose(EntityUid body, EntityUid tool, EntityUid user)
    {
        var loose = FindFirstLoosePart(body);
        if (loose == null)
            return false;

        var isCable = _tags.HasTag(tool, CableCoilTag) && TryComp<StackComponent>(tool, out var stack) && stack.Count > 0;
        var isWelder = !isCable && _tools.HasQuality(tool, "Welding");
        if (!isCable && !isWelder)
            return false;

        if (isWelder)
        {
            return _tools.UseTool(
                tool,
                user,
                body,
                (float) WeldRepairDelay.TotalSeconds,
                new[] { "Welding" },
                new Wh40kLooseCouplingRepairDoAfterEvent(GetNetEntity(loose.Value), weld: true),
                fuel: 2f);
        }

        return _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            user,
            CableRepairDelay,
            new Wh40kLooseCouplingRepairDoAfterEvent(GetNetEntity(loose.Value), weld: false),
            body,
            body,
            tool)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnReattachDoAfter(Entity<Wh40kChassisComponent> ent, ref Wh40kLimbReattachDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled ||
            !TryGetEntity(args.Limb, out var limb) ||
            !TryGetEntity(args.Parent, out var parent))
        {
            return;
        }

        args.Handled = TryReattach(ent, limb.Value, parent.Value, args.Slot, args.Args.User);
    }

    public bool TryReattach(EntityUid body, EntityUid limb, EntityUid parent, string slot, EntityUid user)
    {
        if (!IsSyntheticChassis(body) || !_augmentBed.IsOnPoweredTable(body) ||
            !TryComp<BodyPartComponent>(limb, out var part) ||
            part.Body != null ||
            !IsMechanicalPart(limb) ||
            !_body.BodyHasChild(body, parent) ||
            !TryFindSocket(body, (limb, part), out var socket, out var socketSlot) ||
            socket != parent ||
            socketSlot != slot)
        {
            return false;
        }

        if (!Comp<BodyPartComponent>(parent).Children.ContainsKey(slot))
            _body.TryCreatePartSlot(parent, slot, part.PartType, out _);

        if (!_augmentBed.FitPart(parent, slot, limb))
            return false;

        RemComp<PerishableComponent>(limb);
        var enable = new BodyPartEnableChangedEvent(true);
        RaiseLocalEvent(limb, ref enable);
        var attached = new BodyPartAttachedEvent((limb, part));
        RaiseLocalEvent(body, ref attached);

        var loose = EnsureComp<Wh40kLooseCouplingComponent>(limb);
        loose.RemainingPasses = 5;
        Dirty(limb, loose);

        _body.RefreshBaseLayersFromParts(body);
        _speed.RefreshMovementSpeedModifiers(body);
        _audio.PlayPvs(ReattachSound, body);
        _popup.PopupEntity(Loc.GetString("wh40k-synthetic-limb-reattached", ("limb", limb)), body, user);
        return true;
    }

    private void OnRepairDoAfter(Entity<Wh40kChassisComponent> ent, ref Wh40kLooseCouplingRepairDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        EntityUid? part = TryGetEntity(args.Part, out var requested) &&
                          CompOrNull<BodyPartComponent>(requested.Value)?.Body == ent.Owner &&
                          HasComp<Wh40kLooseCouplingComponent>(requested.Value)
            ? requested.Value
            : FindFirstLoosePart(ent);

        if (part == null || !TryComp<Wh40kLooseCouplingComponent>(part.Value, out var loose))
            return;

        args.Handled = true;
        if (args.Weld)
        {
            SeatLooseCoupling(ent, part.Value, args.Args.User);
            return;
        }

        if (args.Args.Used is not { } cable || !_stacks.Use(cable, 1))
            return;

        var hasCableLeft = TryComp<StackComponent>(cable, out var stack) && stack.Count > 0;
        loose.RemainingPasses--;
        if (loose.RemainingPasses <= 0)
        {
            SeatLooseCoupling(ent, part.Value, args.Args.User);
            args.Repeat = hasCableLeft && FindFirstLoosePart(ent) != null;
            return;
        }

        Dirty(part.Value, loose);
        _audio.PlayPvs(TightenSound, ent);
        _popup.PopupEntity(
            Loc.GetString("wh40k-synthetic-limb-coupling-tightened", ("limb", part.Value), ("passes", loose.RemainingPasses)),
            ent,
            args.Args.User,
            PopupType.Small);
        args.Repeat = hasCableLeft;
    }

    public void SeatLooseCoupling(EntityUid body, EntityUid part, EntityUid? user = null)
    {
        RemComp<Wh40kLooseCouplingComponent>(part);
        _speed.RefreshMovementSpeedModifiers(body);
        _audio.PlayPvs(ReattachSound, body);
        _popup.PopupEntity(Loc.GetString("wh40k-synthetic-limb-coupling-seated", ("limb", part)), body);
    }

    public bool TryFindSocket(EntityUid body, Entity<BodyPartComponent> limb, out EntityUid parent, out string slot)
    {
        parent = default;
        slot = SlotId(limb.Comp);

        BodyPartType? parentType = limb.Comp.PartType switch
        {
            BodyPartType.Arm or BodyPartType.Leg or BodyPartType.Head => BodyPartType.Torso,
            BodyPartType.Hand => BodyPartType.Arm,
            BodyPartType.Foot => BodyPartType.Leg,
            _ => null,
        };

        if (parentType is not { } type)
            return false;

        BodyPartSymmetry? side = type == BodyPartType.Torso ? null : limb.Comp.Symmetry;
        foreach (var (candidate, _) in _body.GetBodyChildrenOfType(body, type, symmetry: side))
        {
            if (_containers.TryGetContainer(candidate, SharedBodySystem.GetPartSlotContainerId(slot), out var container) &&
                container.ContainedEntities.Count > 0)
            {
                continue;
            }

            parent = candidate;
            return true;
        }

        return false;
    }

    public EntityUid? FindFirstLoosePart(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (HasComp<Wh40kLooseCouplingComponent>(part))
                return part;
        }

        return null;
    }

    public void AddLooseExamine(EntityUid body, EntityUid target, ExaminedEvent args)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (TryComp<Wh40kLooseCouplingComponent>(part, out var loose))
            {
                args.PushMarkup(Loc.GetString(
                    "wh40k-synthetic-limb-examine-loose",
                    ("target", target),
                    ("part", part),
                    ("passes", loose.RemainingPasses)));
            }
        }
    }

    private static string SlotId(BodyPartComponent part) =>
        part.Symmetry == BodyPartSymmetry.None
            ? part.PartType.ToString().ToLowerInvariant()
            : $"{part.Symmetry.ToString().ToLowerInvariant()} {part.PartType.ToString().ToLowerInvariant()}";
}
