using Content.Server.Stack;
using Content.Server._WH40K.ItemRarity;
using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared._NF.Item;
using Content.Shared.Armor;
using Content.Shared.Durability.Components;
using Content.Shared.Durability.Events;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.IntegrationTests.Tests._WH40K.ItemRarity;

[TestFixture]
public sealed class ItemRarityStage3Test
{
    private static readonly string[] RarityIds =
    [
        ItemRarityPrototypeIds.Stamped,
        ItemRarityPrototypeIds.Consecrated,
        ItemRarityPrototypeIds.MasterCrafted,
        ItemRarityPrototypeIds.Relic,
        ItemRarityPrototypeIds.OmnissiahShrine,
        ItemRarityPrototypeIds.Archeotech,
    ];

    [TestPrototypes]
    private const string TestPrototypes = @"
- type: entity
  id: ItemRarityStage3RollItem
  parent: BaseItem
  components:
  - type: ItemRarityRandom
    maxTier: 3

- type: entity
  id: ItemRarityStage3DirectItem
  parent: BaseItem
  components:
  - type: ItemRarityRandom
    maxTier: 3
    randomizeOnDirectSpawn: true

- type: entity
  id: ItemRarityStage3StackItem
  parent: WH40KArtifactSquare
  components:
  - type: ItemRarity
    rarity: Consecrated
    bonusPercent: 10
    isRolled: true
  - type: Stack
    count: 4

- type: entity
  id: ItemRarityStage3StackItemOtherRarity
  parent: WH40KArtifactSquare
  components:
  - type: ItemRarity
    rarity: Consecrated
    bonusPercent: 9
    isRolled: true
  - type: Stack
    count: 4
";

    [Test]
    public async Task RarityThresholdsAndDistributionMatchBalanceTable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var raritySystem = server.System<ItemRarityRandomizationSystem>();

