using Content.Shared.Buckle.Components;
using Content.Shared.Climbing.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared.Buckle;

public abstract partial class SharedBuckleSystem
{
    /// <summary>
    /// Keeps a seated body from being shoved off its seat by a wall, table, adjacent chair, or bunkmate.
    /// Projectiles, thrown items, and passers-by still collide normally.
    /// </summary>
    private bool Wh40kSeatIgnoresCollision(BuckleComponent component, ref PreventCollideEvent args)
    {
        if (!component.Buckled || !args.OurFixture.Hard || !args.OtherFixture.Hard ||
            args.OtherEntity == component.BuckledTo)
            return false;

        return args.OtherBody.BodyType == BodyType.Static ||
            HasComp<StrapComponent>(args.OtherEntity) ||
            HasComp<ClimbableComponent>(args.OtherEntity) ||
            TryComp<BuckleComponent>(args.OtherEntity, out var other) && other.Buckled;
    }
}
