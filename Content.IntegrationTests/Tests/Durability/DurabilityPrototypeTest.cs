using Content.Server.Durability;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Durability;
using Content.Shared.Durability.Components;
using Content.Shared.Durability.Events;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Armor;
using Content.Shared.Stacks;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Tools;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Utility;
using System.Collections.Generic;
using System.Linq;

namespace Content.IntegrationTests.Tests.Durability;

[TestFixture]
public sealed class DurabilityPrototypeTest
{
    private static readonly ProtoId<StackPrototype> SteelStack = "Steel";
    private static readonly ProtoId<ToolQualityPrototype> WeldingToolQuality = "Welding";

    [TestPrototypes]
    private const string TestPrototypes = @"
- type: entity
  id: DurabilityTestRepairableItem
  components:
  - type: ItemDurability
    maxDurability: 10
    destroyAtZero: false

- type: entity
  id: DurabilityTestDisposableItem
  components:
  - type: ItemDurability
    maxDurability: 1

- type: entity
  id: DurabilityTestExamineItem
  name: repair examine test weapon
  components:
  - type: ItemDurability
    maxDurability: 10
    destroyAtZero: false
    requiredWorkbenchTier: 3
    repairMaterial: Steel

- type: entity
  id: DurabilityTestArmor
  components:
  - type: ItemDurability
    maxDurability: 100
    destroyAtZero: false
    protectsWearer: true
    protectedBodyParts: Torso
    armorDamageDrainMultiplier: 0.5

- type: entity
  id: DurabilityTestRatingArmor
  components:
  - type: Item
  - type: Clothing
    slots: [outerclothing]
  - type: Armor
    armorRating: 200
    protectedBodyParts: Torso
    modifiers:
      coefficients:
        Slash: 1
        Heat: 1
  - type: ItemDurability
    maxDurability: 100
    destroyAtZero: false
    protectsWearer: true
    protectedBodyParts: Torso

- type: entity
  id: DurabilityTestResistanceArmor
  components:
  - type: Item
  - type: Clothing
    slots: [innerclothing]
  - type: Armor
    armorRating: 0
    protectedBodyParts: Torso
    modifiers:
      coefficients:
        Slash: 0.5
        Heat: 0.5
  - type: ItemDurability
    maxDurability: 100
    destroyAtZero: false
    protectsWearer: true
    protectedBodyParts: Torso
";

    [Test]
    public async Task ItemsWithUseOrProtectionComponentsHaveDurability()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        var itemName = componentFactory.GetComponentName<ItemComponent>();
        var durabilityName = componentFactory.GetComponentName<ItemDurabilityComponent>();
        var relevantComponents = new[]
        {
            "Gun",
            "Tool",
            "Blocking",
            "Armor",
        };