        await server.WaitAssertion(() =>
        {
            var counts = RarityIds.ToDictionary(id => id, _ => 0);
            const int rolls = 10_000;
            for (var i = 0; i < rolls; i++)
            {
                var normalizedRoll = (i + 0.5f) / rolls;
                var rarity = raritySystem.SelectRarityForRoll(normalizedRoll, 6);
                Assert.That(rarity, Is.Not.Null);
                counts[rarity!.ID]++;
            }

            Assert.Multiple(() =>
            {
                Assert.That(counts[ItemRarityPrototypeIds.Stamped], Is.EqualTo(9_000));
                Assert.That(counts[ItemRarityPrototypeIds.Consecrated], Is.EqualTo(800));
                Assert.That(counts[ItemRarityPrototypeIds.MasterCrafted], Is.EqualTo(180));
                Assert.That(counts[ItemRarityPrototypeIds.Relic], Is.EqualTo(17));
                Assert.That(counts[ItemRarityPrototypeIds.OmnissiahShrine], Is.EqualTo(2));
                Assert.That(counts[ItemRarityPrototypeIds.Archeotech], Is.EqualTo(1));
                Assert.That(raritySystem.SelectRarityForRoll(0.99995f, 3)!.ID,
                    Is.EqualTo(ItemRarityPrototypeIds.MasterCrafted));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProfileRollsOnlyFromRandomLootAndIsIdempotent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var direct = server.EntMan.SpawnEntity("ItemRarityStage3RollItem", MapCoordinates.Nullspace);
            Assert.That(server.EntMan.HasComponent<ItemRarityComponent>(direct), Is.False);

            // Once picked up, the server-persisted flag must stay set even if
            // the item is later dropped back into the world.
            server.EntMan.EventBus.RaiseLocalEvent(direct, new RandomLootSpawnedEvent());
            var rarity = server.EntMan.GetComponent<ItemRarityComponent>(direct);
            server.EntMan.EventBus.RaiseLocalEvent(direct, new PickedUpEvent(EntityUid.Invalid, direct));
            Assert.That(rarity.WorldEffectSuppressed, Is.True);
            var firstRarity = rarity.Rarity;
            var firstBonus = rarity.BonusPercent;
            Assert.That(prototypeManager.Index(rarity.Rarity).Tier, Is.LessThanOrEqualTo(3));

            server.EntMan.EventBus.RaiseLocalEvent(direct, new RandomLootSpawnedEvent());
            Assert.Multiple(() =>
            {
                Assert.That(rarity.IsRolled, Is.True);
                Assert.That(rarity.Rarity, Is.EqualTo(firstRarity));
                Assert.That(rarity.BonusPercent, Is.EqualTo(firstBonus));
            });

            var directTestItem = server.EntMan.SpawnEntity("ItemRarityStage3DirectItem", MapCoordinates.Nullspace);
            Assert.That(server.EntMan.GetComponent<ItemRarityComponent>(directTestItem).IsRolled, Is.True);

            var visualSample = server.EntMan.SpawnEntity("ItemRaritySampleRelic", MapCoordinates.Nullspace);
            var visualRarity = server.EntMan.GetComponent<ItemRarityComponent>(visualSample);
            Assert.Multiple(() =>
            {
                Assert.That(visualRarity.Rarity, Is.EqualTo(ItemRarityPrototypeIds.Relic));
                Assert.That(visualRarity.IsRolled, Is.False);
                Assert.That(server.EntMan.HasComponent<ItemRarityStatsComponent>(visualSample), Is.False);
            });

            var fixedStatsSample = server.EntMan.SpawnEntity(
                "ItemRarityStatsSampleArcheotech",
                MapCoordinates.Nullspace);
            var fixedArmor = server.EntMan.GetComponent<ArmorComponent>(fixedStatsSample);
            var fixedDurability = server.EntMan.GetComponent<ItemDurabilityComponent>(fixedStatsSample);
            var fixedStats = server.EntMan.GetComponent<ItemRarityStatsComponent>(fixedStatsSample);
            Assert.Multiple(() =>
            {
                Assert.That(fixedStats.Applied, Is.True);
                Assert.That(fixedStats.BaseArmorRating, Is.EqualTo(25f));
                Assert.That(fixedArmor.ArmorRating, Is.EqualTo(62.5f));
                Assert.That(fixedDurability.MaxDurability, Is.EqualTo(900f));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SplitStackPreservesRarityAndDifferentLotsDoNotMerge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var stackSystem = server.System<StackSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var source = server.EntMan.SpawnEntity("ItemRarityStage3StackItem", MapCoordinates.Nullspace);
            var sourceRarity = server.EntMan.GetComponent<ItemRarityComponent>(source);
            var sourceStack = server.EntMan.GetComponent<StackComponent>(source);
            var sourceCoordinates = server.EntMan.GetComponent<TransformComponent>(source).Coordinates;
            server.EntMan.EventBus.RaiseLocalEvent(source, new PickedUpEvent(EntityUid.Invalid, source));
            var split = stackSystem.Split(source, 1, sourceCoordinates);

            Assert.That(split, Is.Not.Null);
            var splitRarity = server.EntMan.GetComponent<ItemRarityComponent>(split!.Value);
            var splitStats = server.EntMan.GetComponent<ItemRarityStatsComponent>(split.Value);
            Assert.Multiple(() =>
            {
                Assert.That(sourceStack.Count, Is.EqualTo(3));
                Assert.That(splitRarity.Rarity, Is.EqualTo(sourceRarity.Rarity));
                Assert.That(splitRarity.BonusPercent, Is.EqualTo(sourceRarity.BonusPercent));
                Assert.That(splitRarity.IsRolled, Is.True);
                Assert.That(splitRarity.WorldEffectSuppressed, Is.True);
                Assert.That(splitStats.Applied, Is.True);
            });

            var sameLot = server.EntMan.SpawnEntity("ItemRarityStage3StackItem", testMap.GridCoords);
            var differentLot = server.EntMan.SpawnEntity("ItemRarityStage3StackItemOtherRarity", testMap.GridCoords);
            var sameLotRarity = server.EntMan.GetComponent<ItemRarityComponent>(sameLot);
            var differentLotRarity = server.EntMan.GetComponent<ItemRarityComponent>(differentLot);
            Assert.That(sameLotRarity.BonusPercent, Is.Not.EqualTo(differentLotRarity.BonusPercent));

            // The compatibility check is exercised through the same public
            // merge path used by floor-contact stack merging.
            Assert.That(stackSystem.TryMergeToContacts(differentLot), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
