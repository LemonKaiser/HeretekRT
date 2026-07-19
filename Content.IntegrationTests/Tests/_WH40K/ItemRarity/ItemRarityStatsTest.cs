using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared.Armor;
using Content.Shared.Durability.Components;
using Content.Shared.Durability.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WH40K.ItemRarity;

[TestFixture]
public sealed class ItemRarityStatsTest
{
    [TestPrototypes]
    private const string TestPrototypes = @"
- type: entity
  id: ItemRarityStatsTestArmor
  components:
  - type: ItemRarity
    rarity: Consecrated
    bonusPercent: 10
    isRolled: true
  - type: ItemRarityRandom
    maxTier: 3
  - type: Armor
    armorRating: 25
    modifiers:
      coefficients:
        Piercing: 0.8
      flatReductions:
        Piercing: 2
  - type: ItemDurability
    maxDurability: 360
    destroyAtZero: false

- type: entity
  id: ItemRarityStatsTestWeapon
  components:
  - type: ItemRarity
    rarity: Archeotech
    bonusPercent: 200
    isRolled: true
  - type: ItemRarityRandom
    maxTier: 6
    baseWeaponDamageMultiplier: 1
    baseWeaponArmorPenetration: 5
  - type: Gun
    damageModifier: 1
  - type: MeleeWeapon
    armorPenetration: 5
    damage:
      types:
        Piercing: 10
  - type: ItemDurability
    maxDurability: 100
    destroyAtZero: false
";

    [Test]
    public async Task RolledBonusScalesOnlyAllowedStatsOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var armorUid = server.EntMan.SpawnEntity("ItemRarityStatsTestArmor", MapCoordinates.Nullspace);
            var armor = server.EntMan.GetComponent<ArmorComponent>(armorUid);
            var armorDurability = server.EntMan.GetComponent<ItemDurabilityComponent>(armorUid);
            var armorStats = server.EntMan.GetComponent<ItemRarityStatsComponent>(armorUid);

            Assert.Multiple(() =>
            {
                Assert.That(armorStats.Applied, Is.True);
                Assert.That(armorStats.BaseArmorRating, Is.EqualTo(25f));
                Assert.That(armor.ArmorRating, Is.EqualTo(27.5f));
                Assert.That(armorStats.BaseMaxDurability, Is.EqualTo(360f));
                Assert.That(armorDurability.MaxDurability, Is.EqualTo(396f));
                Assert.That(armorDurability.CurrentDurability, Is.EqualTo(396f));
                Assert.That(armor.Modifiers.Coefficients["Piercing"], Is.EqualTo(0.8f));
                Assert.That(armor.Modifiers.FlatReduction["Piercing"], Is.EqualTo(2f));
            });

            server.EntMan.EventBus.RaiseLocalEvent(armorUid, new DurabilityInitializedEvent());
            Assert.Multiple(() =>
            {
                Assert.That(armor.ArmorRating, Is.EqualTo(27.5f));
                Assert.That(armorDurability.MaxDurability, Is.EqualTo(396f));
                Assert.That(armorDurability.CurrentDurability, Is.EqualTo(396f));
            });

            var weaponUid = server.EntMan.SpawnEntity("ItemRarityStatsTestWeapon", MapCoordinates.Nullspace);
            var gun = server.EntMan.GetComponent<GunComponent>(weaponUid);
            var melee = server.EntMan.GetComponent<MeleeWeaponComponent>(weaponUid);
            var weaponStats = server.EntMan.GetComponent<ItemRarityStatsComponent>(weaponUid);

            Assert.Multiple(() =>
            {
                Assert.That(weaponStats.Applied, Is.True);
                Assert.That(weaponStats.BaseWeaponDamageMultiplier, Is.EqualTo(1f));
                Assert.That(weaponStats.EffectiveWeaponDamageMultiplier, Is.EqualTo(3f));
                Assert.That(gun.DamageModifier, Is.EqualTo(3f));
                Assert.That(melee.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(30f)));
                Assert.That(weaponStats.BaseWeaponArmorPenetration, Is.EqualTo(5f));
                Assert.That(weaponStats.EffectiveWeaponArmorPenetration, Is.EqualTo(15f));
                Assert.That(melee.ArmorPenetration, Is.EqualTo(15f));
            });

            server.EntMan.EventBus.RaiseLocalEvent(weaponUid, new DurabilityInitializedEvent());
            Assert.Multiple(() =>
            {
                Assert.That(gun.DamageModifier, Is.EqualTo(3f));
                Assert.That(melee.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(30f)));
                Assert.That(melee.ArmorPenetration, Is.EqualTo(15f));
            });
        });

        await pair.CleanReturnAsync();
    }
}