        await server.WaitAssertion(() =>
        {
            var missing = new List<string>();
            foreach (var prototype in prototypeManager.EnumeratePrototypes<EntityPrototype>())
            {
                if (prototype.Abstract
                    || pair.IsTestPrototype(prototype)
                    || !prototype.Components.ContainsKey(itemName)
                    || prototype.Components.ContainsKey("GasTank")
                    || prototype.Components.ContainsKey("Drink")
                    || prototype.Components.ContainsKey("Food")
                    || prototype.Components.ContainsKey("Utensil")
                    || prototype.ID.Contains("Debug", StringComparison.OrdinalIgnoreCase)
                    || prototype.ID.Contains("Omnigun", StringComparison.OrdinalIgnoreCase)
                    || !relevantComponents.Any(prototype.Components.ContainsKey))
                {
                    continue;
                }

                // Armor is also used for explosion resistance on technical entities. Only clothing armor
                // represents wearable protection and is covered by this invariant.
                if (prototype.Components.ContainsKey("Armor") && !prototype.Components.ContainsKey("Clothing"))
                    continue;

                if (prototype.Components.TryGetComponent("Armor", out var armorRegistration)
                    && armorRegistration is ArmorComponent armor
                    && armor.Modifiers.Coefficients.Count == 0
                    && armor.Modifiers.FlatReduction.Count == 0)
                {
                    continue;
                }

                if (!prototype.Components.ContainsKey(durabilityName))
                {
                    var matched = string.Join(", ", relevantComponents.Where(prototype.Components.ContainsKey));
                    missing.Add($"{prototype.ID} [{matched}]");
                }
            }

            Assert.That(missing, Is.Empty,
                $"Prototypes with usable/protective components but no ItemDurability:\n{string.Join("\n", missing)}");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DurabilityProfilesHaveValidDefaults()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();
        var durabilityName = componentFactory.GetComponentName<ItemDurabilityComponent>();
        var expectedMaxDurability = new Dictionary<string, float>
        {
            ["WeaponCrusher"] = 500f,
            ["BaseBallBat"] = 350f,
            ["RollingPin"] = 400f,
            ["WHOmnissianAxe"] = 400f,
            ["Chainsword"] = 500f,
            ["WeaponStubRifle"] = 120f,
            ["WH40KBible"] = 400f,
            ["ClothingOuterArmorFlakVest"] = 360f,
            ["ClothingOuterArmorFlakVestLight"] = 240f,
            ["ClothingOuterArmorAdvancedFlakVest"] = 480f,
            ["ClothingOuterHardsuitVoidsmanStandard"] = 480f,
            ["ClothingOuterArmorCommissar"] = 360f,
            ["ClothingOuterArmorSeniorOfficerArmoredJacket"] = 360f,
            ["ClothingOuterArmorAstartesMk7"] = 800f,
            ["ClothingHeadHelmetFlak"] = 160f,
            ["ClothingHeadHelmetFlakAdv"] = 200f,
            ["ClothingHeadHelmetHardsuitVoidsmanStandard"] = 240f,
            ["ClothingHeadHelmetAstartesMk7"] = 400f,
        };

        await server.WaitAssertion(() =>
        {
            foreach (var prototype in prototypeManager.EnumeratePrototypes<EntityPrototype>())
            {
                if (!prototype.Components.TryGetComponent(durabilityName, out var registration))
                    continue;

                var durability = (ItemDurabilityComponent) registration;
                Assert.That(durability.MaxDurability, Is.GreaterThan(0f), prototype.ID);
                Assert.That(durability.MaxDurability, Is.EqualTo(DurabilityMath.Round(durability.MaxDurability)), prototype.ID);
                Assert.That(durability.ShotDrain, Is.GreaterThanOrEqualTo(0f), prototype.ID);
                Assert.That(durability.MeleeDrain, Is.GreaterThanOrEqualTo(0f), prototype.ID);
                Assert.That(durability.ToolUseDrain, Is.GreaterThanOrEqualTo(0f), prototype.ID);
                Assert.That(durability.IncomingDamageDrain, Is.GreaterThanOrEqualTo(0f), prototype.ID);
                Assert.That(durability.ArmorDamageDrainMultiplier, Is.GreaterThanOrEqualTo(0f), prototype.ID);
            }

            foreach (var (prototypeId, expectedMaximum) in expectedMaxDurability)
            {
                var prototype = prototypeManager.Index<EntityPrototype>(prototypeId);
                Assert.That(prototype.Components.TryGetComponent(durabilityName, out var registration), Is.True, prototypeId);
                var durability = (ItemDurabilityComponent) registration!;
                Assert.That(durability.MaxDurability, Is.EqualTo(expectedMaximum), prototypeId);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RepairWorkbenchesHaveExpectedTierConfiguration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var expected = new Dictionary<string, (int Tier, float PerMaterial, float Duration, bool RequirePower)>
        {
            ["WH40KDurabilityRepairWorkbenchT1"] = (1, 10f, 1f, false),
            ["WH40KDurabilityRepairWorkbenchT2"] = (2, 15f, 0.5f, false),
            ["WH40KDurabilityRepairWorkbenchT3"] = (3, 20f, 0.25f, true),
        };

        await server.WaitAssertion(() =>
        {
            foreach (var (prototypeId, values) in expected)
            {
                var uid = server.EntMan.SpawnEntity(prototypeId, MapCoordinates.Nullspace);
                var station = server.EntMan.GetComponent<DurabilityRepairStationComponent>(uid);
                var slots = server.EntMan.GetComponent<ItemSlotsComponent>(uid);
                var repairSlot = slots.Slots[DurabilityRepairStationComponent.RepairSlotId];

                Assert.Multiple(() =>
                {
                    Assert.That(station.Tier, Is.EqualTo(values.Tier), prototypeId);
                    Assert.That(station.MaxRepairSlots, Is.EqualTo(1), prototypeId);
                    Assert.That(station.RepairPerMaterial, Is.EqualTo(values.PerMaterial), prototypeId);
                    Assert.That(station.RepairDurationSeconds, Is.EqualTo(values.Duration), prototypeId);
                    Assert.That(station.RequirePower, Is.EqualTo(values.RequirePower), prototypeId);
                    Assert.That(slots.Slots, Has.Count.EqualTo(1), prototypeId);
                    Assert.That(repairSlot.ContainerSlot, Is.Not.Null, prototypeId);
                    Assert.That(repairSlot.ContainerSlot!.ShowContents, Is.True, prototypeId);
                    Assert.That(repairSlot.ContainerSlot.OccludesLight, Is.False, prototypeId);
                    Assert.That(server.EntMan.HasComponent<Content.Shared.Placeable.ItemPlacerComponent>(uid), Is.False,
                        prototypeId);
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RepairWorkbenchExamineExplainsSlottedItemRequirements()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var localization = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var station = server.EntMan.SpawnEntity("WH40KDurabilityRepairWorkbenchT2", MapCoordinates.Nullspace);
            var item = server.EntMan.SpawnEntity("DurabilityTestExamineItem", MapCoordinates.Nullspace);
            var slots = server.EntMan.GetComponent<ItemSlotsComponent>(station);
            var repairSlot = slots.Slots[DurabilityRepairStationComponent.RepairSlotId].ContainerSlot;

            Assert.That(repairSlot, Is.Not.Null);
            Assert.That(server.EntMan.System<SharedContainerSystem>().Insert(item, repairSlot!), Is.True);

            Assert.That(localization.HasString("durability-examine"), Is.True);
            Assert.That(localization.HasString("durability-repair-station-examine"), Is.True);

            var examine = new ExaminedEvent(
                new FormattedMessage(),
                station,
                EntityUid.Invalid,
                isInDetailsRange: true,
                hasDescription: false);
            server.EntMan.EventBus.RaiseLocalEvent(station, examine);

            var steel = localization.GetString(prototypeManager.Index(SteelStack).Name);
            var welder = localization.GetString(prototypeManager.Index(WeldingToolQuality).ToolName);
            var markup = examine.GetTotalMessage().ToMarkup();

            Assert.Multiple(() =>
            {
                Assert.That(markup, Does.Contain("repair examine test weapon"));
                Assert.That(markup, Does.Contain("T3"));
                Assert.That(markup, Does.Contain(steel));
                Assert.That(markup, Does.Contain(welder));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArmorWearUsesAbsorbedDamageAndCoverage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var armor = server.EntMan.SpawnEntity("DurabilityTestArmor", MapCoordinates.Nullspace);
            var durability = server.EntMan.GetComponent<ItemDurabilityComponent>(armor);

            server.EntMan.EventBus.RaiseLocalEvent(armor,
                new ArmorProtectionAppliedEvent(TargetBodyPart.Head, 20f));
            Assert.That(durability.CurrentDurability, Is.EqualTo(100f));

            server.EntMan.EventBus.RaiseLocalEvent(armor,
                new ArmorProtectionAppliedEvent(TargetBodyPart.Torso, 12.34f));
            Assert.That(durability.CurrentDurability, Is.EqualTo(93.8f));

            server.EntMan.EventBus.RaiseLocalEvent(armor,
                new ArmorProtectionAppliedEvent(TargetBodyPart.Torso, 0f));
            Assert.That(durability.CurrentDurability, Is.EqualTo(93.8f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArmorWearUsesOnlyTheLayerThatActuallyProtectedTheRootHit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            var damageable = server.System<DamageableSystem>();

            var heatWearer = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var heatRatingArmor = server.EntMan.SpawnEntity("DurabilityTestRatingArmor", testMap.GridCoords);
            var heatResistanceArmor = server.EntMan.SpawnEntity("DurabilityTestResistanceArmor", testMap.GridCoords);
            Assert.That(inventory.TryEquip(heatWearer, heatResistanceArmor, "jumpsuit", force: true), Is.True);
            Assert.That(inventory.TryEquip(heatWearer, heatRatingArmor, "outerClothing", force: true), Is.True);

            var heatDamage = new DamageSpecifier();
            heatDamage.DamageDict["Heat"] = FixedPoint2.New(100f);
            var heatResult = damageable.TryChangeDamage(
                heatWearer,
                heatDamage,
                targetPart: TargetBodyPart.Torso);

            var heatRatingDurability = server.EntMan.GetComponent<ItemDurabilityComponent>(heatRatingArmor);
            var heatResistanceDurability = server.EntMan.GetComponent<ItemDurabilityComponent>(heatResistanceArmor);
            Assert.Multiple(() =>
            {
                Assert.That(heatResult, Is.Not.Null);
                Assert.That(heatResult!.DamageDict["Heat"], Is.EqualTo(FixedPoint2.New(50f)));
                Assert.That(heatRatingDurability.CurrentDurability, Is.EqualTo(100f));
                Assert.That(heatResistanceDurability.CurrentDurability, Is.EqualTo(50f));
            });

            var slashWearer = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var slashRatingArmor = server.EntMan.SpawnEntity("DurabilityTestRatingArmor", testMap.GridCoords);
            var slashResistanceArmor = server.EntMan.SpawnEntity("DurabilityTestResistanceArmor", testMap.GridCoords);
            Assert.That(inventory.TryEquip(slashWearer, slashResistanceArmor, "jumpsuit", force: true), Is.True);
            Assert.That(inventory.TryEquip(slashWearer, slashRatingArmor, "outerClothing", force: true), Is.True);

            var slashDamage = new DamageSpecifier();
            slashDamage.DamageDict["Slash"] = FixedPoint2.New(100f);
            var slashResult = damageable.TryChangeDamage(
                slashWearer,
                slashDamage,
                targetPart: TargetBodyPart.Torso);

            var slashRatingDurability = server.EntMan.GetComponent<ItemDurabilityComponent>(slashRatingArmor);
            var slashResistanceDurability = server.EntMan.GetComponent<ItemDurabilityComponent>(slashResistanceArmor);
            Assert.Multiple(() =>
            {
                Assert.That(slashResult, Is.Not.Null);
                Assert.That(slashResult!.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(25f)));
                Assert.That(slashRatingDurability.CurrentDurability, Is.EqualTo(25f));
                Assert.That(slashResistanceDurability.CurrentDurability, Is.EqualTo(100f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BrokenArmorDoesNotProtectOrSpendDurability()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            var damageable = server.System<DamageableSystem>();
            var durabilitySystem = server.System<ItemDurabilitySystem>();
            var wearer = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var armor = server.EntMan.SpawnEntity("DurabilityTestResistanceArmor", testMap.GridCoords);
            var durability = server.EntMan.GetComponent<ItemDurabilityComponent>(armor);

            Assert.That(inventory.TryEquip(wearer, armor, "jumpsuit", force: true), Is.True);
            Assert.That(durabilitySystem.TryConsume(
                armor,
                durability.MaxDurability,
                DurabilityReason.IncomingDamage,
                component: durability), Is.True);
            Assert.That(durability.Broken, Is.True);

            var damage = new DamageSpecifier();
            damage.DamageDict["Heat"] = FixedPoint2.New(100f);
            var result = damageable.TryChangeDamage(wearer, damage, targetPart: TargetBodyPart.Torso);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result!.DamageDict["Heat"], Is.EqualTo(FixedPoint2.New(100f)));
                Assert.That(durability.CurrentDurability, Is.Zero);
                Assert.That(durability.Broken, Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsumeRepairAndDepleteUseRoundedServerState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid repairable = default;

        await server.WaitPost(() =>
        {
            var system = server.System<ItemDurabilitySystem>();
            repairable = server.EntMan.SpawnEntity("DurabilityTestRepairableItem", MapCoordinates.Nullspace);
            var component = server.EntMan.GetComponent<ItemDurabilityComponent>(repairable);

            for (var i = 0; i < 30; i++)
                Assert.That(system.TryConsume(repairable, 0.1f, DurabilityReason.Shot, component: component), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(component.CurrentDurability, Is.EqualTo(7f));
                Assert.That(component.Broken, Is.False);
                Assert.That(system.IsUsable(repairable, component), Is.True);
            });

            Assert.That(system.TryRepair(repairable, 100f, component: component), Is.True);
            Assert.That(component.CurrentDurability, Is.EqualTo(10f));

            Assert.That(system.TryConsume(repairable, 10f, DurabilityReason.IncomingDamage, component: component), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(component.CurrentDurability, Is.Zero);
                Assert.That(component.Broken, Is.True);
                Assert.That(system.IsUsable(repairable, component), Is.False);
                Assert.That(server.EntMan.EntityExists(repairable), Is.True);
            });

            Assert.That(system.TryRepair(repairable, 0.1f, component: component), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(component.CurrentDurability, Is.EqualTo(0.1f));
                Assert.That(component.Broken, Is.False);
                Assert.That(system.IsUsable(repairable, component), Is.True);
            });
        });

        EntityUid disposable = default;
        await server.WaitPost(() =>
        {
            var system = server.System<ItemDurabilitySystem>();
            disposable = server.EntMan.SpawnEntity("DurabilityTestDisposableItem", MapCoordinates.Nullspace);
            Assert.That(system.TryConsume(disposable, 1f, DurabilityReason.ToolUse), Is.True);
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() => Assert.That(server.EntMan.Deleted(disposable), Is.True));
        await pair.CleanReturnAsync();
    }
}
