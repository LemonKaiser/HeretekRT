using System.Numerics;
using Content.Server.Projectiles;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Reflection;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class ProjectileCompatibilityTest
{
    [TestPrototypes]
    public const string Prototypes = """
        - type: entity
          parent: BaseBullet
          id: TestCompatibilityProjectile
          components:
          - type: Projectile
            deleteOnCollide: false
            damage:
              types:
                Piercing: 1
        """;

    [Test]
    public async Task RaycastRestoresVelocityOnTranslatingAndRotatingGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var physics = server.System<SharedPhysicsSystem>();
            physics.SetLinearVelocity(map.Grid, new Vector2(30, 10));
            physics.SetAngularVelocity(map.Grid, 1f);
            var bullet = entities.SpawnEntity("TestCompatibilityProjectile", new EntityCoordinates(map.Grid, 2.5f, 1.5f));
            var projectile = entities.GetComponent<ProjectileComponent>(bullet);
            var expected = new Vector2(20, 0);
            physics.SetLinearVelocity(bullet, expected);
            projectile.RaycastResetVelocity = physics.GetMapLinearVelocity(bullet);
            physics.SetLinearVelocity(bullet, new Vector2(2, 0));
            server.System<ProjectileSystem>().Update(1f / 60);
            var actual = entities.GetComponent<PhysicsComponent>(bullet).LinearVelocity;
            Assert.That(Vector2.Distance(actual, expected), Is.LessThan(0.0001f));
            Assert.That(projectile.RaycastResetVelocity, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpentProjectileDropsCachedRaycastVelocity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var bullet = server.EntMan.SpawnEntity("TestCompatibilityProjectile", map.GridCoords);
            var projectile = server.EntMan.GetComponent<ProjectileComponent>(bullet);
            projectile.ProjectileSpent = true;
            projectile.RaycastResetVelocity = Vector2.One;
            server.System<ProjectileSystem>().Update(1f / 60);
            Assert.That(projectile.RaycastResetVelocity, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CancelledHeretekImpactDoesNotSpendProjectile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var bullet = entities.SpawnEntity("TestCompatibilityProjectile", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords);
            var projectile = entities.GetComponent<ProjectileComponent>(bullet);
            var observer = server.System<ProjectileCompatibilityTestSystem>();
            observer.ProtectedTarget = target;
            try
            {
                var result = server.System<ProjectileSystem>().ProjectileCollide(
                    (bullet, projectile, entities.GetComponent<PhysicsComponent>(bullet)), target, null);
                Assert.That(result, Is.Null);
                Assert.That(projectile.ProjectileSpent, Is.False);
                Assert.That(projectile.ImpactCancelled, Is.False);
                Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            }
            finally
            {
                observer.ProtectedTarget = null;
            }
        });
        await pair.CleanReturnAsync();
    }
}

[Reflect(false)]
public sealed class ProjectileCompatibilityTestSystem : EntitySystem
{
    public EntityUid? ProtectedTarget;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageableComponent, ProjectileImpactAttemptEvent>(OnImpact);
    }

    private void OnImpact(Entity<DamageableComponent> ent, ref ProjectileImpactAttemptEvent args)
    {
        if (ent.Owner == ProtectedTarget)
            args.Cancelled = true;
    }
}
