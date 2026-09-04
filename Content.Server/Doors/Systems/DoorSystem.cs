using Content.Server.Access;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Particles;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Power;
using Content.Shared.Prying.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server.Doors.Systems;

public sealed partial class DoorSystem : SharedDoorSystem
{
    [Dependency] private AirtightSystem _airtightSystem = default!;
    [Dependency] private ParticleSpawnSystem _particles = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoorBoltComponent, PowerChangedEvent>(OnBoltPowerChanged);
    }

    protected override void SetCollidable(
        EntityUid uid,
        bool collidable,
        DoorComponent? door = null,
        PhysicsComponent? physics = null,
        OccluderComponent? occluder = null)
    {
        if (!Resolve(uid, ref door))
            return;

        if (door.ChangeAirtight && TryComp(uid, out AirtightComponent? airtight))
            _airtightSystem.SetAirblocked((uid, airtight), collidable);

        // Pathfinding / AI stuff.
        RaiseLocalEvent(new AccessReaderChangeEvent(uid, collidable));

        base.SetCollidable(uid, collidable, door, physics, occluder);
    }

    protected override void OnAfterPry(EntityUid uid, DoorComponent door, ref PriedEvent args)
    {
        base.OnAfterPry(uid, door, ref args);

        if (args.Tool is { } tool && HasComp<PryingComponent>(tool))
            _particles.Spawn(uid, "HrtMetalChips", cooldown: TimeSpan.FromMilliseconds(100));
    }

    private void OnBoltPowerChanged(Entity<DoorBoltComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered)
        {
            if (ent.Comp.BoltWireCut)
                SetBoltsDown(ent, true);
        }

        ent.Comp.Powered = args.Powered;
        Dirty(ent, ent.Comp);
        UpdateBoltLightStatus(ent);
    }
}
