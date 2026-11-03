using Content.Shared._Mono.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class ShipRepairCompatibilityTest
{
    [TestPrototypes]
    public const string Prototypes = """
        - type: entity
          id: TestBatchRepairTool
          components:
          - type: Item
          - type: ShipRepairTool
            enableTileRepair: false
            repairSound: null
          - type: LimitedCharges
            maxCharges: 10
            charges: 10

        - type: entity
          id: TestBatchRepairPart
          components:
          - type: ShipRepairable
            repairCost: 2
            repairTime: 2
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task RepairBatchValidatesEveryTargetBeforeSpawningOrSpending(bool secondPartExists)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var tool = entities.SpawnEntity("TestBatchRepairTool", map.GridCoords);
            var data = entities.EnsureComponent<ShipRepairDataComponent>(map.Grid);
            data.EntityPalette = ["TestBatchRepairPart"];
            var chunk = new ShipRepairChunk { Tiles = new int[data.ChunkSize * data.ChunkSize] };
            chunk.Entities[0] = new ShipRepairEntitySpecifier();
            chunk.Entities[1] = new ShipRepairEntitySpecifier();
            if (secondPartExists)
                chunk.Entities[1].OriginalEntity = entities.GetNetEntity(entities.SpawnEntity("TestBatchRepairPart", map.GridCoords));
            data.Chunks[Vector2i.Zero] = chunk;
            var repair = new ShipRepairDoAfterEvent
            {
                TargetGrid = map.Grid,
                TargetGridIndices = Vector2i.Zero,
                RepairId = [0, 1],
                Cost = 4,
            };
            Assert.That(server.System<SharedDoAfterSystem>().TryStartDoAfter(new DoAfterArgs(entities, user, 0f, repair, tool)), Is.True);
            Assert.That(entities.GetComponent<LimitedChargesComponent>(tool).Charges, Is.EqualTo(secondPartExists ? 10 : 6));
            Assert.That(chunk.Entities[0].OriginalEntity.HasValue, Is.EqualTo(!secondPartExists));
            Assert.That(chunk.Entities[1].OriginalEntity.HasValue, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RepairSelectionDoesNotExceedAvailableCharges()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var tool = entities.SpawnEntity("TestBatchRepairTool", map.GridCoords);
            server.System<SharedChargesSystem>().UseCharges(tool, 7);
            var data = entities.EnsureComponent<ShipRepairDataComponent>(map.Grid);
            data.EntityPalette = ["TestBatchRepairPart"];
            var chunk = new ShipRepairChunk { Tiles = new int[data.ChunkSize * data.ChunkSize] };
            chunk.Entities[0] = new ShipRepairEntitySpecifier();
            chunk.Entities[1] = new ShipRepairEntitySpecifier();
            data.Chunks[Vector2i.Zero] = chunk;
            entities.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(user, tool, null, map.GridCoords, true));
            var doAfters = entities.GetComponent<DoAfterComponent>(user).DoAfters;
            Assert.That(doAfters.Count, Is.EqualTo(1));
            foreach (var action in doAfters.Values)
            {
                var repair = (ShipRepairDoAfterEvent)action.Args.Event;
                Assert.That(repair.Cost, Is.EqualTo(2));
                Assert.That(repair.RepairId, Has.Count.EqualTo(1));
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public void DuplicateRepairComparesTargetIdsByValue()
    {
        var first = new ShipRepairDoAfterEvent { RepairId = [1, 2] };
        Assert.That(first.IsDuplicate(new ShipRepairDoAfterEvent { RepairId = [1, 2] }), Is.True);
        Assert.That(first.IsDuplicate(new ShipRepairDoAfterEvent { RepairId = [1] }), Is.False);
        Assert.That(first.IsDuplicate(new ShipRepairDoAfterEvent()), Is.False);
        Assert.That(new ShipRepairDoAfterEvent().IsDuplicate(new ShipRepairDoAfterEvent()), Is.True);
    }
}
