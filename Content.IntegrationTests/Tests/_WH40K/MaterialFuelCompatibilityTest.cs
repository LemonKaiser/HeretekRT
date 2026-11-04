using Content.Server.Materials;
using Content.Server.Stack;
using Content.Shared._Mono.Fuel;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class MaterialFuelCompatibilityTest
{
    [TestPrototypes]
    public const string Prototypes = """
        - type: entity
          id: TestFuelLimitedStorage
          components:
          - type: Appearance
          - type: MaterialStorage
            storageLimit: 200
        """;

    [Test]
    public async Task OrdinaryMaterialStillInsertsWithoutFuelComponent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var receiver = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<AppearanceComponent>(receiver);
            entities.AddComponent<MaterialStorageComponent>(receiver);
            var steel = entities.SpawnEntity("SheetSteel1", map.GridCoords);
            var amount = entities.GetComponent<PhysicalCompositionComponent>(steel).MaterialComposition["Steel"];
            var storage = server.System<MaterialStorageSystem>();
            Assert.That(storage.TryInsertMaterialEntity(receiver, steel, receiver), Is.True);
            Assert.That(storage.GetMaterialAmount(receiver, "Steel"), Is.EqualTo(amount));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PartialInsertionConservesUnburntMaterialAndRespectsCapacity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var wood = entities.SpawnEntity("MaterialWoodPlank", map.GridCoords);
            var fuel = entities.GetComponent<MaterialFuelComponent>(wood);
            var material = fuel.Material!.Value.Id;
            var full = fuel.BaseMaterialComposition;
            var composition = entities.GetComponent<PhysicalCompositionComponent>(wood);
            server.System<MaterialFuelSystem>().SetRemainingMaterial((wood, fuel), full / 2);
            server.System<StackSystem>().SetCount(wood, 3);

            var receiver = entities.SpawnEntity("TestFuelLimitedStorage", map.GridCoords);
            Assert.That(entities.GetComponent<MaterialStorageComponent>(receiver).StorageLimit, Is.EqualTo(full * 2));
            var storage = server.System<MaterialStorageSystem>();
            Assert.That(storage.TryInsertMaxPossibleMaterialEntity(receiver, wood, receiver, out var empty), Is.True);
            Assert.That(empty, Is.False);
            Assert.That(storage.GetMaterialAmount(receiver, material), Is.EqualTo(full + full / 2));
            Assert.That(entities.GetComponent<StackComponent>(wood).Count, Is.EqualTo(1));
            Assert.That(composition.MaterialComposition[material], Is.EqualTo(full));

            var secondReceiver = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<AppearanceComponent>(secondReceiver);
            entities.AddComponent<MaterialStorageComponent>(secondReceiver);
            Assert.That(storage.TryInsertMaxPossibleMaterialEntity(secondReceiver, wood, secondReceiver, out empty), Is.True);
            Assert.That(empty, Is.True);
            Assert.That(storage.GetMaterialAmount(receiver, material) + storage.GetMaterialAmount(secondReceiver, material), Is.EqualTo(full * 2 + full / 2));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SplittingPreservesConsumedFuelAndMergingCannotRestoreIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var stacks = server.System<StackSystem>();
            var wood = entities.SpawnEntity("MaterialWoodPlank", map.GridCoords);
            var fuel = entities.GetComponent<MaterialFuelComponent>(wood);
            var material = fuel.Material!.Value.Id;
            var full = fuel.BaseMaterialComposition;
            var composition = entities.GetComponent<PhysicalCompositionComponent>(wood);
            server.System<MaterialFuelSystem>().SetRemainingMaterial((wood, fuel), full / 2);
            stacks.SetCount(wood, 3);
            var split = stacks.Split(wood, 1, map.GridCoords);
            Assert.That(split, Is.Not.Null);
            Assert.That(composition.MaterialComposition[material], Is.EqualTo(full));
            Assert.That(entities.GetComponent<PhysicalCompositionComponent>(split!.Value).MaterialComposition[material], Is.EqualTo(full / 2));
            Assert.That(stacks.TryAdd(split.Value, wood), Is.False);
            Assert.That(stacks.TryAdd(wood, split.Value), Is.False);

            var fresh = entities.SpawnEntity("MaterialWoodPlank1", map.GridCoords);
            Assert.That(stacks.TryAdd(fresh, wood), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
