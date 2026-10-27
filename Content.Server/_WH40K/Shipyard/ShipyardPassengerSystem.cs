using Content.Shared.Buckle;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Shipyard;

public sealed partial class ShipyardPassengerSystem : EntitySystem
{
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public void Evacuate(EntityUid shuttle, EntityCoordinates? destination)
    {
        var passengers = new List<EntityUid>();
        FindPassengers(shuttle, passengers);

        foreach (var passenger in passengers)
        {
            var xform = Transform(passenger);
            var coordinates = destination
                ?? new EntityCoordinates(xform.MapUid!.Value, _transform.GetWorldPosition(passenger));

            _buckle.Unbuckle((passenger, null), null);
            if (_containers.TryGetContainingContainer((passenger, null, null), out var container))
                _containers.Remove(passenger, container, reparent: false, force: true);

            _transform.SetCoordinates(passenger, coordinates);
        }
    }

    private void FindPassengers(EntityUid parent, List<EntityUid> passengers)
    {
        var children = Transform(parent).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<MobStateComponent>(child)
                || HasComp<ActorComponent>(child)
                || TryComp<MindContainerComponent>(child, out var mind) && mind.HasMind)
            {
                passengers.Add(child);
                continue;
            }

            FindPassengers(child, passengers);
        }
    }
}
