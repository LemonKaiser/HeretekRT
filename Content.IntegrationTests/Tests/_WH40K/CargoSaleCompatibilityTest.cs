using Content.Server._Mono.Store.Components;
using Content.Server.Cargo.Systems;
using Content.Shared.Cargo.Events;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Reflection;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class CargoSaleCompatibilityTest
{
    [TestPrototypes]
    public const string Prototypes = """
        - type: entity
          parent: BaseItem
          id: TestLimitedSaleCargo
          components:
          - type: StaticPrice
            price: 10
          - type: SoldAlert
            maxPerSale: 1
            channels: []
            groupId: TestLimitedSaleCargo

        - type: entity
          parent: TestLimitedSaleCargo
          id: TestLimitedSaleInjectedCargo
          components:
          - type: CurrencyInjectionOnSell
            company: PDV
            amount:
              Telecrystal: 1
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task PalletSaleHonorsLimitWithoutDeletingOrRecordingRejectedGoods(bool overLimit)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            entities.SpawnEntity("CargoPalletSell", coordinates);
            var console = entities.SpawnEntity("ComputerPalletConsoleNFNormalMarket", coordinates);
            var prototype = overLimit ? "TestLimitedSaleInjectedCargo" : "TestLimitedSaleCargo";
            var first = entities.SpawnEntity(prototype, coordinates);
            EntityUid? second = overLimit ? entities.SpawnEntity(prototype, coordinates) : null;
            var observer = server.System<CargoSaleTestSystem>();
            observer.Grid = map.Grid;
            observer.Sales = 0;
            try
            {
                entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
                Assert.That(observer.Sales, Is.EqualTo(overLimit ? 0 : 1));
                Assert.That(entities.EntityExists(first), Is.EqualTo(overLimit));
                if (overLimit)
                {
                    Assert.That(entities.EntityExists(second!.Value), Is.True);
                    entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
                    Assert.That(observer.Sales, Is.EqualTo(0));
                    Assert.That(entities.GetComponent<CurrencyInjectionOnSellComponent>(first).Amount["Telecrystal"], Is.EqualTo(FixedPoint2.New(1)));
                    Assert.That(entities.GetComponent<CurrencyInjectionOnSellComponent>(second.Value).Amount["Telecrystal"], Is.EqualTo(FixedPoint2.New(1)));
                }
            }
            finally
            {
                observer.Grid = null;
            }
        });
        await pair.CleanReturnAsync();
    }
}

[Reflect(false)]
public sealed class CargoSaleTestSystem : EntitySystem
{
    public EntityUid? Grid;
    public int Sales;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySoldEvent>(OnSold);
    }

    private void OnSold(ref EntitySoldEvent args)
    {
        if (args.Grid == Grid)
            Sales++;
    }
}
