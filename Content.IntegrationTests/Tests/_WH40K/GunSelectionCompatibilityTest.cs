using Content.Shared._FinalHorizon.Guns.AlternateGun;
using Content.Shared.Buckle;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class GunSelectionCompatibilityTest
{
    [TestPrototypes]
    public const string Prototypes = """
        - type: entity
          id: TestCompatibilityGun
          components:
          - type: Item
          - type: Gun

        - type: entity
          id: TestCompatibilityAlternateGun
          parent: TestCompatibilityGun
          components:
          - type: AlternateGun
          - type: ItemSlots
            slots:
              launcher:
                startingItem: TestCompatibilityGun

        - type: entity
          id: TestCompatibilityMountedGun
          parent: TestCompatibilityGun
          components:
          - type: Strap
            position: Stand
          - type: BuckleMountedGun
        """;

    [Test]
    public async Task HeldAlternateGunRemainsCompatibleWithExistingCallers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var guns = server.System<SharedGunSystem>();
            var primary = entities.SpawnEntity("TestCompatibilityAlternateGun", map.GridCoords);
            AssertGun(guns, primary, primary);

            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, primary, animate: false), Is.True);
            AssertGun(guns, user, primary);

            var alternate = server.System<ItemSlotsSystem>().GetItemOrNull(primary, "launcher");
            Assert.That(alternate, Is.Not.Null);
            var mode = entities.GetComponent<AlternateGunComponent>(primary);
            mode.Enabled = true;
            entities.Dirty(primary, mode);
            AssertGun(guns, user, alternate!.Value);

            entities.DeleteEntity(alternate.Value);
            AssertGun(guns, user, primary);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MountedGunKeepsItsOperatorAndEnabledStrapChecks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var guns = server.System<SharedGunSystem>();
            var mounted = entities.SpawnEntity("TestCompatibilityMountedGun", map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var buckle = server.System<SharedBuckleSystem>();
            Assert.That(buckle.TryBuckle(user, user, mounted, popup: false), Is.True);
            AssertGun(guns, user, mounted);

            buckle.StrapSetEnabled(mounted, false);
            Assert.That(guns.TryGetGun(user, out _), Is.False);
            Assert.That(guns.TryGetGun(user, out _, out var component), Is.False);
            Assert.That(component, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertGun(SharedGunSystem guns, EntityUid user, EntityUid expected)
    {
        Assert.That(guns.TryGetGun(user, out var gun), Is.True);
        Assert.That(gun.Owner, Is.EqualTo(expected));
        Assert.That(guns.TryGetGun(user, out var legacyGun, out var component), Is.True);
        Assert.That(legacyGun, Is.EqualTo(expected));
        Assert.That(component, Is.SameAs(gun.Comp));
    }
}
