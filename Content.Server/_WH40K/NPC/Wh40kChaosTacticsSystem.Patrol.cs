using Content.Server.NPC.Pathfinding;
using Robust.Shared.Random;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    [Dependency] private IRobustRandom _random = default!;

    // Idle navigation may open ordinary doors, but must not damage or force obstacles.
    public const PathFlags PatrolFlags = PathFlags.Interact;

    private void InitializePatrol()
    {
        SubscribeLocalEvent<Wh40kChaosTacticsComponent, MapInitEvent>(OnPatrolMapInit);
    }

    private void OnPatrolMapInit(EntityUid uid, Wh40kChaosTacticsComponent comp, MapInitEvent args)
    {
        var xform = Transform(uid);
        if (xform.MapUid == null)
            return;

        // Keep the spawn point relative to the ship, including after the ship moves or rotates.
        comp.PatrolOrigin = _transform.WithEntityId(xform.Coordinates, xform.GridUid ?? xform.MapUid.Value);
        DelayPatrol(comp);
    }

    public void DelayPatrol(Wh40kChaosTacticsComponent comp)
    {
        comp.NextPatrol = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(8f, 15f));
    }
}
