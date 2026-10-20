using System.Linq;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server._WH40K.Combat.PhantomStep;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    [Dependency] private WH40KPhantomStepSystem _phantomStep = default!;
    [Dependency] private ChatSystem _chat = default!;

    private bool DashShotReady(EntityUid uid, Wh40kChaosMarineBrainComponent brain, Wh40kChaosMarineComponent state)
    {
        if (brain.DashPending)
            return !Comp<WH40KPhantomStepComponent>(uid).Dashing;
        if (brain.Target is not { } target || !CanTarget(uid, target)) return true;
        var origin = Local(uid, uid);
        var aim = Local(uid, target);
        var forward = aim - origin;
        if (forward.LengthSquared() < .01f) forward = Vector2.UnitX;
        forward = Vector2.Normalize(forward);
        Vector2[] candidates =
        [
            Wh40kChaosMarineCombat.OrbitDash(origin, aim, brain.OrbitSide),
            brain.DashPrevious ?? Wh40kChaosMarineCombat.OrbitDash(origin, aim, -brain.OrbitSide),
            origin + forward * MathF.Min(3, MathF.Max(0, Vector2.Distance(origin, aim) - 1.1f)),
        ];
        foreach (var destination in candidates)
        {
            if (Vector2.Distance(origin, destination) < .5f || !ValidPoint(uid, state, destination, .5f)) continue;
            if (!CanWalkDirect(uid, Coordinates(state, destination))) continue;
            if (!_phantomStep.TryDashTo(uid, Coordinates(state, destination),
                    TimeSpan.FromSeconds(Wh40kChaosMarineCombat.DashDuration(state)))) continue;
            brain.DashPrevious = origin;
            brain.DashPending = true;
            return false;
        }
        // A completely boxed-in boss still fires; the dash never bypasses a wall.
        return true;
    }

    private void OnShotContact(EntityUid uid, Wh40kMarineShotComponent shot, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != SharedProjectileSystem.ProjectileFixture || !args.OtherFixture.Hard) return;
        shot.ContactNormal = args.WorldNormal;
    }

    private bool TryBounceShot(EntityUid uid, Wh40kMarineShotComponent shot, ProjectileComponent projectile, EntityUid target)
    {
        if (shot.BouncesRemaining <= 0 || projectile.Shooter != shot.Boss || HasComp<MobStateComponent>(target) ||
            !TryComp<PhysicsComponent>(uid, out var body) || !HasComp<PhysicsComponent>(target)) return false;
        var velocity = projectile.RaycastResetVelocity ?? _physics.GetMapLinearVelocity(uid, component: body);
        if (velocity.LengthSquared() < .01f) return false;
        var normal = shot.ContactNormal;
        shot.ContactNormal = Vector2.Zero;
        var position = _transform.GetMapCoordinates(uid);
        var box = _physics.GetWorldAABB(target);
        if (normal.LengthSquared() < .01f || MathF.Abs(Vector2.Dot(normal, velocity)) < velocity.Length() * .05f)
        {
            // Native fast-projectile raycasts can reach the surface before a contact manifold exists.
            normal = SurfaceNormal(box, position.Position, velocity);
        }
        normal = Vector2.Normalize(normal);
        if (Vector2.Dot(normal, velocity) > 0) normal = -normal;
        var reflected = Vector2.Reflect(velocity, normal);
        shot.BouncesRemaining--;
        shot.LastBounce = target;
        shot.BounceNormal = normal;
        var support = new Vector2(normal.X < 0 ? box.Left : box.Right, normal.Y < 0 ? box.Bottom : box.Top);
        // The projectile has an asymmetric AABB; its centre can still be 0.3 tiles off the wall.
        // Keep the surface plane, not that centre, when merging adjacent tile contacts.
        shot.BouncePosition = position.Position + normal * Vector2.Dot(support - position.Position, normal);
        shot.BounceIgnoreUntil = Now + TimeSpan.FromSeconds(.08);
        projectile.ImpactCancelled = true;
        projectile.RaycastResetVelocity = null;
        _transform.SetMapCoordinates(uid, position.Offset(normal * .12f));
        var parentVelocity = _physics.GetMapLinearVelocity(Transform(uid).ParentUid);
        _physics.SetLinearVelocity(uid, reflected - parentVelocity, body: body);
        _transform.SetWorldRotation(uid, reflected.ToWorldAngle());
        if (shot.Boss is { } boss && TryComp<Wh40kChaosMarineComponent>(boss, out var state))
            Effect(boss, state, Local(boss, uid), MarineEffect.Ricochet, .45f, .18f);
        Sound(uid, "/Audio/Weapons/Guns/Hits/ric2.ogg", -5);
        return true;
    }

    private bool SameBounceSurface(Wh40kMarineShotComponent shot, EntityUid target)
    {
        if (shot.LastBounce == null || Now >= shot.BounceIgnoreUntil || HasComp<MobStateComponent>(target)) return false;
        if (shot.LastBounce == target) return true;
        if (!HasComp<PhysicsComponent>(target)) return false;
        // Adjacent wall tiles can report two contacts for one impact on their seam.
        // Only ignore the same outward plane; a perpendicular wall must still stop/reflect the bolt.
        var box = _physics.GetWorldAABB(target);
        var nearest = Vector2.Clamp(shot.BouncePosition, box.BottomLeft, box.TopRight);
        if (Vector2.DistanceSquared(nearest, shot.BouncePosition) > .04f) return false;
        var support = new Vector2(shot.BounceNormal.X < 0 ? box.Left : box.Right,
            shot.BounceNormal.Y < 0 ? box.Bottom : box.Top);
        return MathF.Abs(Vector2.Dot(support - shot.BouncePosition, shot.BounceNormal)) < .06f;
    }

    private static Vector2 SurfaceNormal(Box2 box, Vector2 point, Vector2 incoming)
    {
        (Vector2 Normal, float Distance)[] faces =
        [
            (-Vector2.UnitX, MathF.Abs(point.X - box.Left)),
            (Vector2.UnitX, MathF.Abs(point.X - box.Right)),
            (-Vector2.UnitY, MathF.Abs(point.Y - box.Bottom)),
            (Vector2.UnitY, MathF.Abs(point.Y - box.Top)),
        ];
        return faces.Where(f => Vector2.Dot(f.Normal, incoming) < 0).MinBy(f => f.Distance).Normal;
    }

    private Vector2 BankShotAim(EntityUid uid, EntityUid target)
    {
        var direct = Local(uid, target);
        if (Visible(uid, target)) return direct;
        var state = Comp<Wh40kChaosMarineComponent>(uid);
        var origin = _transform.GetWorldPosition(uid);
        var destination = _transform.GetWorldPosition(target);
        var map = Transform(uid).MapID;
        var walls = new HashSet<EntityUid>();
        for (var i = 0; i < 24; i++)
        {
            var direction = new Angle(i * MathF.Tau / 24).ToVec();
            var hit = _physics.IntersectRay(map,
                new CollisionRay(origin, direction, (int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable)),
                18, uid, false).Where(h => !HasComp<MobStateComponent>(h.HitEntity)).OrderBy(h => h.Distance).FirstOrDefault();
            if (hit.HitEntity.IsValid()) walls.Add(hit.HitEntity);
        }
        var bestLength = float.MaxValue;
        var bestAim = direct;
        foreach (var wall in walls)
        {
            var box = _physics.GetWorldAABB(wall);
            (Vector2 Point, Vector2 Normal, bool Vertical)[] faces =
            [
                (new Vector2(box.Left, box.Center.Y), -Vector2.UnitX, true),
                (new Vector2(box.Right, box.Center.Y), Vector2.UnitX, true),
                (new Vector2(box.Center.X, box.Bottom), -Vector2.UnitY, false),
                (new Vector2(box.Center.X, box.Top), Vector2.UnitY, false),
            ];
            foreach (var (plane, normal, vertical) in faces)
            {
                if (Vector2.Dot(origin - plane, normal) <= .15f || Vector2.Dot(destination - plane, normal) <= .15f)
                    continue;
                var mirror = destination - 2 * Vector2.Dot(destination - plane, normal) * normal;
                var ray = mirror - origin;
                var denominator = Vector2.Dot(ray, normal);
                if (MathF.Abs(denominator) < .001f) continue;
                var fraction = Vector2.Dot(plane - origin, normal) / denominator;
                if (fraction <= 0 || fraction >= 1) continue;
                var bounce = origin + ray * fraction;
                if (vertical ? bounce.Y < box.Bottom + .08f || bounce.Y > box.Top - .08f
                    : bounce.X < box.Left + .08f || bounce.X > box.Right - .08f) continue;
                var localBounce = Vector2.Transform(bounce + normal * .15f, _transform.GetInvWorldMatrix(state.Anchor!.Value));
                if (!ValidPoint(uid, state, localBounce, .2f) ||
                    !ClearLine(uid, Local(uid, uid), localBounce) || !ClearLine(uid, localBounce, direct, target)) continue;
                var length = Vector2.Distance(origin, bounce) + Vector2.Distance(bounce, destination);
                if (length >= bestLength) continue;
                bestLength = length;
                bestAim = Vector2.Transform(mirror, _transform.GetInvWorldMatrix(state.Anchor.Value));
            }
        }
        return bestAim;
    }
}
