using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared._WH40K.SectorMap.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WH40K.SectorMap;

/// <summary>
/// Contains movers after each physics step on both sides of prediction, using the same perimeter
/// as the red overlay. Server-side loose entities and grids also reuse this correction geometry.
/// </summary>
public sealed partial class SharedKoronusPlanetSurfaceBoundarySystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterSolve);
    }

    private void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent args)
    {
        var query = EntityQueryEnumerator<InputMoverComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var physics, out var transform))
        {
            if (args.Prediction && !physics.Predict ||
                transform.Anchored ||
                transform.MapUid is not { } map ||
                !TryComp<KoronusPlanetSurfaceBoundaryComponent>(map, out var boundary) ||
                transform.ParentUid != map && transform.ParentUid != transform.GridUid)
            {
                continue;
            }

            // Includes walkers on authored grids, vehicles and generated terrain, while leaving
            // inventory/container contents attached to their carrier.
            ClampEntity(uid, transform, new Box2(boundary.Minimum, boundary.Maximum), physics);
        }
    }

    public void ClampEntity(EntityUid uid, TransformComponent transform, Box2 bounds, PhysicsComponent? physics = null)
    {
        var position = _transform.GetWorldPosition(transform);
        physics ??= CompOrNull<PhysicsComponent>(uid);
        var entityBounds = physics != null && TryComp<FixturesComponent>(uid, out var fixtures)
            ? _physics.GetWorldAABB(uid, fixtures, physics, transform)
            : new Box2(position, position);
        var correction = GetBoundsCorrection(entityBounds, bounds);
        if (correction == Vector2.Zero)
            return;

        _transform.SetWorldPosition((uid, transform), position + correction);
        if (physics == null)
            return;

        // Block outward motion, keeping movement along the edge and back into the map usable.
        var velocity = physics.LinearVelocity;
        if (correction.X * velocity.X < 0f)
            velocity.X = 0f;
        if (correction.Y * velocity.Y < 0f)
            velocity.Y = 0f;
        _physics.SetLinearVelocity(uid, velocity, body: physics);
        _physics.SetAngularVelocity(uid, 0f, body: physics);
    }

    public static Vector2 GetBoundsCorrection(Box2 value, Box2 bounds)
    {
        var x = value.Width > bounds.Width
            ? bounds.Center.X - value.Center.X
            : value.Left < bounds.Left
                ? bounds.Left - value.Left
                : value.Right > bounds.Right
                    ? bounds.Right - value.Right
                    : 0f;
        var y = value.Height > bounds.Height
            ? bounds.Center.Y - value.Center.Y
            : value.Bottom < bounds.Bottom
                ? bounds.Bottom - value.Bottom
                : value.Top > bounds.Top
                    ? bounds.Top - value.Top
                    : 0f;
        return new Vector2(x, y);
    }
}
