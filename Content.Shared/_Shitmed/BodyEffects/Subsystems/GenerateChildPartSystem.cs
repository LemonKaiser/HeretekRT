using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Body.Events;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Network;
using System.Numerics;

namespace Content.Shared._Shitmed.BodyEffects.Subsystems;

public sealed partial class GenerateChildPartSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _bodySystem = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GenerateChildPartComponent, BodyPartComponentsModifyEvent>(OnPartComponentsModify);
    }

    private void OnPartComponentsModify(EntityUid uid, GenerateChildPartComponent component, ref BodyPartComponentsModifyEvent args)
    {
        if (args.Add)
            CreatePart(uid, component);
        //else
            //DeletePart(uid, component);
    }

    private void CreatePart(EntityUid uid, GenerateChildPartComponent component)
    {
        if (!TryComp(uid, out BodyPartComponent? partComp)
            || partComp.Body is null
            || component.Active)
            return;

        // I pinky swear to also move this to the server side properly next update :)
        if (_net.IsServer)
        {
            // Reuse an already-attached child part if the body or restore system populated the slot first.
            foreach (var childSlotId in partComp.Children.Keys)
            {
                if (_containers.TryGetContainer(uid, SharedBodySystem.GetPartSlotContainerId(childSlotId), out var existingCont) &&
                    existingCont is ContainerSlot existingSlot &&
                    existingSlot.ContainedEntity is { } existingChild)
                {
                    component.ChildPart = existingChild;
                    component.Active = true;
                    return;
                }
            }

            var childPart = SpawnAttachedTo(component.Id, new EntityCoordinates(partComp.Body.Value, Vector2.Zero));

            if (!TryComp(childPart, out BodyPartComponent? childPartComp))
                return;

            var slotName = _bodySystem.GetSlotFromBodyPart(childPartComp);
            _bodySystem.TryCreatePartSlot(uid, slotName, childPartComp.PartType, out var _);
            _bodySystem.AttachPart(uid, slotName, childPart, partComp, childPartComp);
            component.ChildPart = childPart;
            component.Active = true;
            Dirty(childPart, childPartComp);
        }
    }

    // Still unusued, gotta figure out what I want to do with this function outside of fuckery with mantis blades.
    private void DeletePart(EntityUid uid, GenerateChildPartComponent component)
    {
        if (!TryComp(uid, out BodyPartComponent? partComp))
            return;

        _bodySystem.DropSlotContents((uid, partComp));
        var ev = new BodyPartDroppedEvent((uid, partComp));
        RaiseLocalEvent(uid, ref ev);
        QueueDel(uid);
    }
}

