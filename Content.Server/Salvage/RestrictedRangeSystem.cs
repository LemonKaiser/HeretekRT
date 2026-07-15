using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Physics;
using Content.Shared.Salvage;
using Robust.Shared.Map;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Salvage;

public sealed partial class RestrictedRangeSystem : SharedRestrictedRangeSystem
{
    private const int BoundarySegments = 16;

    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RestrictedRangeComponent, MapInitEvent>(OnRestrictedMapInit);
    }

    private void OnRestrictedMapInit(EntityUid uid, RestrictedRangeComponent component, MapInitEvent args)
    {
        component.BoundaryEntity = CreateBoundary(new EntityCoordinates(uid, component.Origin), component.Range);
    }

    public EntityUid CreateBoundary(EntityCoordinates coordinates, float range)
    {
        var boundaryUid = Spawn(null, coordinates);
        var boundaryPhysics = AddComp<PhysicsComponent>(boundaryUid);
        var radius = range + 0.25f;
        var vertices = new Vector2[BoundarySegments];
        var angleStep = MathF.Tau / BoundarySegments;

        // Keep the original clockwise winding so the solid side faces into the playable area.
        // Each edge must be its own fixture: Robust contacts are keyed by Fixture, while a
        // ChainShape exposes several broadphase proxies that can report the same fixture pair.
        for (var i = 0; i < vertices.Length; i++)
        {
            var angle = -angleStep * i;
            vertices[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        var collisionLayer = (int) (CollisionGroup.HighImpassable | CollisionGroup.Impassable | CollisionGroup.LowImpassable);
        for (var i = 0; i < vertices.Length; i++)
        {
            var edge = new EdgeShape();
            edge.SetOneSided(
                vertices[(i - 1 + vertices.Length) % vertices.Length],
                vertices[i],
                vertices[(i + 1) % vertices.Length],
                vertices[(i + 2) % vertices.Length]);
            _fixtures.TryCreateFixture(
                boundaryUid,
                edge,
                $"boundary-{i}",
                collisionLayer: collisionLayer,
                updates: false,
                body: boundaryPhysics);
        }

        _fixtures.FixtureUpdate(boundaryUid, body: boundaryPhysics);
        _physics.WakeBody(boundaryUid, body: boundaryPhysics);
        AddComp<BoundaryComponent>(boundaryUid);
        return boundaryUid;
    }
}
