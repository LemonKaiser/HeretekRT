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

    public bool IsPassenger(EntityUid entity)
    {
        return HasComp<MobStateComponent>(entity) || HasComp<ActorComponent>(entity) ||
               TryComp<MindContainerComponent>(entity, out var mind) && mind.HasMind;
    }

    public bool HasPassengers(EntityUid shuttle)
    {
        var passengers = new List<EntityUid>();
        FindPassengers(shuttle, passengers);
        return passengers.Count > 0;
    }

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
            if (IsPassenger(child))
            {
                passengers.Add(child);
                continue;
            }

            FindPassengers(child, passengers);
        }
    }
}
