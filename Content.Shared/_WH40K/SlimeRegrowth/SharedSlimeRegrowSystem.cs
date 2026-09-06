using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._WH40K.SlimeRegrowth;

public abstract partial class SharedSlimeRegrowSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ThirstSystem _thirst = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SlimeRegrowComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SlimeRegrowComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SlimeRegrowComponent, SlimeRegrowLimbEvent>(OnRegrow);
    }

    private void OnMapInit(Entity<SlimeRegrowComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Action = _actions.AddAction(ent, ent.Comp.ActionPrototype);
        Dirty(ent);
    }

    private void OnShutdown(Entity<SlimeRegrowComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.Action);
    }

    private void OnRegrow(Entity<SlimeRegrowComponent> ent, ref SlimeRegrowLimbEvent args)
    {
        if (args.Handled || !_net.IsServer)
            return;

        var user = args.Performer;
        if (!TryComp<BodyComponent>(user, out var body) || body.Prototype == null ||
            _body.GetRootPartOrNull(user, body) == null)
        {
            return;
        }

        var missing = FindMissingLimbs(user, body);
        if (missing.Count == 0)
        {
            _popups.PopupEntity(Loc.GetString(ent.Comp.NoLimbPopup), user, user);
            return;
        }

        if (!TryComp<HungerComponent>(user, out var hunger) || _hunger.GetHunger(hunger) < ent.Comp.HungerCost)
        {
            _popups.PopupEntity(Loc.GetString(ent.Comp.TooHungryPopup), user, user);
            return;
        }

        if (!TryComp<ThirstComponent>(user, out var thirst) || thirst.CurrentThirst < ent.Comp.ThirstCost)
        {
            _popups.PopupEntity(Loc.GetString(ent.Comp.TooThirstyPopup), user, user);
            return;
        }

        var limb = _random.Pick(missing);
        if (!TryGrowLimb(limb))
        {
            _popups.PopupEntity(Loc.GetString(ent.Comp.NoLimbPopup), user, user);
            return;
        }

        _hunger.ModifyHunger(user, -ent.Comp.HungerCost, hunger);
        _thirst.ModifyThirst(user, thirst, -ent.Comp.ThirstCost);
        _popups.PopupEntity(Loc.GetString(ent.Comp.SuccessPopup), user, user);
        _audio.PlayEntity(ent.Comp.Sound, user, user);
        args.Handled = true;
    }

    private List<MissingLimb> FindMissingLimbs(EntityUid user, BodyComponent body)
    {
        var result = new List<MissingLimb>();
        var root = _body.GetRootPartOrNull(user, body);
        if (body.Prototype is not { } prototypeId || root == null)
            return result;

        var prototype = _prototypes.Index(prototypeId);
        var visited = new HashSet<string> { prototype.Root };
        var entities = new Dictionary<string, EntityUid> { [prototype.Root] = root.Value.Entity };
        var queue = new Queue<string>();
        queue.Enqueue(prototype.Root);

        while (queue.TryDequeue(out var parentSlotId))
        {
            var parentSlot = prototype.Slots[parentSlotId];
            foreach (var childSlotId in parentSlot.Connections)
            {
                if (!visited.Add(childSlotId))
                    continue;

                var childSlot = prototype.Slots[childSlotId];
                var parent = entities[parentSlotId];
                if (_containers.TryGetContainer(parent, SharedBodySystem.GetPartSlotContainerId(childSlotId), out var container) &&
                    container.ContainedEntities.Count > 0)
                {
                    entities[childSlotId] = container.ContainedEntities[0];
                    queue.Enqueue(childSlotId);
                    continue;
                }

                if (childSlot.Part is not { } partId ||
                    !_prototypes.TryIndex<EntityPrototype>(partId, out var partPrototype) ||
                    !partPrototype.TryComp<BodyPartComponent>(out var part, _componentFactory) ||
                    part.IsVital)
                {
                    continue;
                }

                result.Add(new MissingLimb(parent, childSlotId, childSlot));
            }
        }

        return result;
    }

    private bool TryGrowLimb(MissingLimb missing)
    {
        if (missing.Slot.Part is not { } partId)
            return false;

        var part = Spawn(partId, new EntityCoordinates(missing.Parent, Vector2.Zero));
        var partComponent = Comp<BodyPartComponent>(part);
        if (!_body.TryCreatePartSlotAndAttach(missing.Parent, missing.SlotId, part, partComponent.PartType, child: partComponent))
        {
            QueueDel(part);
            return false;
        }

        foreach (var (slotId, organId) in missing.Slot.Organs)
        {
            _body.TryCreateOrganSlot(part, slotId, out _);
            SpawnInContainerOrDrop(organId, part, SharedBodySystem.GetOrganContainerId(slotId));
        }

        return true;
    }

    private readonly record struct MissingLimb(EntityUid Parent, string SlotId, BodyPrototypeSlot Slot);
}
